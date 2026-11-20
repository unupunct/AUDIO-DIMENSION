import XCTest
import Metal

func renderFrame(_ renderer: MetalRenderer, _ preset: Preset, frames: [AudioFrame], w: Int = 480, h: Int = 270) -> [UInt8] {
    let e = VisualEngine(preset: preset)!
    var scene = SceneParams()
    for f in frames { scene = e.step(frame: f, dt: 1.0 / 60, aspect: Float(w) / Float(h)) }
    return renderer.renderToBytes(canvas: e.canvas, params: scene, width: w, height: h)
}

func brightFraction(_ px: [UInt8], threshold: Int = 80) -> Double {
    var n = 0
    var i = 0
    while i < px.count { if max(Int(px[i]), Int(px[i + 1]), Int(px[i + 2])) > threshold { n += 1 }; i += 4 }
    return Double(n) / Double(px.count / 4)
}

func totalSum(_ px: [UInt8]) -> Int { var s = 0; var i = 0; while i < px.count { s += Int(px[i]) + Int(px[i + 1]) + Int(px[i + 2]); i += 4 }; return s }

final class RendererTests: XCTestCase {
    static var renderer: MetalRenderer? = { try? MetalRenderer() }()
    var r: MetalRenderer { RendererTests.renderer! }
    var lib: PresetLibrary { PresetLibrary(resources: TestPaths.resources, customFolder: TestPaths.temp(), defaults: TestPaths.defaults()) }

    func testMetalDeviceAndShadersCompile() {
        XCTAssertNotNil(RendererTests.renderer, "Metal device and shader compilation")
    }

    func testEveryPresetRendersVisibleOutput() {
        guard RendererTests.renderer != nil else { return XCTFail("no renderer") }
        let frames = Array(loudFrames().prefix(120))
        for p in lib.builtIn {
            let px = renderFrame(r, p, frames: frames)
            XCTAssertTrue(brightFraction(px) > 0.001, "\(p.name) rendered almost nothing (\(brightFraction(px)))")
        }
    }

    func testBackgroundGradientRenders() {
        var p = lib.builtIn[0]
        p.visual.background.mode = "Gradient"; p.visual.background.color1 = "#FF0000"; p.visual.background.color2 = "#0000FF"
        p.visual.effects.vignette = false; p.visual.effects.bloom = false; p.visual.effects.chromaticAberration = false
        let px = renderFrame(r, p, frames: [silentFrames(1).last!], w: 64, h: 64)
        func pixel(_ x: Int, _ y: Int) -> (Int, Int, Int) { let i = (y * 64 + x) * 4; return (Int(px[i + 2]), Int(px[i + 1]), Int(px[i])) } // r,g,b from BGRA
        let top = pixel(32, 1), bottom = pixel(32, 62)
        XCTAssertTrue(top.0 > 200 && top.2 < 60, "top should be red: \(top)")
        XCTAssertTrue(bottom.2 > 200 && bottom.0 < 60, "bottom should be blue: \(bottom)")
    }

    func testAudioChangesThePicture() {
        let p = lib.builtIn.first { $0.visualizerId == "circular-spectrum" }!
        let a = renderFrame(r, p, frames: Array(silentFrames().prefix(60))), b = renderFrame(r, p, frames: Array(loudFrames().prefix(60)))
        var diff = 0; var i = 0
        while i < a.count { if abs(Int(a[i]) - Int(b[i])) > 25 { diff += 1 }; i += 4 }
        XCTAssertTrue(Double(diff) / Double(a.count / 4) > 0.01, "less than 1% of pixels changed")
    }

    func testRenderingIsDeterministic() {
        guard let r2 = try? MetalRenderer() else { return XCTFail("no second renderer") }
        let p = lib.builtIn.first { $0.visualizerId == "fireflies" }!
        let frames = Array(loudFrames().prefix(90))
        XCTAssertEqual(renderFrame(r, p, frames: frames), renderFrame(r2, p, frames: frames), "same input must give byte-identical frames")
    }

    func testBloomAddsLight() {
        var p = lib.builtIn.first { $0.visualizerId == "circular-spectrum" }!
        let frames = Array(loudFrames().prefix(60))
        p.visual.effects.bloom = false
        let off = totalSum(renderFrame(r, p, frames: frames))
        p.visual.effects.bloom = true; p.visual.effects.bloomAmount = 3; p.visual.effects.bloomThreshold = 0.2
        let on = totalSum(renderFrame(r, p, frames: frames))
        XCTAssertTrue(Double(on) > Double(off) * 1.03, "bloom should add light: \(off) -> \(on)")
    }

    func testVignetteDarkensCorners() {
        var p = lib.builtIn[0]
        p.visual.background.mode = "Solid"; p.visual.background.color1 = "#808080"
        p.visual.effects.bloom = false; p.visual.effects.chromaticAberration = false
        func corner(_ vig: Bool) -> Int {
            p.visual.effects.vignette = vig; p.visual.effects.vignetteAmount = 1
            let px = renderFrame(r, p, frames: [silentFrames(1).last!], w: 128, h: 128)
            return Int(px[2])
        }
        XCTAssertTrue(corner(true) < corner(false) - 30)
    }

    func testColourGradingAndScanlines() {
        var p = lib.builtIn[0]
        p.visual.background.mode = "Solid"; p.visual.background.color1 = "#806040"
        p.visual.effects.bloom = false; p.visual.effects.vignette = false; p.visual.effects.chromaticAberration = false
        let f = [silentFrames(1).last!]
        let base = totalSum(renderFrame(r, p, frames: f, w: 64, h: 64))
        p.visual.effects.colorGrading = true; p.visual.effects.brightness = 0.2
        XCTAssertTrue(totalSum(renderFrame(r, p, frames: f, w: 64, h: 64)) > base)
        p.visual.effects.colorGrading = false; p.visual.effects.scanlines = true; p.visual.effects.scanlineAmount = 1
        XCTAssertTrue(totalSum(renderFrame(r, p, frames: f, w: 64, h: 64)) < base)
    }

    func testResizingWorksAndKeepsSize() {
        let p = lib.builtIn[0]
        let f = [loudFrames().last!]
        XCTAssertEqual(renderFrame(r, p, frames: f, w: 320, h: 180).count, 320 * 180 * 4)
        XCTAssertEqual(renderFrame(r, p, frames: f, w: 640, h: 360).count, 640 * 360 * 4)
        XCTAssertEqual(renderFrame(r, p, frames: f, w: 180, h: 320).count, 180 * 320 * 4)
        XCTAssertEqual(renderFrame(r, p, frames: f, w: 100, h: 100).count, 100 * 100 * 4)
    }

    func testFrameTimeIsReasonable() {
        let p = lib.builtIn.first { $0.visualizerId == "reactive-sphere" }!
        let e = VisualEngine(preset: p)!
        let frames = loudFrames()
        var scene = SceneParams()
        let t0 = Date()
        for i in 0..<30 {
            scene = e.step(frame: frames[i], dt: 1.0 / 60, aspect: 16.0 / 9.0)
            _ = r.renderToBytes(canvas: e.canvas, params: scene, width: 1280, height: 720)
        }
        let perFrame = Date().timeIntervalSince(t0) / 30
        print("  720p sphere frame incl. readback: \(Int(perFrame * 1000)) ms")
        XCTAssertTrue(perFrame < 0.5, "frame took \(perFrame)s")
    }
}
