import XCTest

func loudFrames(_ seconds: Double = 3) -> [AudioFrame] {
    // Kicks plus broadband noise bursts so every band, the waveform and the beat detector all see activity.
    var sig = Signal.kicks(seconds: seconds, period: 0.5)
    let n = Signal.noise(seconds: seconds, amp: 0.25)
    for i in 0..<sig.count { sig[i] += n[i] * (Float((i / 12000) % 2)) }
    return analyze(sig)
}

func silentFrames(_ seconds: Double = 3) -> [AudioFrame] { analyze(Signal.silence(seconds: seconds)) }

func run(_ preset: Preset, _ frames: [AudioFrame], aspect: Float = 16.0 / 9.0) -> VisualEngine {
    let e = VisualEngine(preset: preset)!
    for f in frames { e.step(frame: f, dt: 1.0 / 60, aspect: aspect) }
    return e
}

func signature(_ c: Canvas) -> Double {
    var s = 0.0
    // Absolute values and squares so symmetric shapes (waveforms, spirals) cannot cancel out to the same total.
    for v in c.solid + c.glowV {
        let x = Double(v.x), y = Double(v.y)
        s += abs(x) * 1.3 + abs(y) * 1.7 + x * x * 0.7 + y * y * 0.9 + Double(v.a) * 0.9 + Double(v.r) * 0.3
    }
    return s + Double(c.vertexCount)
}

final class VisualizerTests: XCTestCase {
    var presets: [Preset] { PresetLibrary(resources: TestPaths.resources, customFolder: TestPaths.temp(), defaults: TestPaths.defaults()).builtIn }

    func testRegistryMatchesPresets() {
        XCTAssertEqual(VisualizerRegistry.ids.count, 14)
        let used = Set(presets.map { $0.visualizerId })
        XCTAssertEqual(used, Set(VisualizerRegistry.ids), "every implemented visualizer has at least one built-in preset")
        XCTAssertNil(VisualizerRegistry.make("does-not-exist"))
    }

    func testEveryPresetDrawsFiniteGeometry() {
        let frames = Array(loudFrames().prefix(120))
        for p in presets {
            let e = run(p, frames)
            XCTAssertTrue(e.canvas.vertexCount > 0, "\(p.name) drew nothing")
            XCTAssertTrue(e.canvas.vertexCount < Canvas.maxVertices, "\(p.name) hit the vertex cap")
            var bad = 0
            for v in e.canvas.solid + e.canvas.glowV where !(v.x.isFinite && v.y.isFinite && v.a.isFinite && abs(v.x) < 50 && abs(v.y) < 50) { bad += 1 }
            XCTAssertEqual(bad, 0, "\(p.name) produced \(bad) invalid vertices")
        }
    }

    func testEveryVisualizerSurvivesAllOptions() {
        let frames = Array(loudFrames().prefix(90))
        var seen = Set<String>()
        for base in presets where !seen.contains(base.visualizerId) {
            seen.insert(base.visualizerId)
            for variant in 0..<4 {
                for mirror in [false, true] {
                    for density in [0.05, 2.0] {
                        var p = base
                        p.visual.params.variant = variant; p.visual.params.mirror = mirror; p.visual.params.particleDensity = density
                        let e = run(p, frames)
                        var bad = 0
                        for v in e.canvas.solid + e.canvas.glowV where !(v.x.isFinite && v.y.isFinite) { bad += 1 }
                        XCTAssertEqual(bad, 0, "\(p.visualizerId) v\(variant) mirror=\(mirror) density=\(density)")
                        XCTAssertTrue(e.canvas.vertexCount > 0, "\(p.visualizerId) v\(variant) empty")
                    }
                }
            }
        }
    }

    func testPicturesFollowTheAudio() {
        let loud = loudFrames(), quiet = silentFrames()
        for p in presets {
            let a = signature(run(p, quiet).canvas), b = signature(run(p, loud).canvas)
            XCTAssertTrue(abs(a - b) > 0.5, "\(p.name) looks the same with and without audio (\(a) vs \(b))")
        }
    }

    func testDeterministic() {
        let frames = Array(loudFrames().prefix(100))
        for p in presets {
            let a = run(p, frames).canvas, b = run(p, frames).canvas
            XCTAssertEqual(a.solid.count, b.solid.count, p.name)
            XCTAssertEqual(signature(a), signature(b), p.name)
        }
    }

    func testBarsGrowWithLoudness() {
        let p = presets.first { $0.visualizerId == "classic-spectrum" }!
        func top(_ e: VisualEngine) -> Float { e.canvas.solid.map { $0.y }.max() ?? -9 }
        XCTAssertGreaterThan(top(run(p, loudFrames())), top(run(p, silentFrames())) + 0.2)
    }

    func testSettingsChangeTheLook() {
        let frames = Array(loudFrames().prefix(60))
        let base = presets.first { $0.visualizerId == "circular-spectrum" }!
        var wide = base; wide.visual.params.lineWidth = 12
        var noGlow = base; noGlow.visual.params.glow = 0
        XCTAssertNotEqual(signature(run(base, frames).canvas), signature(run(wide, frames).canvas))
        XCTAssertTrue(run(noGlow, frames).canvas.glowV.count < run(base, frames).canvas.glowV.count, "glow 0 removes halos")
        var scaled = base; scaled.visual.params.scale = 2
        XCTAssertNotEqual(signature(run(base, frames).canvas), signature(run(scaled, frames).canvas))
    }

    func testPaletteColoursAreUsed() {
        var p = presets.first { $0.visualizerId == "classic-spectrum" }!
        p.visual.palette.colors = ["#FF0000", "#FF0000"]
        let e = run(p, Array(loudFrames().prefix(60)))
        let reds = e.canvas.solid.filter { $0.r > 0.95 && $0.g < 0.05 && $0.b < 0.05 && $0.a > 0.8 }.count
        XCTAssertTrue(reds > 50, "pure red palette gave \(reds) red vertices")
    }

    // MARK: Canvas

    func testCanvasPrimitives() {
        let c = Canvas(); c.glow = 0
        c.line(P2(0, 0), P2(1, 0), width: 0.1, RGBA(1, 1, 1, 1))
        XCTAssertEqual(c.solid.count, 6)
        XCTAssertEqual(c.glowV.count, 0)
        c.glow = 1
        c.line(P2(0, 0), P2(1, 0), width: 0.1, RGBA(1, 1, 1, 1))
        XCTAssertEqual(c.glowV.count, 6)
        c.clear(); c.line(P2(0.5, 0.5), P2(0.5, 0.5), width: 0.1, RGBA(1, 1, 1, 1))
        XCTAssertEqual(c.vertexCount, 0, "zero-length line draws nothing")
    }

    func testCanvasHonoursVertexCap() {
        let c = Canvas()
        for _ in 0..<400_000 { c.rect(x: 0, y: 0, w: 0.1, h: 0.1, RGBA(1, 1, 1, 1)) }
        XCTAssertTrue(c.vertexCount <= Canvas.maxVertices)
    }
}
