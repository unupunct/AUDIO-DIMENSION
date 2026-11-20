import Foundation
import simd

/// Everything the renderer needs besides geometry. Layout must match `SceneU` / `FinalU` in the shader source.
public struct SceneParams {
    public var aspect: Float = 16.0 / 9.0
    public var time: Float = 0
    public var level: Float = 0
    public var beat: Float = 0
    /// 0 solid, 1 gradient, 2 animated gradient, 3 starfield
    public var bgMode: Float = 1
    public var bgSpeed: Float = 0.3
    public var bgReactive: Float = 0.5
    public var bg1 = RGBA(0.02, 0.01, 0.05, 1)
    public var bg2 = RGBA(0.1, 0.02, 0.2, 1)
    public var bloom: Float = 1, bloomThreshold: Float = 0.55
    public var vignette: Float = 0.4, chroma: Float = 0, grain: Float = 0, scanlines: Float = 0
    public var saturation: Float = 1, contrast: Float = 1, brightness: Float = 0
    public init() {}
}

/// Turns a preset plus audio frames into geometry. Deterministic: the same preset, frames and dt always give the same picture.
public final class VisualEngine {
    public private(set) var preset: Preset
    private var visualizer: Visualizer
    public let canvas = Canvas()
    private var phase: Float = 0
    private var time: Float = 0
    private var bgPhase: Float = 0

    public init?(preset: Preset) {
        guard let v = VisualizerRegistry.make(preset.visualizerId) else { return nil }
        self.preset = preset; visualizer = v
    }

    /// Switch preset; the visualizer's internal state (particles, history) starts fresh.
    public func setPreset(_ p: Preset) {
        guard let v = VisualizerRegistry.make(p.visualizerId) else { return }
        preset = p; visualizer = v
    }

    /// Edit the working copy in place (inspector sliders) without resetting the visualizer.
    public func update(_ change: (inout Preset) -> Void) { change(&preset) }

    public func reset() { phase = 0; time = 0; bgPhase = 0; if let v = VisualizerRegistry.make(preset.visualizerId) { visualizer = v } }

    @discardableResult
    public func step(frame: AudioFrame, dt: Float, aspect: Float) -> SceneParams {
        let dt = dt.clamped(0, 0.1)
        let p = preset.visual
        time += dt
        phase += dt * Float(p.params.speed)
        bgPhase += dt * Float(p.background.animationSpeed)
        canvas.clear()
        canvas.aspect = aspect
        canvas.glow = Float(p.params.glow)
        let ctx = VisualContext(params: p.params, colors: p.palette.colors.map { parseHexColor($0) },
                                phase: phase, time: time, dt: dt, aspect: aspect, frame: frame)
        visualizer.draw(canvas, ctx)

        var s = SceneParams()
        s.aspect = aspect; s.time = bgPhase * 3; s.level = frame.level; s.beat = frame.beat
        switch p.background.mode {
        case "Solid", "Transparent": s.bgMode = 0
        case "Gradient": s.bgMode = 1
        case "AnimatedGradient": s.bgMode = 2
        default: s.bgMode = 3
        }
        s.bgSpeed = 1; s.bgReactive = Float(p.background.audioReactive)
        s.bg1 = parseHexColor(p.background.color1); s.bg2 = parseHexColor(p.background.color2)
        let fx = p.effects
        s.bloom = fx.bloom ? Float(fx.bloomAmount) : 0
        s.bloomThreshold = Float(fx.bloomThreshold)
        s.vignette = fx.vignette ? Float(fx.vignetteAmount) : 0
        s.chroma = fx.chromaticAberration ? Float(fx.chromaticAmount) : 0
        s.grain = fx.filmGrain ? Float(fx.filmGrainAmount) : 0
        s.scanlines = fx.scanlines ? Float(fx.scanlineAmount) : 0
        if fx.colorGrading { s.saturation = Float(fx.saturation); s.contrast = Float(fx.contrast); s.brightness = Float(fx.brightness) }
        return s
    }
}
