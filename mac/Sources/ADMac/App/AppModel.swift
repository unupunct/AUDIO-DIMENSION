import Foundation
import SwiftUI
import MetalKit
import AVFoundation

enum Page: String, CaseIterable, Identifiable {
    case studio = "Visualizer Studio", presets = "Preset Library", record = "Record & Export"
    var id: String { rawValue }
    var icon: String { self == .studio ? "waveform" : (self == .presets ? "square.grid.2x2" : "record.circle") }
}

enum RecState { case idle, recording, paused }

struct ResolutionOption: Identifiable, Hashable {
    let name: String, width: Int, height: Int
    var id: String { name }
    static let all = [
        ResolutionOption(name: "720p (1280×720)", width: 1280, height: 720),
        ResolutionOption(name: "1080p (1920×1080)", width: 1920, height: 1080),
        ResolutionOption(name: "1440p (2560×1440)", width: 2560, height: 1440),
        ResolutionOption(name: "4K (3840×2160)", width: 3840, height: 2160),
        ResolutionOption(name: "Vertical 1080×1920", width: 1080, height: 1920),
        ResolutionOption(name: "Square 1080×1080", width: 1080, height: 1080),
    ]
}

enum Locations {
    static var resources: URL? {
        if let p = ProcessInfo.processInfo.environment["ADMAC_RESOURCES"] { return URL(fileURLWithPath: p) }
        return Bundle.main.resourceURL
    }
    static var support: URL {
        if let p = ProcessInfo.processInfo.environment["ADMAC_DATA_DIR"] { return URL(fileURLWithPath: p) }
        return FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Audio Dimension")
    }
    static var movies: URL {
        if let p = ProcessInfo.processInfo.environment["ADMAC_DATA_DIR"] { return URL(fileURLWithPath: p).appendingPathComponent("Movies") }
        return FileManager.default.urls(for: .moviesDirectory, in: .userDomainMask)[0].appendingPathComponent("Audio Dimension")
    }
    static var recordings: URL { movies.appendingPathComponent("Recordings") }
    static var exports: URL { movies.appendingPathComponent("Exports") }
}

final class AppModel: ObservableObject {
    let library: PresetLibrary
    let analyzer = AudioAnalyzer()
    let capture: AudioCapture
    let renderer: MetalRenderer?
    let rendererError: String?
    let engine: VisualEngine

    @Published var page: Page = .studio
    @Published var selectedID: String
    @Published var work: Preset {
        didSet { engine.update { $0 = work }; analyzer.settings = work.audio.analyzerSettings() }
    }
    @Published var devices: [AudioInputDevice] = []
    @Published var selectedDeviceID: AudioDeviceID?
    @Published var showInspector = true
    @Published var previewQuality = 0.75
    @Published var fps = 0.0
    @Published var message: String?

    @Published var recState: RecState = .idle
    @Published var elapsed = 0.0
    @Published var recordings: [URL] = []
    @Published var autoRender = true
    @Published var resolution = ResolutionOption.all[1]
    @Published var exportFPS = 30
    @Published var exporting = false
    @Published var exportProgress = 0.0
    @Published var exportStatus = ""
    @Published var lastExport: URL?

    private var startedCaptureForRecording = false
    private var lastTick = CACurrentMediaTime()
    private var fpsAccum = 0.0, fpsCount = 0
    private var cancelExport = ManagedFlag()
    private var timer: Timer?

    init() {
        library = PresetLibrary(resources: Locations.resources, customFolder: Locations.support.appendingPathComponent("Presets"))
        var r: MetalRenderer? = nil, err: String? = nil
        do { r = try MetalRenderer() } catch { err = error.localizedDescription }
        renderer = r; rendererError = err
        let first = library.preset(id: "neon-pulse") ?? library.builtIn.first!
        engine = VisualEngine(preset: first)!
        selectedID = first.id
        work = first
        capture = AudioCapture(analyzer: analyzer)
        analyzer.settings = first.audio.analyzerSettings()
        refreshDevices()
        refreshRecordings()
        timer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in self?.tick() }
    }

    // MARK: presets

    func select(_ id: String) {
        guard let p = library.preset(id: id) else { return }
        selectedID = id
        work = p
        engine.setPreset(p)
        analyzer.settings = p.audio.analyzerSettings()
    }

    func resetLook() { if let p = library.preset(id: selectedID) { work = p } }

    func saveAsCustom(name: String) {
        var p = work
        p.name = name.trimmingCharacters(in: .whitespaces).isEmpty ? "My preset" : name
        p.id = "custom-new"
        do { let saved = try library.saveCustom(p); select(saved.id); message = "Saved preset “\(saved.name)”." }
        catch { message = error.localizedDescription }
    }

    // MARK: audio

    func refreshDevices() {
        devices = AudioDevices.inputDevices()
        if selectedDeviceID == nil || !devices.contains(where: { $0.id == selectedDeviceID }) {
            selectedDeviceID = devices.first(where: { $0.isDefault })?.id ?? devices.first?.id
        }
    }

    var selectedDevice: AudioInputDevice? { devices.first { $0.id == selectedDeviceID } }
    var hasLoopbackDriver: Bool { devices.contains { $0.isLoopback } }
    var liveLabel: String { selectedDevice?.isLoopback == true ? "SYSTEM AUDIO LIVE" : "MICROPHONE LIVE" }

    func toggleCapture() {
        if capture.isRunning { capture.stop(); return }
        capture.start(device: selectedDevice) { [weak self] result in
            if case .failure(let e) = result { self?.message = e.errorDescription }
        }
    }

    func openMicrophonePrivacy() {
        if let u = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone") { NSWorkspace.shared.open(u) }
    }

    // MARK: frame loop (called by the MTKView on the main thread)

    func drawFrame(in view: MTKView) {
        guard let renderer = renderer, let drawable = view.currentDrawable else { return }
        let now = CACurrentMediaTime()
        let dt = now - lastTick
        lastTick = now
        let frame = analyzer.analyze(dt: dt)
        let size = view.drawableSize
        let scene = engine.step(frame: frame, dt: Float(dt), aspect: Float(size.width / max(size.height, 1)))
        renderer.render(canvas: engine.canvas, params: scene, target: drawable.texture, present: drawable)
        fpsAccum += dt; fpsCount += 1
    }

    private func tick() {
        if fpsAccum > 0 { fps = Double(fpsCount) / fpsAccum; fpsAccum = 0; fpsCount = 0 }
        if recState == .recording { elapsed = Double(capture.framesWritten) / max(capture.sampleRate, 1) }
    }

    // MARK: recording

    func refreshRecordings() {
        let files = (try? FileManager.default.contentsOfDirectory(at: Locations.recordings, includingPropertiesForKeys: [.contentModificationDateKey])) ?? []
        recordings = files.filter { ["wav", "m4a", "mp3", "aiff", "caf"].contains($0.pathExtension.lowercased()) }
            .sorted { ((try? $0.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast) >
                      ((try? $1.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast) }
    }

    func startRecording() {
        func begin() {
            do {
                try FileManager.default.createDirectory(at: Locations.recordings, withIntermediateDirectories: true)
                let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd HH-mm-ss"
                try capture.beginRecording(to: Locations.recordings.appendingPathComponent("Recording \(f.string(from: Date())).wav"))
                elapsed = 0; recState = .recording
            } catch {
                message = "Could not start recording: \(error.localizedDescription)"
                if startedCaptureForRecording { capture.stop(); startedCaptureForRecording = false }
            }
        }
        if capture.isRunning { startedCaptureForRecording = false; begin(); return }
        startedCaptureForRecording = true
        capture.start(device: selectedDevice) { [weak self] result in
            switch result {
            case .success: begin()
            case .failure(let e): self?.message = e.errorDescription; self?.startedCaptureForRecording = false
            }
        }
    }

    func pauseRecording() { capture.setRecordingPaused(recState == .recording); recState = recState == .recording ? .paused : .recording }

    func stopRecording() {
        capture.endRecording()
        recState = .idle
        if startedCaptureForRecording { capture.stop(); startedCaptureForRecording = false }
        refreshRecordings()
        if autoRender, let newest = recordings.first { export(audio: newest) }
    }

    // MARK: export

    func export(audio: URL) {
        guard !exporting else { return }
        let preset = work
        let settings = ExportSettings(width: resolution.width, height: resolution.height, fps: exportFPS)
        let name = audio.deletingPathExtension().lastPathComponent
        let out = Locations.exports.appendingPathComponent("\(name) \(settings.height)p\(settings.fps).mp4")
        exporting = true; exportProgress = 0; exportStatus = "Rendering…"; lastExport = nil
        cancelExport = ManagedFlag()
        let flag = cancelExport
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            do {
                try VideoExporter.export(audioURL: audio, preset: preset, settings: settings, outputURL: out) { p in
                    DispatchQueue.main.async { self?.exportProgress = p }
                    return !flag.value
                }
                DispatchQueue.main.async { self?.exporting = false; self?.lastExport = out; self?.exportStatus = "Saved \(out.lastPathComponent)" }
            } catch {
                DispatchQueue.main.async { self?.exporting = false; self?.exportStatus = error.localizedDescription }
            }
        }
    }

    func cancelCurrentExport() { cancelExport.value = true; exportStatus = "Cancelling…" }

    func delete(recording: URL) { try? FileManager.default.removeItem(at: recording); refreshRecordings() }
}
