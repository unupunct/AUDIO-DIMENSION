import Foundation
import AVFoundation

// Test-only signal generators. The app itself never synthesises audio; these exist to give the analyser, renderer and exporter
// known input so their output can be checked.
enum Signal {
    static func tone(_ freq: Double, seconds: Double, sr: Double = 48000, amp: Float = 0.5) -> [Float] {
        let n = Int(seconds * sr)
        return (0..<n).map { amp * Float(sin(2 * Double.pi * freq * Double($0) / sr)) }
    }
    static func noise(seconds: Double, sr: Double = 48000, amp: Float = 0.4, seed: UInt64 = 1) -> [Float] {
        var g = SplitMix(seed: seed)
        return (0..<Int(seconds * sr)).map { _ in (g.float() * 2 - 1) * amp }
    }
    static func silence(seconds: Double, sr: Double = 48000) -> [Float] { [Float](repeating: 0, count: Int(seconds * sr)) }
    /// Kick-like bursts: a 60 Hz tone with a sharp envelope every `period` seconds.
    static func kicks(seconds: Double, period: Double, sr: Double = 48000) -> [Float] {
        let n = Int(seconds * sr)
        var out = [Float](repeating: 0, count: n)
        let twoPi = 2 * Double.pi
        for i in 0..<n {
            let t: Double = Double(i) / sr
            let ph: Double = t.truncatingRemainder(dividingBy: period)
            let low: Double = exp(-ph * 14) * sin(twoPi * 60 * ph)
            let high: Double = exp(-ph * 30) * sin(twoPi * 3000 * ph)
            out[i] = Float(0.8 * low + 0.15 * high)
        }
        return out
    }

    static func writeWAV(_ samples: [Float], sr: Double = 48000, to url: URL) throws {
        let fmt = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: sr, channels: 1, interleaved: false)!
        let file = try AVAudioFile(forWriting: url, settings: fmt.settings, commonFormat: .pcmFormatFloat32, interleaved: false)
        let buf = AVAudioPCMBuffer(pcmFormat: fmt, frameCapacity: AVAudioFrameCount(samples.count))!
        buf.frameLength = AVAudioFrameCount(samples.count)
        for i in 0..<samples.count { buf.floatChannelData![0][i] = samples[i] }
        try file.write(from: buf)
    }
}

enum TestPaths {
    static var resources: URL { URL(fileURLWithPath: ProcessInfo.processInfo.environment["ADMAC_RESOURCES"] ?? "Resources") }
    static func temp(_ name: String = UUID().uuidString) -> URL {
        let u = FileManager.default.temporaryDirectory.appendingPathComponent("admac-tests").appendingPathComponent(name)
        try? FileManager.default.createDirectory(at: u, withIntermediateDirectories: true)
        return u
    }
    static func allPresets() -> [Preset] {
        let data = try! Data(contentsOf: resources.appendingPathComponent("builtin-presets.json"))
        return try! PresetValidator.parse(data)
    }
    static func preset(_ id: String) -> Preset { allPresets().first { $0.id == id }! }
    static func defaults() -> UserDefaults { UserDefaults(suiteName: "ad.test.\(UUID().uuidString)")! }
}

/// Feeds samples through an analyser at a fixed frame rate and returns every frame.
func analyze(_ samples: [Float], sr: Double = 48000, fps: Double = 60, settings: AnalyzerSettings = AnalyzerSettings()) -> [AudioFrame] {
    let a = AudioAnalyzer(sampleRate: sr)
    a.settings = settings
    let step = Int(sr / fps)
    var frames: [AudioFrame] = []
    var i = 0
    while i + step <= samples.count {
        a.push(Array(samples[i..<(i + step)]))
        frames.append(a.analyze(dt: 1 / fps))
        i += step
    }
    return frames
}
