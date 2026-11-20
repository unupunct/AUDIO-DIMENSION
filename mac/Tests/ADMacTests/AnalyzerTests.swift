import XCTest

final class AnalyzerTests: XCTestCase {
    func testSilenceGivesZero() {
        let f = analyze(Signal.silence(seconds: 1)).last!
        XCTAssertEqual(f.level, 0, accuracy: 0.0001)
        XCTAssertTrue(f.spectrum.allSatisfy { $0 < 0.001 })
        XCTAssertEqual(f.beat, 0, accuracy: 0.001)
    }

    func testBassToneLandsInBassBands() {
        var s = AnalyzerSettings(); s.autoGain = false
        let f = analyze(Signal.tone(100, seconds: 1), settings: s).last!
        XCTAssertTrue(f.bands[1] > 0.5, "bass band \(f.bands[1])")
        XCTAssertTrue(f.bands[1] > f.bands[5] + 0.4, "bass \(f.bands[1]) vs treble \(f.bands[5])")
        let peakBin = f.spectrum.firstIndex(of: f.spectrum.max()!)!
        XCTAssertTrue(peakBin < 20, "peak bin \(peakBin)")
    }

    func testMidAndTrebleTonesLandInTheRightBands() {
        var s = AnalyzerSettings(); s.autoGain = false
        let hi = analyze(Signal.tone(8000, seconds: 1), settings: s).last!
        XCTAssertTrue(hi.bands[5] > hi.bands[1] + 0.3, "treble tone: \(hi.bands)")
        let mid = analyze(Signal.tone(3000, seconds: 1), settings: s).last!
        XCTAssertEqual(mid.bands.firstIndex(of: mid.bands.max()!)!, 4, "3 kHz should be high-mid: \(mid.bands)")
    }

    func testAmplitudeCalibration() {
        // A 0.5 amplitude sine should read near (20*log10(0.5)+70)/70 = 0.914 at its bin with auto gain off.
        var s = AnalyzerSettings(); s.autoGain = false
        let f = analyze(Signal.tone(1000, seconds: 1, amp: 0.5), settings: s).last!
        XCTAssertEqual(f.spectrum.max()!, 0.914, accuracy: 0.08)
    }

    func testLouderIsHigher() {
        var s = AnalyzerSettings(); s.autoGain = false
        let quiet = analyze(Signal.tone(440, seconds: 1, amp: 0.02), settings: s).last!
        let loud = analyze(Signal.tone(440, seconds: 1, amp: 0.6), settings: s).last!
        XCTAssertGreaterThan(loud.level, quiet.level + 0.2)
        XCTAssertGreaterThan(loud.spectrum.max()!, quiet.spectrum.max()! + 0.2)
    }

    func testAutoGainRaisesQuietSignal() {
        var off = AnalyzerSettings(); off.autoGain = false
        var on = AnalyzerSettings(); on.autoGain = true
        let q = Signal.tone(440, seconds: 6, amp: 0.02)
        XCTAssertGreaterThan(analyze(q, settings: on).last!.spectrum.max()!, analyze(q, settings: off).last!.spectrum.max()! + 0.15)
    }

    func testDisabledBandIsZero() {
        var s = AnalyzerSettings(); s.autoGain = false; s.bandEnabled[1] = false
        let f = analyze(Signal.tone(100, seconds: 1), settings: s).last!
        XCTAssertEqual(f.bands[1], 0, accuracy: 0.0001)
    }

    func testSensitivityScalesResponse() {
        var lo = AnalyzerSettings(); lo.autoGain = false; lo.sensitivity = 0.3
        var hi = AnalyzerSettings(); hi.autoGain = false; hi.sensitivity = 3
        let t = Signal.tone(440, seconds: 1, amp: 0.1)
        XCTAssertGreaterThan(analyze(t, settings: hi).last!.spectrum.max()!, analyze(t, settings: lo).last!.spectrum.max()! + 0.2)
    }

    func testBeatDetectionFindsKicks() {
        let frames = analyze(Signal.kicks(seconds: 4, period: 0.5))
        var count = 0, prev: Float = 0
        for f in frames { if f.beat > 0.95 && prev <= 0.95 { count += 1 }; prev = f.beat }
        XCTAssertTrue(count >= 5 && count <= 10, "beats detected: \(count)")
    }

    func testSteadyToneIsNotAStreamOfBeats() {
        var count = 0, prev: Float = 0
        for f in analyze(Signal.tone(440, seconds: 3)) { if f.beat > 0.95 && prev <= 0.95 { count += 1 }; prev = f.beat }
        XCTAssertTrue(count <= 1, "beats in a steady tone: \(count)")
    }

    func testBeatDetectionCanBeDisabled() {
        var s = AnalyzerSettings(); s.peakDetection = false
        XCTAssertEqual(analyze(Signal.kicks(seconds: 3, period: 0.5), settings: s).filter { $0.beat > 0.95 }.count, 0)
    }

    func testDeterministic() {
        let sig = Signal.noise(seconds: 1) + Signal.kicks(seconds: 1, period: 0.5)
        let a = analyze(sig), b = analyze(sig)
        XCTAssertEqual(a.count, b.count)
        var same = true
        for i in 0..<a.count where a[i].spectrum != b[i].spectrum || a[i].bands != b[i].bands || a[i].beat != b[i].beat || a[i].waveform != b[i].waveform { same = false }
        XCTAssertTrue(same, "analysis must be reproducible for offline export")
    }

    func testValuesStayInRangeForExtremeInput() {
        let loud = Signal.noise(seconds: 1, amp: 5)
        var ok = true
        for f in analyze(loud) {
            if !f.spectrum.allSatisfy({ $0 >= 0 && $0 <= 1 && !$0.isNaN }) { ok = false }
            if !f.bands.allSatisfy({ $0 >= 0 && $0 <= 1 }) { ok = false }
            if !f.waveform.allSatisfy({ abs($0) <= 1 }) { ok = false }
            if !(f.level >= 0 && f.level <= 1) { ok = false }
        }
        XCTAssertTrue(ok, "all values must stay within 0...1")
    }

    func testSampleRateChangeRebuildsBins() {
        let a = AudioAnalyzer(sampleRate: 44100)
        a.setSampleRate(96000)
        XCTAssertEqual(a.sampleRate, 96000, accuracy: 0.1)
        a.push(Signal.tone(1000, seconds: 0.5, sr: 96000))
        let f = a.analyze(dt: 1.0 / 60)
        XCTAssertGreaterThan(f.spectrum.max()!, 0.3)
    }

    func testDeviceListIsWellFormed() {
        for d in AudioDevices.inputDevices() { XCTAssertTrue(d.inputChannels > 0 && !d.name.isEmpty) }
    }
}
