import Foundation
import AVFoundation
import CoreVideo

public struct ExportSettings: Equatable {
    public var width = 1920
    public var height = 1080
    public var fps = 30
    public init(width: Int = 1920, height: Int = 1080, fps: Int = 30) { self.width = width; self.height = height; self.fps = fps }
}

public enum ExportError: Error, LocalizedError {
    case cancelled
    case unreadableAudio(String)
    case writer(String)
    case noSpace(String)
    public var errorDescription: String? {
        switch self {
        case .cancelled: return "Export cancelled."
        case .unreadableAudio(let m): return "The audio file could not be read: \(m)"
        case .writer(let m): return "Video export failed: \(m)"
        case .noSpace(let m): return m
        }
    }
}

/// Offline, deterministic video render: audio file -> analyser at exactly 1/fps -> Metal -> H.264/AAC MP4 (AVFoundation).
/// Because the picture is derived from the audio samples, never from the wall clock, picture and sound stay in sync
/// no matter how fast or slow the Mac renders.
public enum VideoExporter {
    public static func export(audioURL: URL, preset: Preset, settings: ExportSettings, outputURL: URL,
                              progress: @escaping (Double) -> Bool) throws {
        guard settings.width >= 64, settings.height >= 64, settings.width % 2 == 0, settings.height % 2 == 0,
              (12...120).contains(settings.fps) else { throw ExportError.writer("unsupported size or frame rate") }

        let file: AVAudioFile
        do { file = try AVAudioFile(forReading: audioURL) } catch { throw ExportError.unreadableAudio(error.localizedDescription) }
        let sr = file.processingFormat.sampleRate
        let totalFrames = file.length
        guard totalFrames > 0 else { throw ExportError.unreadableAudio("it contains no audio") }
        let duration = Double(totalFrames) / sr
        let videoFrames = max(1, Int(ceil(duration * Double(settings.fps))))

        // Rough disk check (about 1.5x the target bitrate for the duration plus slack).
        let bitrate = max(2_000_000, Int(Double(settings.width * settings.height) * Double(settings.fps) * 0.10))
        let need = Int64(Double(bitrate) / 8 * duration * 1.5) + 50_000_000
        let dir = outputURL.deletingLastPathComponent()
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        if let free = try? dir.resourceValues(forKeys: [.volumeAvailableCapacityForImportantUsageKey]).volumeAvailableCapacityForImportantUsage, free < need {
            throw ExportError.noSpace("Not enough free disk space for this export (need about \(need / 1_000_000) MB).")
        }

        guard let engine = VisualEngine(preset: preset) else { throw ExportError.writer("unknown visualizer '\(preset.visualizerId)'") }
        let renderer: MetalRenderer
        do { renderer = try MetalRenderer() } catch { throw ExportError.writer(error.localizedDescription) }
        let analyzer = AudioAnalyzer(sampleRate: sr)
        analyzer.settings = preset.audio.analyzerSettings()

        let tmp = outputURL.deletingPathExtension().appendingPathExtension("partial.mp4")
        try? FileManager.default.removeItem(at: tmp)
        try? FileManager.default.removeItem(at: outputURL)
        let writer: AVAssetWriter
        do { writer = try AVAssetWriter(outputURL: tmp, fileType: .mp4) } catch { throw ExportError.writer(error.localizedDescription) }
        func fail(_ e: Error) -> Error { writer.cancelWriting(); try? FileManager.default.removeItem(at: tmp); return e }

        let vSettings: [String: Any] = [
            AVVideoCodecKey: AVVideoCodecType.h264, AVVideoWidthKey: settings.width, AVVideoHeightKey: settings.height,
            AVVideoCompressionPropertiesKey: [AVVideoAverageBitRateKey: bitrate, AVVideoProfileLevelKey: AVVideoProfileLevelH264HighAutoLevel,
                                              AVVideoMaxKeyFrameIntervalKey: settings.fps * 2],
        ]
        let vIn = AVAssetWriterInput(mediaType: .video, outputSettings: vSettings)
        vIn.expectsMediaDataInRealTime = false
        let adaptor = AVAssetWriterInputPixelBufferAdaptor(assetWriterInput: vIn, sourcePixelBufferAttributes: [
            kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
            kCVPixelBufferWidthKey as String: settings.width, kCVPixelBufferHeightKey as String: settings.height,
        ])
        let channels = min(2, Int(file.processingFormat.channelCount))
        let aSettings: [String: Any] = [AVFormatIDKey: kAudioFormatMPEG4AAC, AVSampleRateKey: sr, AVNumberOfChannelsKey: channels, AVEncoderBitRateKey: 192_000]
        let aIn = AVAssetWriterInput(mediaType: .audio, outputSettings: aSettings)
        aIn.expectsMediaDataInRealTime = false
        guard writer.canAdd(vIn), writer.canAdd(aIn) else { throw fail(ExportError.writer("could not configure the encoder")) }
        writer.add(vIn); writer.add(aIn)

        // Audio source: decode the same file with AVAssetReader to linear PCM and hand it to the AAC input.
        let asset = AVURLAsset(url: audioURL)
        guard let track = asset.tracks(withMediaType: .audio).first,
              let reader = try? AVAssetReader(asset: asset) else { throw fail(ExportError.unreadableAudio("no audio track")) }
        let aOut = AVAssetReaderTrackOutput(track: track, outputSettings: [
            AVFormatIDKey: kAudioFormatLinearPCM, AVLinearPCMBitDepthKey: 16, AVLinearPCMIsFloatKey: false,
            AVLinearPCMIsBigEndianKey: false, AVLinearPCMIsNonInterleaved: false, AVSampleRateKey: sr, AVNumberOfChannelsKey: channels,
        ])
        reader.add(aOut)
        guard reader.startReading() else { throw fail(ExportError.unreadableAudio(reader.error?.localizedDescription ?? "cannot decode")) }
        guard writer.startWriting() else { throw fail(ExportError.writer(writer.error?.localizedDescription ?? "cannot start writing")) }
        writer.startSession(atSourceTime: .zero)

        let audioDone = DispatchSemaphore(value: 0)
        let cancelFlag = ManagedFlag()
        aIn.requestMediaDataWhenReady(on: DispatchQueue(label: "ad.export.audio")) {
            while aIn.isReadyForMoreMediaData {
                if cancelFlag.value { aIn.markAsFinished(); audioDone.signal(); return }
                if let buf = aOut.copyNextSampleBuffer() { if !aIn.append(buf) { aIn.markAsFinished(); audioDone.signal(); return } }
                else { aIn.markAsFinished(); audioDone.signal(); return }
            }
        }

        // Video: analyse exactly one frame interval of samples per picture.
        let readBuf = AVAudioPCMBuffer(pcmFormat: file.processingFormat, frameCapacity: AVAudioFrameCount(Double(sr) / Double(settings.fps) + 64))!
        var consumed: AVAudioFramePosition = 0
        let aspect = Float(settings.width) / Float(settings.height)
        let dt = 1.0 / Double(settings.fps)
        var failure: Error?

        for i in 0..<videoFrames {
            if !progress(Double(i) / Double(videoFrames)) { cancelFlag.value = true; failure = ExportError.cancelled; break }
            let endSample = min(totalFrames, AVAudioFramePosition((Double(i + 1) * sr / Double(settings.fps)).rounded()))
            let want = AVAudioFrameCount(max(0, endSample - consumed))
            if want > 0 {
                readBuf.frameLength = 0
                do { try file.read(into: readBuf, frameCount: min(want, readBuf.frameCapacity)) } catch { failure = ExportError.unreadableAudio(error.localizedDescription); break }
                let n = Int(readBuf.frameLength), chans = Int(readBuf.format.channelCount)
                if n > 0, let ch = readBuf.floatChannelData {
                    var mono = [Float](repeating: 0, count: n)
                    let k = 1 / Float(chans)
                    for c in 0..<chans { for j in 0..<n { mono[j] += ch[c][j] * k } }
                    analyzer.push(mono)
                }
                consumed += AVAudioFramePosition(readBuf.frameLength)
            }
            let frame = analyzer.analyze(dt: dt)
            let scene = engine.step(frame: frame, dt: Float(dt), aspect: aspect)
            let bytes = renderer.renderToBytes(canvas: engine.canvas, params: scene, width: settings.width, height: settings.height)

            var waits = 0
            while !vIn.isReadyForMoreMediaData {
                usleep(2000); waits += 1
                if writer.status == .failed { break }
                if waits > 30_000 { failure = ExportError.writer("the encoder stopped responding"); break }
            }
            if failure != nil || writer.status == .failed { break }
            guard let pool = adaptor.pixelBufferPool else { failure = ExportError.writer("no pixel buffer pool"); break }
            var pbOpt: CVPixelBuffer?
            CVPixelBufferPoolCreatePixelBuffer(nil, pool, &pbOpt)
            guard let pb = pbOpt else { failure = ExportError.writer("out of memory"); break }
            CVPixelBufferLockBaseAddress(pb, [])
            if let base = CVPixelBufferGetBaseAddress(pb) {
                let stride = CVPixelBufferGetBytesPerRow(pb), rowBytes = settings.width * 4
                bytes.withUnsafeBytes { src in
                    for y in 0..<settings.height { memcpy(base + y * stride, src.baseAddress! + y * rowBytes, rowBytes) }
                }
            }
            CVPixelBufferUnlockBaseAddress(pb, [])
            if !adaptor.append(pb, withPresentationTime: CMTime(value: CMTimeValue(i), timescale: CMTimeScale(settings.fps))) {
                failure = ExportError.writer(writer.error?.localizedDescription ?? "could not append a frame"); break
            }
        }

        if let f = failure {
            cancelFlag.value = true
            vIn.markAsFinished()
            _ = audioDone.wait(timeout: .now() + 5)
            reader.cancelReading()
            throw fail(f)
        }
        vIn.markAsFinished()
        audioDone.wait()
        let done = DispatchSemaphore(value: 0)
        writer.finishWriting { done.signal() }
        done.wait()
        guard writer.status == .completed else { throw fail(ExportError.writer(writer.error?.localizedDescription ?? "unknown error")) }
        try FileManager.default.moveItem(at: tmp, to: outputURL)
        _ = progress(1)
    }
}

final class ManagedFlag {
    private let lock = NSLock()
    private var v = false
    var value: Bool { get { lock.lock(); defer { lock.unlock() }; return v } set { lock.lock(); v = newValue; lock.unlock() } }
}
