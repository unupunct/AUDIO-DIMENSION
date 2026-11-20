import XCTest
import AVFoundation

struct DecodedVideo { var lumas: [Double]; var width: Int; var height: Int; var duration: Double; var audioDuration: Double; var audioTracks: Int }

func decode(_ url: URL) -> DecodedVideo? {
    let asset = AVURLAsset(url: url)
    guard let vt = asset.tracks(withMediaType: .video).first, let reader = try? AVAssetReader(asset: asset) else { return nil }
    let out = AVAssetReaderTrackOutput(track: vt, outputSettings: [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA])
    reader.add(out)
    guard reader.startReading() else { return nil }
    var lumas: [Double] = []
    while let sb = out.copyNextSampleBuffer() {
        guard let pb = CMSampleBufferGetImageBuffer(sb) else { continue }
        CVPixelBufferLockBaseAddress(pb, .readOnly)
        let w = CVPixelBufferGetWidth(pb), h = CVPixelBufferGetHeight(pb), stride = CVPixelBufferGetBytesPerRow(pb)
        let base = CVPixelBufferGetBaseAddress(pb)!.assumingMemoryBound(to: UInt8.self)
        var sum = 0.0, n = 0.0
        for y in stride_(0, h, 3) { for x in stride_(0, w, 3) { let i = y * stride + x * 4; sum += 0.114 * Double(base[i]) + 0.587 * Double(base[i + 1]) + 0.299 * Double(base[i + 2]); n += 1 } }
        CVPixelBufferUnlockBaseAddress(pb, .readOnly)
        lumas.append(sum / n)
    }
    let size = vt.naturalSize
    let at = asset.tracks(withMediaType: .audio)
    return DecodedVideo(lumas: lumas, width: Int(size.width), height: Int(size.height), duration: CMTimeGetSeconds(vt.timeRange.duration),
                        audioDuration: at.first.map { CMTimeGetSeconds($0.timeRange.duration) } ?? 0, audioTracks: at.count)
}

func stride_(_ a: Int, _ b: Int, _ s: Int) -> StrideTo<Int> { stride(from: a, to: b, by: s) }

final class ExportTests: XCTestCase {
    var preset: Preset { PresetLibrary(resources: TestPaths.resources, customFolder: TestPaths.temp(), defaults: TestPaths.defaults()).builtIn.first { $0.visualizerId == "classic-spectrum" }! }

    func export(_ audio: URL, _ s: ExportSettings, preset: Preset? = nil, progress: @escaping (Double) -> Bool = { _ in true }) throws -> URL {
        let out = TestPaths.temp().appendingPathComponent("out.mp4")
        try VideoExporter.export(audioURL: audio, preset: preset ?? self.preset, settings: s, outputURL: out, progress: progress)
        return out
    }

    func testExportsPlayableMP4WithAudioAndCorrectFrames() {
        let wav = TestPaths.temp().appendingPathComponent("a.wav")
        try! Signal.writeWAV(Signal.kicks(seconds: 2, period: 0.5), to: wav)
        do {
            let url = try export(wav, ExportSettings(width: 320, height: 180, fps: 24))
            guard let v = decode(url) else { return XCTFail("cannot decode output") }
            XCTAssertEqual(v.width, 320); XCTAssertEqual(v.height, 180)
            XCTAssertEqual(v.lumas.count, 48, "2 s at 24 fps")
            XCTAssertEqual(v.audioTracks, 1)
            XCTAssertEqual(v.duration, 2.0, accuracy: 0.1)
            XCTAssertEqual(v.audioDuration, 2.0, accuracy: 0.15)
            XCTAssertTrue(FileManager.default.fileExists(atPath: url.path))
            XCTAssertFalse(FileManager.default.fileExists(atPath: url.deletingPathExtension().appendingPathExtension("partial.mp4").path), "no partial file left")
        } catch { XCTFail("export threw \(error)") }
    }

    func testPictureIsSyncedToTheSound() {
        // 1.0 s of silence, then loud audio. The picture must change at 1.0 s (within a few frames), not before.
        let fps = 24
        let wav = TestPaths.temp().appendingPathComponent("sync.wav")
        let sig = Signal.silence(seconds: 1.0) + Signal.noise(seconds: 1.5, amp: 0.5)
        try! Signal.writeWAV(sig, to: wav)
        do {
            let v = decode(try export(wav, ExportSettings(width: 320, height: 180, fps: fps)))!
            let base = v.lumas[0...10].reduce(0, +) / 11
            XCTAssertTrue((v.lumas[0...20].max()! - v.lumas[0...20].min()!) < 1.0, "picture must stay still during silence")
            guard let idx = v.lumas.firstIndex(where: { $0 > base + 2 }) else { return XCTFail("picture never reacted") }
            XCTAssertTrue(idx >= fps - 1 && idx <= fps + 3, "reaction at frame \(idx), expected about \(fps)")
            XCTAssertEqual(Double(v.lumas.count), 2.5 * Double(fps), accuracy: 1)
        } catch { XCTFail("export threw \(error)") }
    }

    func testAllFrameRatesAndPortraitSize() {
        let wav = TestPaths.temp().appendingPathComponent("b.wav")
        try! Signal.writeWAV(Signal.kicks(seconds: 1, period: 0.5), to: wav)
        for (w, h, fps) in [(180, 320, 30), (200, 200, 60), (256, 144, 24), (256, 144, 25)] {
            do {
                let v = decode(try export(wav, ExportSettings(width: w, height: h, fps: fps)))!
                XCTAssertEqual(v.lumas.count, fps, "\(w)x\(h)@\(fps)")
                XCTAssertEqual(v.width, w); XCTAssertEqual(v.height, h)
            } catch { XCTFail("\(w)x\(h)@\(fps) threw \(error)") }
        }
    }

    func testCompressedAndStereoInput() {
        // m4a (AAC) input
        let m4a = TestPaths.temp().appendingPathComponent("c.m4a")
        do {
            let fmt = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 44100, channels: 2, interleaved: false)!
            let file = try AVAudioFile(forWriting: m4a, settings: [AVFormatIDKey: kAudioFormatMPEG4AAC, AVSampleRateKey: 44100, AVNumberOfChannelsKey: 2])
            let samples = Signal.kicks(seconds: 1, period: 0.5, sr: 44100)
            let buf = AVAudioPCMBuffer(pcmFormat: fmt, frameCapacity: AVAudioFrameCount(samples.count))!
            buf.frameLength = AVAudioFrameCount(samples.count)
            for i in 0..<samples.count { buf.floatChannelData![0][i] = samples[i]; buf.floatChannelData![1][i] = 0 }   // right channel silent
            try file.write(from: buf)
        } catch { return XCTFail("could not create m4a: \(error)") }
        do {
            let v = decode(try export(m4a, ExportSettings(width: 256, height: 144, fps: 24)))!
            XCTAssertEqual(v.lumas.count, 24)
            XCTAssertEqual(v.audioTracks, 1)
            XCTAssertTrue(v.lumas.max()! - v.lumas.min()! > 1, "picture should react to the left channel only audio")
        } catch { XCTFail("m4a export threw \(error)") }
    }

    func testCancelLeavesNoFiles() {
        let wav = TestPaths.temp().appendingPathComponent("d.wav")
        try! Signal.writeWAV(Signal.noise(seconds: 3), to: wav)
        let out = TestPaths.temp().appendingPathComponent("cancel.mp4")
        do {
            try VideoExporter.export(audioURL: wav, preset: preset, settings: ExportSettings(width: 256, height: 144, fps: 24), outputURL: out) { $0 < 0.3 }
            XCTFail("expected cancellation")
        } catch let e as ExportError { if case .cancelled = e {} else { XCTFail("wrong error \(e)") } }
        catch { XCTFail("wrong error \(error)") }
        XCTAssertFalse(FileManager.default.fileExists(atPath: out.path))
        XCTAssertFalse(FileManager.default.fileExists(atPath: out.deletingPathExtension().appendingPathExtension("partial.mp4").path))
    }

    func testBadInputsGiveClearErrors() {
        let junk = TestPaths.temp().appendingPathComponent("junk.wav")
        try? Data("this is not audio".utf8).write(to: junk)
        XCTAssertThrowsError(try export(junk, ExportSettings(width: 256, height: 144, fps: 24)))
        XCTAssertThrowsError(try export(URL(fileURLWithPath: "/nonexistent/file.wav"), ExportSettings(width: 256, height: 144, fps: 24)))
        let wav = TestPaths.temp().appendingPathComponent("e.wav")
        try! Signal.writeWAV(Signal.tone(440, seconds: 0.5), to: wav)
        XCTAssertThrowsError(try export(wav, ExportSettings(width: 255, height: 144, fps: 24)), "odd width")
        XCTAssertThrowsError(try export(wav, ExportSettings(width: 256, height: 144, fps: 500)), "absurd fps")
        let empty = TestPaths.temp().appendingPathComponent("empty.wav")
        try! Signal.writeWAV([], to: empty)
        XCTAssertThrowsError(try export(empty, ExportSettings(width: 256, height: 144, fps: 24)))
    }

    func testExportIsDeterministic() {
        let wav = TestPaths.temp().appendingPathComponent("f.wav")
        try! Signal.writeWAV(Signal.kicks(seconds: 1, period: 0.5), to: wav)
        do {
            let a = decode(try export(wav, ExportSettings(width: 256, height: 144, fps: 24)))!.lumas
            let b = decode(try export(wav, ExportSettings(width: 256, height: 144, fps: 24)))!.lumas
            XCTAssertEqual(a.count, b.count)
            var maxDiff = 0.0
            for i in 0..<min(a.count, b.count) { maxDiff = max(maxDiff, abs(a[i] - b[i])) }
            XCTAssertTrue(maxDiff < 0.5, "two renders of the same audio differ by \(maxDiff)")
        } catch { XCTFail("threw \(error)") }
    }

    func testRecordingFileRoundTrip() {
        // Same writer settings AudioCapture uses for recordings: a mono float WAV must be readable by the exporter.
        let url = TestPaths.temp().appendingPathComponent("rec.wav")
        let fmt = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 44100, channels: 1, interleaved: false)!
        do {
            let file = try AVAudioFile(forWriting: url, settings: fmt.settings, commonFormat: .pcmFormatFloat32, interleaved: false)
            XCTAssertNotNil(file)
        } catch { return XCTFail("\(error)") }
        try! Signal.writeWAV(Signal.tone(440, seconds: 1, sr: 44100), sr: 44100, to: url)
        XCTAssertNoThrow(try export(url, ExportSettings(width: 256, height: 144, fps: 24)))
    }
}
