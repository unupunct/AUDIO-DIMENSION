import SwiftUI
import MetalKit
import AppKit

// MARK: - Metal preview

struct MetalPreview: NSViewRepresentable {
    @ObservedObject var model: AppModel

    func makeNSView(context: Context) -> MTKView {
        let v = MTKView()
        v.device = model.renderer?.device
        v.colorPixelFormat = MetalRenderer.pixelFormat
        v.framebufferOnly = true
        v.preferredFramesPerSecond = 60
        v.autoResizeDrawable = false
        v.delegate = context.coordinator
        v.layer?.isOpaque = true
        return v
    }

    func updateNSView(_ v: MTKView, context: Context) {
        let scale = v.window?.backingScaleFactor ?? 2
        let q = CGFloat(model.previewQuality)
        let w = max(64, v.bounds.width * scale * q), h = max(64, v.bounds.height * scale * q)
        if abs(v.drawableSize.width - w) > 2 || abs(v.drawableSize.height - h) > 2 { v.drawableSize = CGSize(width: w, height: h) }
    }

    func makeCoordinator() -> Coordinator { Coordinator(model) }

    final class Coordinator: NSObject, MTKViewDelegate {
        let model: AppModel
        init(_ m: AppModel) { model = m }
        func mtkView(_ view: MTKView, drawableSizeWillChange size: CGSize) {}
        func draw(in view: MTKView) { model.drawFrame(in: view) }
    }
}

// MARK: - Root

struct RootView: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        if let err = model.rendererError {
            VStack(spacing: 12) {
                Image(systemName: "exclamationmark.triangle").font(.system(size: 40))
                Text("Audio Dimension needs Metal graphics").font(.title2)
                Text(err).foregroundColor(.secondary)
            }.frame(maxWidth: .infinity, maxHeight: .infinity)
        } else {
            HStack(spacing: 0) {
                Sidebar()
                Divider()
                ZStack {
                    switch model.page {
                    case .studio: StudioView()
                    case .presets: PresetsView()
                    case .record: RecordView()
                    }
                }
            }
            .alert(isPresented: Binding(get: { model.message != nil }, set: { if !$0 { model.message = nil } })) {
                Alert(title: Text("Audio Dimension"), message: Text(model.message ?? ""), dismissButton: .default(Text("OK")))
            }
        }
    }
}

struct Sidebar: View {
    @EnvironmentObject var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("AUDIO DIMENSION").font(.system(size: 12, weight: .bold)).foregroundColor(.secondary).padding(.bottom, 10)
            ForEach(Page.allCases) { p in
                Button(action: { model.page = p }) {
                    HStack { Image(systemName: p.icon).frame(width: 20); Text(p.rawValue); Spacer() }
                        .padding(.vertical, 7).padding(.horizontal, 10)
                        .background(model.page == p ? Color.accentColor.opacity(0.25) : Color.clear)
                        .cornerRadius(7)
                }.buttonStyle(.plain)
            }
            Spacer()
            if model.capture.isRunning { LivePill() }
            Text("Preview 1.0.0").font(.caption2).foregroundColor(.secondary)
        }
        .padding(14).frame(width: 200)
    }
}

struct LivePill: View {
    @EnvironmentObject var model: AppModel
    var body: some View {
        HStack(spacing: 6) {
            Circle().fill(Color.white).frame(width: 7, height: 7)
            Text(model.liveLabel).font(.system(size: 10, weight: .bold)).foregroundColor(.white)
        }
        .padding(.horizontal, 10).padding(.vertical, 5).background(Color.red).cornerRadius(10)
    }
}

// MARK: - Studio

struct StudioView: View {
    @EnvironmentObject var model: AppModel
    @State private var showSave = false
    @State private var newName = ""

    var body: some View {
        HStack(spacing: 0) {
            VStack(spacing: 0) {
                ZStack(alignment: .topLeading) {
                    MetalPreview(model: model)
                    HStack {
                        if model.capture.isRunning { LivePill() }
                        Text(String(format: "%.0f FPS", model.fps)).font(.system(size: 10, design: .monospaced))
                            .padding(5).background(Color.black.opacity(0.5)).cornerRadius(5)
                    }.padding(10)
                    if !model.capture.isRunning {
                        VStack(spacing: 6) {
                            Text("Choose a source and press Start").font(.headline)
                            Text("The visuals follow real audio. Nothing is captured until you start.").font(.caption).foregroundColor(.secondary)
                        }
                        .padding(14).background(Color.black.opacity(0.55)).cornerRadius(10)
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                    }
                }
                ControlBar()
            }
            if model.showInspector { Divider(); Inspector(showSave: $showSave) }
        }
        .sheet(isPresented: $showSave) {
            VStack(alignment: .leading, spacing: 12) {
                Text("Save as preset").font(.headline)
                TextField("Preset name", text: $newName).frame(width: 280)
                HStack {
                    Spacer()
                    Button("Cancel") { showSave = false }
                    Button("Save") { model.saveAsCustom(name: newName); showSave = false }.keyboardShortcut(.defaultAction)
                }
            }.padding(20)
        }
    }
}

struct ControlBar: View {
    @EnvironmentObject var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 12) {
                Picker("Source", selection: $model.selectedDeviceID) {
                    ForEach(model.devices) { d in
                        Text(d.name + (d.isLoopback ? "  (system audio)" : "") + (d.isDefault ? "  (default)" : "")).tag(Optional(d.id))
                    }
                }.frame(width: 360).disabled(model.capture.isRunning)
                Button(action: { model.refreshDevices() }) { Image(systemName: "arrow.clockwise") }.help("Rescan audio devices")
                Button(model.capture.isRunning ? "Stop" : "Start") { model.toggleCapture() }
                    .keyboardShortcut(.space, modifiers: [])
                LevelMeter(analyzer: model.analyzer, running: model.capture.isRunning)
                Spacer()
                Text(model.work.name).font(.headline)
                Picker("", selection: $model.previewQuality) {
                    Text("Preview 50%").tag(0.5); Text("Preview 75%").tag(0.75); Text("Preview 100%").tag(1.0)
                }.frame(width: 130).help("Preview resolution; exports always use the size you choose")
                Button(action: { withAnimation { model.showInspector.toggle() } }) { Image(systemName: "sidebar.right") }
            }
            if model.capture.needsPermission {
                HStack {
                    Text(model.capture.lastError ?? "").font(.caption).foregroundColor(.orange)
                    Button("Open Privacy Settings") { model.openMicrophonePrivacy() }
                }
            } else if !model.hasLoopbackDriver {
                Text("To visualize system audio (what your Mac plays), install a virtual audio driver such as BlackHole and route your output to it; it will then appear in this list.")
                    .font(.caption).foregroundColor(.secondary)
            }
        }
        .padding(12)
    }
}

struct LevelMeter: View {
    let analyzer: AudioAnalyzer
    let running: Bool
    @State private var level: Float = 0
    @State private var peak: Float = 0
    let timer = Timer.publish(every: 1.0 / 20, on: .main, in: .common).autoconnect()
    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                RoundedRectangle(cornerRadius: 3).fill(Color.gray.opacity(0.25))
                RoundedRectangle(cornerRadius: 3).fill(LinearGradient(colors: [.green, .yellow, .red], startPoint: .leading, endPoint: .trailing))
                    .frame(width: g.size.width * CGFloat(level))
                Rectangle().fill(Color.white).frame(width: 2).offset(x: g.size.width * CGFloat(peak))
            }
        }
        .frame(width: 160, height: 10)
        .onReceive(timer) { _ in
            // Reads the analyser's last state through a tiny dedicated probe so meters never disturb the visual frame loop.
            guard running else { level = 0; peak = 0; return }
            let f = analyzer.meter()
            level = f.0; peak = f.1
        }
    }
}

struct Inspector: View {
    @EnvironmentObject var model: AppModel
    @Binding var showSave: Bool

    func slider(_ title: String, _ value: Binding<Double>, _ range: ClosedRange<Double>) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack { Text(title).font(.caption); Spacer(); Text(String(format: "%.2f", value.wrappedValue)).font(.caption).foregroundColor(.secondary) }
            Slider(value: value, in: range)
        }
    }

    func hexBinding(_ i: Int) -> Binding<Color> {
        Binding(
            get: { Color(nsColor: NSColor(calibratedRed: CGFloat(parseHexColor(model.work.visual.palette.colors[i]).x),
                                          green: CGFloat(parseHexColor(model.work.visual.palette.colors[i]).y),
                                          blue: CGFloat(parseHexColor(model.work.visual.palette.colors[i]).z), alpha: 1)) },
            set: { c in
                let n = NSColor(c).usingColorSpace(.sRGB) ?? .white
                model.work.visual.palette.colors[i] = String(format: "#%02X%02X%02X", Int(n.redComponent * 255 + 0.5), Int(n.greenComponent * 255 + 0.5), Int(n.blueComponent * 255 + 0.5))
            })
    }

    var lookSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Look").font(.headline)
            slider("Speed", $model.work.visual.params.speed, 0...4)
            slider("Reactivity", $model.work.visual.params.reactivity, 0...3)
            slider("Scale", $model.work.visual.params.scale, 0.2...3)
            slider("Line width", $model.work.visual.params.lineWidth, 0.5...16)
            slider("Glow", $model.work.visual.params.glow, 0...4)
            slider("Particle density", $model.work.visual.params.particleDensity, 0.05...2)
            Picker("Variant", selection: $model.work.visual.params.variant) { ForEach(0..<4) { Text("Style \($0 + 1)").tag($0) } }
            Toggle("Mirror", isOn: $model.work.visual.params.mirror)
        }
    }

    var colourSection: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Colours").font(.caption)
            HStack { ForEach(0..<model.work.visual.palette.colors.count, id: \.self) { ColorPicker("", selection: hexBinding($0)).labelsHidden() } }
        }
    }

    var effectsSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Effects").font(.headline)
            Toggle("Bloom", isOn: $model.work.visual.effects.bloom)
            slider("Bloom amount", $model.work.visual.effects.bloomAmount, 0...4)
            Toggle("Vignette", isOn: $model.work.visual.effects.vignette)
            Toggle("Chromatic aberration", isOn: $model.work.visual.effects.chromaticAberration)
            Toggle("Film grain", isOn: $model.work.visual.effects.filmGrain)
            Toggle("Scanlines", isOn: $model.work.visual.effects.scanlines)
        }
    }

    var audioSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Audio response").font(.headline)
            slider("Sensitivity", $model.work.audio.sensitivity, 0.05...8)
            slider("Smoothing", $model.work.audio.smoothing, 0...0.99)
            slider("Bass", $model.work.audio.bassSensitivity, 0...4)
            slider("Mid", $model.work.audio.midSensitivity, 0...4)
            slider("Treble", $model.work.audio.trebleSensitivity, 0...4)
            Toggle("Automatic gain", isOn: $model.work.audio.autoGain)
            Toggle("Beat detection", isOn: $model.work.audio.peakDetection)
        }
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 10) {
                lookSection
                colourSection
                Divider()
                effectsSection
                Divider()
                audioSection
                Divider()
                HStack {
                    Button("Reset look") { model.resetLook() }
                    Button("Save as preset…") { showSave = true }
                }
            }.padding(14)
        }
        .frame(width: 270)
    }
}

// MARK: - Presets

struct PresetsView: View {
    @EnvironmentObject var model: AppModel
    @State private var search = ""
    @State private var category = "All"
    @State private var favoritesOnly = false

    var filtered: [Preset] {
        model.library.all.filter { p in
            (category == "All" || p.category == category)
                && (!favoritesOnly || model.library.favorites.contains(p.id))
                && (search.isEmpty || p.name.localizedCaseInsensitiveContains(search) || p.tags.contains { $0.localizedCaseInsensitiveContains(search) })
        }
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                TextField("Search presets", text: $search).textFieldStyle(.roundedBorder).frame(width: 220)
                Picker("", selection: $category) {
                    Text("All categories").tag("All")
                    ForEach(model.library.categories, id: \.self) { Text($0).tag($0) }
                }.frame(width: 200)
                Toggle("Favourites", isOn: $favoritesOnly)
                Spacer()
                Button("Import…") { importPresets() }
                Button("Export current…") { exportCurrent() }
            }.padding(12)
            if model.library.unavailableCount > 0 {
                Text("\(model.library.unavailableCount) of the 60 built-in presets need visualizers that are not on the Mac yet; the \(model.library.builtIn.count) shown here are ready.")
                    .font(.caption).foregroundColor(.secondary).padding(.horizontal, 12).padding(.bottom, 6)
            }
            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 210), spacing: 14)], spacing: 14) {
                    ForEach(filtered) { p in PresetCard(preset: p) }
                }.padding(12)
            }
        }
    }

    func importPresets() {
        let panel = NSOpenPanel()
        panel.allowedFileTypes = ["adpreset", "json"]
        panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do { let n = try model.library.importFile(url); model.message = "Imported \(n) preset\(n == 1 ? "" : "s")." }
        catch { model.message = error.localizedDescription }
    }

    func exportCurrent() {
        let panel = NSSavePanel()
        panel.allowedFileTypes = ["adpreset"]
        panel.nameFieldStringValue = "\(model.work.name).adpreset"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            var p = model.work; p.id = model.selectedID
            try PresetValidator.serialize([p]).write(to: url, options: .atomic)
            model.message = "Exported to \(url.lastPathComponent)."
        } catch { model.message = error.localizedDescription }
    }
}

struct PresetCard: View {
    @EnvironmentObject var model: AppModel
    let preset: Preset

    var thumbnail: NSImage? {
        Locations.resources.flatMap { NSImage(contentsOf: $0.appendingPathComponent("Thumbnails/\(preset.id).png")) }
    }

    var body: some View {
        let selected = model.selectedID == preset.id
        VStack(alignment: .leading, spacing: 6) {
            ZStack(alignment: .topTrailing) {
                Group {
                    if let t = thumbnail { Image(nsImage: t).resizable().aspectRatio(16.0 / 9.0, contentMode: .fill) }
                    else { Rectangle().fill(Color.gray.opacity(0.2)).aspectRatio(16.0 / 9.0, contentMode: .fit).overlay(Text(preset.visualizerId).font(.caption2)) }
                }.frame(height: 118).clipped().cornerRadius(8)
                Button(action: { model.library.toggleFavorite(preset.id) }) {
                    Image(systemName: model.library.favorites.contains(preset.id) ? "star.fill" : "star").foregroundColor(.yellow).padding(6)
                }.buttonStyle(.plain)
            }
            Text(preset.name).font(.system(size: 13, weight: .semibold))
            Text(preset.category).font(.caption).foregroundColor(.secondary)
            Text(preset.description).font(.caption2).foregroundColor(.secondary).lineLimit(2)
            HStack {
                Button(selected ? "In use" : "Use this preset") { model.select(preset.id); model.page = .studio }.disabled(selected)
                if preset.id.hasPrefix("custom-") {
                    Button("Delete") { model.library.deleteCustom(preset.id) }
                }
            }
        }
        .padding(8)
        .background(RoundedRectangle(cornerRadius: 10).fill(Color.gray.opacity(selected ? 0.25 : 0.12)))
        .overlay(RoundedRectangle(cornerRadius: 10).stroke(selected ? Color.accentColor : Color.clear, lineWidth: 2))
    }
}

// MARK: - Record & export

struct RecordView: View {
    @EnvironmentObject var model: AppModel

    func time(_ s: Double) -> String { String(format: "%02d:%02d", Int(s) / 60, Int(s) % 60) }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("Record & Export").font(.title2)
                GroupBox(label: Text("Record")) {
                    VStack(alignment: .leading, spacing: 10) {
                        HStack(spacing: 12) {
                            Picker("Source", selection: $model.selectedDeviceID) {
                                ForEach(model.devices) { Text($0.name).tag(Optional($0.id)) }
                            }.frame(width: 360).disabled(model.recState != .idle || model.capture.isRunning)
                            if model.recState == .idle {
                                Button("● Record") { model.startRecording() }.disabled(model.exporting)
                            } else {
                                Button(model.recState == .paused ? "Resume" : "Pause") { model.pauseRecording() }
                                Button("■ Stop") { model.stopRecording() }
                                Text((model.recState == .paused ? "PAUSED  " : "REC  ") + time(model.elapsed))
                                    .font(.system(.body, design: .monospaced)).foregroundColor(model.recState == .paused ? .orange : .red)
                            }
                        }
                        Toggle("Render the video automatically when the recording stops", isOn: $model.autoRender)
                        Text("Audio is recorded first, then the picture is rendered frame by frame from it, so picture and sound always stay in sync.")
                            .font(.caption).foregroundColor(.secondary)
                    }.padding(6)
                }
                GroupBox(label: Text("Video settings")) {
                    HStack(spacing: 16) {
                        Picker("Size", selection: $model.resolution) { ForEach(ResolutionOption.all) { Text($0.name).tag($0) } }.frame(width: 300)
                        Picker("Frame rate", selection: $model.exportFPS) { ForEach([24, 30, 60], id: \.self) { Text("\($0) fps").tag($0) } }.frame(width: 190)
                        Text("MP4 · H.264 + AAC").font(.caption).foregroundColor(.secondary)
                    }.padding(6)
                }
                if model.exporting || !model.exportStatus.isEmpty {
                    GroupBox(label: Text("Export")) {
                        VStack(alignment: .leading, spacing: 8) {
                            if model.exporting {
                                ProgressView(value: model.exportProgress)
                                HStack { Text("\(Int(model.exportProgress * 100))%"); Spacer(); Button("Cancel") { model.cancelCurrentExport() } }
                            }
                            Text(model.exportStatus).font(.caption)
                            if let u = model.lastExport {
                                Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([u]) }
                            }
                        }.padding(6)
                    }
                }
                GroupBox(label: Text("Recordings (\(model.recordings.count))")) {
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            Button("Render video from an audio file…") { pickAudio() }.disabled(model.exporting)
                            Button("Open recordings folder") { NSWorkspace.shared.open(Locations.recordings) }
                        }
                        ForEach(model.recordings, id: \.self) { u in
                            HStack {
                                Text(u.lastPathComponent).font(.system(size: 12))
                                Spacer()
                                Button("Render video") { model.export(audio: u) }.disabled(model.exporting)
                                Button("Show") { NSWorkspace.shared.activateFileViewerSelecting([u]) }
                                Button("Delete") { model.delete(recording: u) }
                            }
                        }
                        if model.recordings.isEmpty { Text("No recordings yet.").font(.caption).foregroundColor(.secondary) }
                    }.padding(6)
                }
            }.padding(20)
        }
        .onAppear { model.refreshDevices(); model.refreshRecordings() }
    }

    func pickAudio() {
        let panel = NSOpenPanel()
        panel.allowedFileTypes = ["wav", "mp3", "m4a", "aiff", "aif", "caf", "flac"]
        panel.allowsMultipleSelection = false
        if panel.runModal() == .OK, let url = panel.url { model.export(audio: url) }
    }
}
