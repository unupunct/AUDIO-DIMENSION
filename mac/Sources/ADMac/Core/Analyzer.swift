import Foundation
import Accelerate

/// User-facing audio response settings (same meaning as the Windows app's audio section of a preset).
public struct AnalyzerSettings: Equatable {
    public var sensitivity: Float = 1
    public var smoothing: Float = 0.6
    public var bassSensitivity: Float = 1
    public var midSensitivity: Float = 1
    public var trebleSensitivity: Float = 1
    public var tilt: Float = 0
    public var autoGain = true
    public var peakDetection = true
    /// sub-bass, bass, low-mid, mid, high-mid, treble
    public var bandEnabled = [Bool](repeating: true, count: 6)
    public init() {}
}

/// One analysed slice of audio, everything normalised to 0...1.
public struct AudioFrame {
    public static let spectrumBins = 64
    public static let waveformSamples = 256
    public var spectrum = [Float](repeating: 0, count: AudioFrame.spectrumBins)
    public var bands = [Float](repeating: 0, count: 6)
    public var waveform = [Float](repeating: 0, count: AudioFrame.waveformSamples)
    public var level: Float = 0
    public var peak: Float = 0
    /// 1 on a detected beat, decaying afterwards.
    public var beat: Float = 0
    public var time: Double = 0
    public init() {}
}

/// Real FFT analyser (Accelerate). Thread-safe `push` from the audio thread, `analyze` from the render thread.
/// Fully deterministic: the same samples and the same `dt` sequence always give the same frames (needed for offline export).
public final class AudioAnalyzer {
    public static let fftSize = 2048
    public static let bandEdges: [Float] = [20, 60, 250, 500, 2000, 4000, 16000]

    public var settings = AnalyzerSettings()
    public private(set) var sampleRate: Double

    private let n = AudioAnalyzer.fftSize
    private let log2n = vDSP_Length(11)
    private var fft: FFTSetup
    private var window: [Float]
    private let inBuf: UnsafeMutablePointer<Float>
    private let realp: UnsafeMutablePointer<Float>
    private let imagp: UnsafeMutablePointer<Float>
    private var ring: [Float]
    private var writeIndex = 0
    private var filled = 0
    private let lock = NSLock()

    private var binRanges: [(Int, Int)] = []
    private var binCenters: [Float] = []
    private var bandBins: [[Int]] = []

    private var smoothSpectrum = [Float](repeating: 0, count: AudioFrame.spectrumBins)
    private var peakTracker: Float = 0.05
    private var gain: Float = 1
    private var peakHold: Float = 0
    private var bassAvg: Float = 0
    private var beatPulse: Float = 0
    private var lastBeat: Double = -10
    private var time: Double = 0

    public init(sampleRate: Double = 48000) {
        self.sampleRate = sampleRate
        fft = vDSP_create_fftsetup(log2n, FFTRadix(kFFTRadix2))!
        window = [Float](repeating: 0, count: AudioAnalyzer.fftSize)
        vDSP_hann_window(&window, vDSP_Length(AudioAnalyzer.fftSize), Int32(vDSP_HANN_NORM))
        // vDSP_HANN_NORM scales for unity power; use the plain window so a unit sine reads as amplitude 1.
        for i in 0..<AudioAnalyzer.fftSize {
            window[i] = 0.5 - 0.5 * cosf(2 * .pi * Float(i) / Float(AudioAnalyzer.fftSize))
        }
        inBuf = .allocate(capacity: AudioAnalyzer.fftSize)
        realp = .allocate(capacity: AudioAnalyzer.fftSize / 2)
        imagp = .allocate(capacity: AudioAnalyzer.fftSize / 2)
        ring = [Float](repeating: 0, count: AudioAnalyzer.fftSize)
        buildBins()
    }

    deinit {
        vDSP_destroy_fftsetup(fft)
        inBuf.deallocate(); realp.deallocate(); imagp.deallocate()
    }

    public func setSampleRate(_ sr: Double) {
        lock.lock(); defer { lock.unlock() }
        guard sr > 8000, sr != sampleRate else { return }
        sampleRate = sr
        buildBins()
    }

    public func reset() {
        lock.lock(); defer { lock.unlock() }
        for i in 0..<ring.count { ring[i] = 0 }
        writeIndex = 0; filled = 0
        for i in 0..<smoothSpectrum.count { smoothSpectrum[i] = 0 }
        peakTracker = 0.05; gain = 1; peakHold = 0; bassAvg = 0; beatPulse = 0; lastBeat = -10; time = 0
    }

    private func buildBins() {
        let count = AudioFrame.spectrumBins
        let fMin: Float = 30, fMax: Float = min(16000, Float(sampleRate) / 2 * 0.95)
        let ratio = fMax / fMin
        let perBin = Float(sampleRate) / Float(n)
        var edges = [Float](repeating: 0, count: count + 1)
        for i in 0...count { edges[i] = fMin * powf(ratio, Float(i) / Float(count)) }
        binRanges = []; binCenters = []
        for i in 0..<count {
            let lo = max(1, Int(edges[i] / perBin))
            let hi = min(n / 2 - 1, max(lo + 1, Int(ceilf(edges[i + 1] / perBin))))
            binRanges.append((lo, hi))
            binCenters.append(sqrtf(edges[i] * edges[i + 1]))
        }
        bandBins = (0..<6).map { b in
            let lo = AudioAnalyzer.bandEdges[b], hi = AudioAnalyzer.bandEdges[b + 1]
            var idx = binCenters.indices.filter { binCenters[$0] >= lo && binCenters[$0] < hi }
            if idx.isEmpty {
                let mid = sqrtf(lo * hi)
                idx = [binCenters.indices.min(by: { abs(binCenters[$0] - mid) < abs(binCenters[$1] - mid) })!]
            }
            return idx
        }
    }

    /// Append mono samples (called from the audio thread).
    public func push(_ samples: UnsafePointer<Float>, count: Int) {
        lock.lock(); defer { lock.unlock() }
        for i in 0..<count {
            ring[writeIndex] = samples[i]
            writeIndex += 1
            if writeIndex == n { writeIndex = 0 }
        }
        filled = min(n, filled + count)
    }

    public func push(_ samples: [Float]) {
        samples.withUnsafeBufferPointer { if let b = $0.baseAddress { push(b, count: $0.count) } }
    }

    private func bandOfBin(_ i: Int) -> Int {
        let f = binCenters[i]
        for b in 0..<6 where f < AudioAnalyzer.bandEdges[b + 1] { return b }
        return 5
    }

    /// Produce the analysis for the most recent audio. `dt` is the time since the previous call.
    public func analyze(dt: Double) -> AudioFrame {
        lock.lock(); defer { lock.unlock() }
        let s = settings
        let dtf = Float(max(dt, 0.0001))
        time += dt
        var frame = AudioFrame()
        frame.time = time

        // Oldest-to-newest copy of the ring.
        for i in 0..<n { inBuf[i] = ring[(writeIndex + i) % n] }

        // Level (RMS of the newest 1024 samples).
        var rms: Float = 0
        vDSP_rmsqv(inBuf + (n - 1024), 1, &rms, 1024)
        let gated = filled == 0 || rms < 1e-4

        // Waveform (newest 1024 decimated to 256), scaled by sensitivity.
        for i in 0..<AudioFrame.waveformSamples {
            frame.waveform[i] = max(-1, min(1, inBuf[n - 1024 + i * 4] * s.sensitivity * gain.clamped(0.5, 4)))
        }

        if gated {
            for i in 0..<AudioFrame.spectrumBins {
                smoothSpectrum[i] *= powf(max(s.smoothing, 0.5), dtf * 60)
            }
            beatPulse *= expf(-dtf * 7)
            peakHold = max(0, peakHold - dtf * 0.5)
            frame.spectrum = smoothSpectrum
            frame.bands = bandValues(from: smoothSpectrum, settings: s)
            frame.level = 0; frame.peak = peakHold; frame.beat = beatPulse
            return frame
        }

        // Window + real FFT.
        vDSP_vmul(inBuf, 1, window, 1, inBuf, 1, vDSP_Length(n))
        var split = DSPSplitComplex(realp: realp, imagp: imagp)
        UnsafeRawPointer(inBuf).withMemoryRebound(to: DSPComplex.self, capacity: n / 2) {
            vDSP_ctoz($0, 2, &split, 1, vDSP_Length(n / 2))
        }
        vDSP_fft_zrip(fft, &split, 1, log2n, FFTDirection(kFFTDirection_Forward))

        // Linear amplitude per FFT bin (a unit sine with a Hann window gives mag = N/4 * 2 after zrip scaling).
        var amp = [Float](repeating: 0, count: n / 2)
        let scale = 4 / Float(n)
        for k in 1..<(n / 2) { amp[k] = sqrtf(realp[k] * realp[k] + imagp[k] * imagp[k]) * scale }

        // Per log-bin amplitude with sensitivity, frequency tilt and per-range sensitivity.
        var bins = [Float](repeating: 0, count: AudioFrame.spectrumBins)
        var maxAmp: Float = 0
        for i in 0..<AudioFrame.spectrumBins {
            let (lo, hi) = binRanges[i]
            var m: Float = 0
            for k in lo..<hi { m = max(m, amp[k]) }
            let band = bandOfBin(i)
            let rangeSens: Float = band <= 1 ? s.bassSensitivity : (band <= 3 ? s.midSensitivity : s.trebleSensitivity)
            let tilt = 1 + s.tilt * log2f(binCenters[i] / 1000) / 4
            m *= s.sensitivity * rangeSens * max(0.05, tilt)
            if !s.bandEnabled[band] { m = 0 }
            bins[i] = m
            maxAmp = max(maxAmp, m)
        }

        // Automatic gain: slow follower of the loudest bin.
        if s.autoGain {
            peakTracker = max(maxAmp / max(gain, 0.001), peakTracker * expf(-dtf / 4))
            peakTracker = max(peakTracker, 0.01)
            let target = (0.30 / peakTracker).clamped(0.6, 12)
            gain += (target - gain) * min(1, dtf * 2)
        } else {
            gain = 1
        }

        var raw = [Float](repeating: 0, count: AudioFrame.spectrumBins)
        for i in 0..<AudioFrame.spectrumBins {
            let db = 20 * log10f(bins[i] * gain + 1e-9)
            raw[i] = ((db + 70) / 70).clamped(0, 1)
        }

        // Fast attack, smooth release.
        let rel = powf(s.smoothing.clamped(0, 0.99), dtf * 60)
        for i in 0..<AudioFrame.spectrumBins {
            if raw[i] > smoothSpectrum[i] { smoothSpectrum[i] += (raw[i] - smoothSpectrum[i]) * 0.75 }
            else { smoothSpectrum[i] = smoothSpectrum[i] * rel + raw[i] * (1 - rel) }
        }
        frame.spectrum = smoothSpectrum
        let rawBands = bandValues(from: raw, settings: s)
        frame.bands = bandValues(from: smoothSpectrum, settings: s)

        // Level and peak.
        let db = 20 * log10f(rms * s.sensitivity + 1e-9)
        frame.level = ((db + 60) / 60).clamped(0, 1)
        peakHold = max(frame.level, peakHold - dtf * 0.5)
        frame.peak = peakHold

        // Beat detection on bass energy against a slow average.
        let bass = (rawBands[0] + rawBands[1]) / 2
        bassAvg += (bass - bassAvg) * (1 - expf(-dtf / 0.8))
        if s.peakDetection, bass > bassAvg * 1.35 + 0.05, bass > 0.25, time - lastBeat > 0.18 {
            beatPulse = 1; lastBeat = time
        } else {
            beatPulse *= expf(-dtf * 7)
        }
        frame.beat = beatPulse
        return frame
    }

    private var meterPeak: Float = 0

    /// Cheap level/peak read for the UI meter (does not advance the analysis state).
    public func meter() -> (Float, Float) {
        lock.lock(); defer { lock.unlock() }
        var sum: Float = 0
        for i in 0..<1024 { let v = ring[(writeIndex + n - 1024 + i) % n]; sum += v * v }
        let rms = sqrtf(sum / 1024)
        let level = ((20 * log10f(rms * settings.sensitivity + 1e-9) + 60) / 60).clamped(0, 1)
        meterPeak = max(level, meterPeak - 0.03)
        return (level, meterPeak)
    }

    private func bandValues(from bins: [Float], settings s: AnalyzerSettings) -> [Float] {
        (0..<6).map { b in
            if !s.bandEnabled[b] { return 0 }
            // Half average, half loudest bin: a pure tone still registers in a wide band, noise still reads as broad energy.
            let idx = bandBins[b]
            let mean = idx.reduce(0) { $0 + bins[$1] } / Float(idx.count)
            let peak = idx.reduce(0) { max($0, bins[$1]) }
            return (mean + peak) / 2
        }
    }
}

extension Comparable {
    func clamped(_ lo: Self, _ hi: Self) -> Self { min(max(self, lo), hi) }
}
