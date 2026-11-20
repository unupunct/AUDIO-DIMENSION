import Foundation
import AVFoundation
import CoreAudio
import AudioToolbox

public struct AudioInputDevice: Identifiable, Hashable {
    public var id: AudioDeviceID
    public var uid: String
    public var name: String
    public var inputChannels: Int
    public var isDefault: Bool
    /// Virtual loopback drivers (BlackHole, Loopback, Soundflower...) are how macOS exposes "system audio".
    public var isLoopback: Bool { ["blackhole", "loopback", "soundflower", "ishowu", "vb-cable"].contains { name.lowercased().contains($0) } }
}

public enum AudioDevices {
    private static func address(_ sel: AudioObjectPropertySelector, scope: AudioObjectPropertyScope = kAudioObjectPropertyScopeGlobal) -> AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: sel, mScope: scope, mElement: kAudioObjectPropertyElementMain)
    }

    private static func string(_ id: AudioObjectID, _ sel: AudioObjectPropertySelector) -> String? {
        var a = address(sel)
        var ref: Unmanaged<CFString>? = nil
        var size = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        guard AudioObjectGetPropertyData(id, &a, 0, nil, &size, &ref) == noErr, let r = ref else { return nil }
        return r.takeRetainedValue() as String
    }

    private static func inputChannels(_ id: AudioObjectID) -> Int {
        var a = address(kAudioDevicePropertyStreamConfiguration, scope: kAudioObjectPropertyScopeInput)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(id, &a, 0, nil, &size) == noErr, size > 0 else { return 0 }
        let raw = UnsafeMutableRawPointer.allocate(byteCount: Int(size), alignment: MemoryLayout<AudioBufferList>.alignment)
        defer { raw.deallocate() }
        guard AudioObjectGetPropertyData(id, &a, 0, nil, &size, raw) == noErr else { return 0 }
        let list = UnsafeMutableAudioBufferListPointer(raw.assumingMemoryBound(to: AudioBufferList.self))
        return list.reduce(0) { $0 + Int($1.mNumberChannels) }
    }

    public static func defaultInputID() -> AudioDeviceID? {
        var a = address(kAudioHardwarePropertyDefaultInputDevice)
        var id = AudioDeviceID(0)
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &a, 0, nil, &size, &id) == noErr, id != 0 else { return nil }
        return id
    }

    /// Every device that has at least one input channel (microphones, interfaces, loopback drivers).
    public static func inputDevices() -> [AudioInputDevice] {
        var a = address(kAudioHardwarePropertyDevices)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject), &a, 0, nil, &size) == noErr else { return [] }
        var ids = [AudioDeviceID](repeating: 0, count: Int(size) / MemoryLayout<AudioDeviceID>.size)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &a, 0, nil, &size, &ids) == noErr else { return [] }
        let def = defaultInputID()
        return ids.compactMap { id in
            let ch = inputChannels(id)
            guard ch > 0 else { return nil }
            return AudioInputDevice(id: id, uid: string(id, kAudioDevicePropertyDeviceUID) ?? "\(id)",
                                    name: string(id, kAudioObjectPropertyName) ?? "Device \(id)", inputChannels: ch, isDefault: id == def)
        }
    }
}

public enum CaptureError: Error, LocalizedError {
    case permissionDenied, noInput, engine(String)
    public var errorDescription: String? {
        switch self {
        case .permissionDenied:
            return "Microphone access is turned off for Audio Dimension. Open System Preferences > Security & Privacy > Privacy > Microphone and switch it on."
        case .noInput: return "No audio input device is available."
        case .engine(let m): return "Could not start audio capture: \(m)"
        }
    }
}

/// Live capture from a microphone or a loopback driver through AVAudioEngine. The audio is analysed for the visuals and,
/// when a recording is armed, written to a WAV file. Nothing is captured unless `start` was called.
public final class AudioCapture: ObservableObject {
    @Published public private(set) var isRunning = false
    @Published public private(set) var deviceName = ""
    @Published public private(set) var lastError: String?
    @Published public private(set) var needsPermission = false

    public let analyzer: AudioAnalyzer
    private var engine: AVAudioEngine?
    private let writeQueue = DispatchQueue(label: "ad.recorder.write")
    private let writerLock = NSLock()
    private var writer: AVAudioFile?
    private var paused = false
    public private(set) var framesWritten: AVAudioFramePosition = 0
    public private(set) var sampleRate: Double = 48000

    public init(analyzer: AudioAnalyzer) { self.analyzer = analyzer }

    public static var authorizationStatus: AVAuthorizationStatus { AVCaptureDevice.authorizationStatus(for: .audio) }

    public func requestPermission(_ done: @escaping (Bool) -> Void) {
        switch AudioCapture.authorizationStatus {
        case .authorized: done(true)
        case .notDetermined: AVCaptureDevice.requestAccess(for: .audio) { ok in DispatchQueue.main.async { done(ok) } }
        default: done(false)
        }
    }

    public func start(device: AudioInputDevice?, completion: @escaping (Result<Void, CaptureError>) -> Void) {
        stop()
        requestPermission { [weak self] ok in
            guard let self = self else { return }
            guard ok else {
                self.needsPermission = true; self.lastError = CaptureError.permissionDenied.errorDescription
                completion(.failure(.permissionDenied)); return
            }
            self.needsPermission = false
            do {
                try self.startEngine(device: device)
                self.lastError = nil
                completion(.success(()))
            } catch let e as CaptureError {
                self.lastError = e.errorDescription; completion(.failure(e))
            } catch {
                let e = CaptureError.engine(error.localizedDescription)
                self.lastError = e.errorDescription; completion(.failure(e))
            }
        }
    }

    private func startEngine(device: AudioInputDevice?) throws {
        let engine = AVAudioEngine()
        let input = engine.inputNode
        if let device = device, let unit = input.audioUnit {
            var id = device.id
            let st = AudioUnitSetProperty(unit, kAudioOutputUnitProperty_CurrentDevice, kAudioUnitScope_Global, 0, &id, UInt32(MemoryLayout<AudioDeviceID>.size))
            if st != noErr { throw CaptureError.engine("could not select \(device.name) (error \(st))") }
        }
        let format = input.outputFormat(forBus: 0)
        guard format.sampleRate > 0, format.channelCount > 0 else { throw CaptureError.noInput }
        sampleRate = format.sampleRate
        analyzer.setSampleRate(format.sampleRate)
        analyzer.reset()

        input.installTap(onBus: 0, bufferSize: 1024, format: format) { [weak self] buffer, _ in
            self?.handle(buffer)
        }
        do { try engine.start() } catch { input.removeTap(onBus: 0); throw CaptureError.engine(error.localizedDescription) }
        self.engine = engine
        deviceName = device?.name ?? AudioDevices.inputDevices().first(where: { $0.isDefault })?.name ?? "Default input"
        isRunning = true
    }

    public func stop() {
        guard let e = engine else { isRunning = false; return }
        e.inputNode.removeTap(onBus: 0)
        e.stop()
        engine = nil
        isRunning = false
    }

    private func handle(_ buffer: AVAudioPCMBuffer) {
        guard let ch = buffer.floatChannelData else { return }
        let n = Int(buffer.frameLength), chans = Int(buffer.format.channelCount)
        var mono = [Float](repeating: 0, count: n)
        if chans == 1 { for i in 0..<n { mono[i] = ch[0][i] } }
        else {
            let k = 1 / Float(chans)
            for c in 0..<chans { for i in 0..<n { mono[i] += ch[c][i] * k } }
        }
        analyzer.push(mono)

        writerLock.lock()
        let w = writer, isPaused = paused
        writerLock.unlock()
        if let w = w, !isPaused, let copy = AVAudioPCMBuffer(pcmFormat: buffer.format, frameCapacity: buffer.frameLength) {
            copy.frameLength = buffer.frameLength
            for c in 0..<chans { memcpy(copy.floatChannelData![c], ch[c], n * MemoryLayout<Float>.size) }
            writeQueue.async { [weak self] in
                try? w.write(from: copy)
                self?.framesWritten += AVAudioFramePosition(n)
            }
        }
    }

    // MARK: recording

    public func beginRecording(to url: URL) throws {
        guard let e = engine else { throw CaptureError.noInput }
        let format = e.inputNode.outputFormat(forBus: 0)
        let file = try AVAudioFile(forWriting: url, settings: format.settings, commonFormat: .pcmFormatFloat32, interleaved: false)
        writerLock.lock(); writer = file; paused = false; framesWritten = 0; writerLock.unlock()
    }

    public func setRecordingPaused(_ p: Bool) { writerLock.lock(); paused = p; writerLock.unlock() }

    /// Closes the file after every queued buffer has been written.
    public func endRecording() {
        writerLock.lock(); writer = nil; paused = false; writerLock.unlock()
        writeQueue.sync {}
    }
}
