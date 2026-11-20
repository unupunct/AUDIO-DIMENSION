import Foundation
import simd

public struct VisualContext {
    public var params: PresetParams
    public var colors: [RGBA]
    /// Accumulated `dt * speed`, so changing speed never makes the picture jump.
    public var phase: Float
    public var time: Float
    public var dt: Float
    public var aspect: Float
    public var frame: AudioFrame

    public var lw: Float { Float(params.lineWidth) * 0.0030 }
    public var reactivity: Float { Float(params.reactivity) }
    public var scale: Float { Float(params.scale) }
    public var glow: Float { Float(params.glow) }

    /// Palette lookup, t in 0...1 interpolates across the colours.
    public func color(_ t: Float, alpha: Float = 1) -> RGBA {
        guard colors.count > 1 else { var c = colors.first ?? RGBA(1, 1, 1, 1); c.w = alpha; return c }
        let f = t.clamped(0, 1) * Float(colors.count - 1)
        let i = min(Int(f), colors.count - 2)
        var c = simd_mix(colors[i], colors[i + 1], RGBA(repeating: f - Float(i)))
        c.w = alpha
        return c
    }

    /// Spectrum at t (0 = lowest, 1 = highest frequency), interpolated.
    public func bin(_ t: Float) -> Float {
        let f = t.clamped(0, 1) * Float(AudioFrame.spectrumBins - 1)
        let i = min(Int(f), AudioFrame.spectrumBins - 2)
        let k = f - Float(i)
        return frame.spectrum[i] * (1 - k) + frame.spectrum[i + 1] * k
    }
}

public protocol Visualizer: AnyObject {
    func draw(_ c: Canvas, _ x: VisualContext)
}

/// Small deterministic generator so offline renders are reproducible.
struct SplitMix {
    var s: UInt64
    init(seed: UInt64) { s = seed }
    mutating func next() -> UInt64 {
        s &+= 0x9E3779B97F4A7C15
        var z = s
        z = (z ^ (z >> 30)) &* 0xBF58476D1CE4E5B9
        z = (z ^ (z >> 27)) &* 0x94D049BB133111EB
        return z ^ (z >> 31)
    }
    mutating func float() -> Float { Float(next() >> 40) / Float(1 << 24) }
    mutating func range(_ lo: Float, _ hi: Float) -> Float { lo + (hi - lo) * float() }
}

public enum VisualizerRegistry {
    static let factories: [String: () -> Visualizer] = [
        "classic-spectrum": { ClassicSpectrum() },
        "neon-equalizer": { NeonEqualizer() },
        "circular-spectrum": { CircularSpectrum() },
        "circular-oscilloscope": { CircularOscilloscope() },
        "neon-waveform": { NeonWaveform() },
        "audio-ribbon": { AudioRibbon() },
        "reactive-rings": { ReactiveRings() },
        "energy-waves": { EnergyWaves() },
        "frequency-mountains": { FrequencyMountains() },
        "starfield": { Starfield() },
        "fireflies": { Fireflies() },
        "particle-explosion": { ParticleExplosion() },
        "vortex": { Vortex() },
        "reactive-sphere": { ReactiveSphere() },
    ]
    public static var ids: [String] { factories.keys.sorted() }
    public static func make(_ id: String) -> Visualizer? { factories[id]?() }
}

// MARK: - Spectrum family

final class ClassicSpectrum: Visualizer {
    var caps = [Float](repeating: 0, count: 130)
    func draw(_ c: Canvas, _ x: VisualContext) {
        let n = x.params.segments > 0 ? max(8, x.params.segments) : 48
        let span = x.aspect * 0.9, variant = x.params.variant
        let slot = span * 2 / Float(n), barW = slot * 0.62
        var tops: [P2] = [], topColors: [RGBA] = []
        let baseY: Float = variant == 1 ? 0 : -0.55
        for i in 0..<n {
            let t = Float(i) / Float(max(n - 1, 1))
            let tt = x.params.mirror ? abs(t * 2 - 1) : t
            let v = min(1.2, x.bin(tt) * x.reactivity)
            let h = v * 1.05 * x.scale
            let cx = -span + (Float(i) + 0.5) * slot
            let col = x.color(t, alpha: 0.95)
            caps[i] = max(v, caps[i] - x.dt * 0.6)
            switch variant {
            case 1:
                c.rect(x: cx - barW / 2, y: -h / 2, w: barW, h: h, col)
            case 3:
                tops.append(P2(cx, baseY + h)); topColors.append(col)
            default:
                c.rect(x: cx - barW / 2, y: baseY, w: barW, h: h, col)
                if variant == 2 {
                    var refl = col; refl.w = 0.22
                    c.rect(x: cx - barW / 2, y: baseY - h * 0.45, w: barW, h: h * 0.45, refl)
                }
            }
            if x.glow > 0.01 && variant != 3 {
                var g = col; g.w = 0.10 * min(x.glow, 4)
                let pad = barW * 0.5 * (1 + x.glow)
                c.rect(x: cx - barW / 2 - pad, y: baseY - (variant == 1 ? h / 2 : 0) - pad, w: barW + pad * 2, h: h + pad * 2, g, additive: true)
            }
            if variant != 3 && variant != 1 {
                c.rect(x: cx - barW / 2, y: baseY + caps[i] * 1.05 * x.scale + 0.012, w: barW, h: 0.012, x.color(t, alpha: 0.9))
            }
        }
        if variant == 3 { c.gradientPolyline(tops, colors: topColors, width: x.lw * 1.4) }
        c.line(P2(-span, baseY - 0.01), P2(span, baseY - 0.01), width: x.lw * 0.5, x.color(0.5, alpha: 0.35), halo: false)
    }
}

final class NeonEqualizer: Visualizer {
    var caps = [Float](repeating: 0, count: 66)
    func draw(_ c: Canvas, _ x: VisualContext) {
        let n = x.params.segments > 0 ? min(max(x.params.segments, 8), 64) : 32
        let rows = 18, span = x.aspect * 0.86
        let slot = span * 2 / Float(n), w = slot * 0.78
        let cellH = 1.3 * x.scale / Float(rows), h = cellH * 0.7
        let y0 = -0.62 * x.scale
        for i in 0..<n {
            let t = Float(i) / Float(max(n - 1, 1))
            let v = min(1, x.bin(x.params.mirror ? abs(t * 2 - 1) : t) * x.reactivity)
            let lit = Int(v * Float(rows) + 0.5)
            caps[i] = max(Float(lit), caps[i] - x.dt * 8)
            let cx = -span + Float(i) * slot + (slot - w) / 2
            for r in 0..<rows {
                let rt = Float(r) / Float(rows - 1)
                let on = r < lit || r == Int(caps[i])
                var col = x.color(rt, alpha: on ? 0.95 : 0.06)
                if r == Int(caps[i]) && r >= lit { col.w = 0.8 }
                c.rect(x: cx, y: y0 + Float(r) * cellH, w: w, h: h, col)
                if on && x.glow > 0.01 {
                    var g = col; g.w = 0.07 * min(x.glow, 4)
                    c.rect(x: cx - w * 0.3, y: y0 + Float(r) * cellH - h * 0.3, w: w * 1.6, h: h * 1.6, g, additive: true)
                }
            }
        }
    }
}

final class CircularSpectrum: Visualizer {
    func draw(_ c: Canvas, _ x: VisualContext) {
        let n = x.params.segments > 0 ? max(12, x.params.segments) : 96
        let r0 = 0.34 * x.scale * (1 + 0.12 * x.frame.beat * x.reactivity)
        let spin = Float(x.params.rotation) * .pi / 180 + x.phase * 0.18
        for i in 0..<n {
            let t = Float(i) / Float(n)
            let tt = x.params.mirror || x.params.variant == 1 ? abs(t * 2 - 1) : t
            let v = min(1.3, x.bin(tt) * x.reactivity)
            let a = t * 2 * .pi + spin
            let d = P2(cosf(a), sinf(a))
            let len = 0.03 + v * 0.42 * x.scale
            let col = x.color(tt, alpha: 0.95)
            c.line(d * r0, d * (r0 + len), width: x.lw * 1.1, col, halo: i % 2 == 0)
            if x.params.variant == 2 { c.line(d * r0, d * (r0 - len * 0.35), width: x.lw * 0.8, x.color(tt, alpha: 0.5), halo: false) }
        }
        c.ring(.zero, radius: r0 - 0.012, width: x.lw * 0.7, x.color(0.5, alpha: 0.7))
        if x.params.variant == 3 {
            c.ring(.zero, radius: r0 * 0.6 * (1 + x.frame.level * 0.3), width: x.lw * 0.5, x.color(0.8, alpha: 0.5))
        }
    }
}

// MARK: - Waveform family

final class CircularOscilloscope: Visualizer {
    func draw(_ c: Canvas, _ x: VisualContext) {
        let n = AudioFrame.waveformSamples
        let base = 0.42 * x.scale
        let spin = Float(x.params.rotation) * .pi / 180 + x.phase * 0.1
        func ringPoints(offset: Int, radius: Float, gain: Float) -> [P2] {
            (0..<n).map { i in
                let a = Float(i) / Float(n) * 2 * .pi + spin
                let r = radius + x.frame.waveform[(i + offset) % n] * 0.28 * x.reactivity * gain
                return P2(cosf(a), sinf(a)) * r
            }
        }
        c.polyline(ringPoints(offset: 0, radius: base, gain: 1), width: x.lw * 1.4, x.color(0.1, alpha: 0.95), closed: true)
        if x.params.variant >= 1 {
            c.polyline(ringPoints(offset: n / 2, radius: base * 0.62, gain: 0.7), width: x.lw, x.color(0.8, alpha: 0.85), closed: true)
        }
        if x.params.variant >= 2 {
            c.polyline(ringPoints(offset: n / 4, radius: base * 1.3, gain: 0.5), width: x.lw * 0.8, x.color(0.5, alpha: 0.6), closed: true)
        }
    }
}

final class NeonWaveform: Visualizer {
    var history: [[Float]] = []
    func draw(_ c: Canvas, _ x: VisualContext) {
        history.append(x.frame.waveform)
        if history.count > 5 { history.removeFirst() }
        let n = AudioFrame.waveformSamples, span = x.aspect * 0.92
        func pts(_ w: [Float], yOff: Float, gain: Float) -> [P2] {
            (0..<n).map { i in P2(-span + 2 * span * Float(i) / Float(n - 1), yOff + w[i] * 0.55 * x.scale * x.reactivity * gain) }
        }
        let variant = x.params.variant
        for (k, w) in history.enumerated() {
            let age = Float(history.count - 1 - k)
            let alpha = max(0.08, 1 - age * 0.28)
            let col = x.color(Float(k) / 4, alpha: alpha)
            let wd = x.lw * (k == history.count - 1 ? 1.6 : 0.9)
            c.polyline(pts(w, yOff: 0, gain: 1), width: wd, col, halo: k == history.count - 1)
            if variant == 1 || x.params.mirror { c.polyline(pts(w, yOff: 0, gain: -1), width: wd, x.color(1 - Float(k) / 4, alpha: alpha * 0.8), halo: false) }
        }
        if variant == 2 {
            c.polyline(pts(x.frame.waveform, yOff: 0.35, gain: 0.45), width: x.lw, x.color(0.3, alpha: 0.6))
            c.polyline(pts(x.frame.waveform, yOff: -0.35, gain: 0.45), width: x.lw, x.color(0.7, alpha: 0.6))
        }
        if variant == 3 {
            for i in stride(from: 0, to: n, by: 3) {
                let px = -span + 2 * span * Float(i) / Float(n - 1)
                let h = abs(x.frame.waveform[i]) * 0.5 * x.scale * x.reactivity
                c.line(P2(px, -h), P2(px, h), width: x.lw * 0.9, x.color(Float(i) / Float(n), alpha: 0.8), halo: false)
            }
        }
    }
}

final class AudioRibbon: Visualizer {
    func draw(_ c: Canvas, _ x: VisualContext) {
        let n = AudioFrame.waveformSamples, delay = 24 + x.params.variant * 12
        var pts: [P2] = [], cols: [RGBA] = []
        let rot = Float(x.params.rotation) * .pi / 180 + x.phase * 0.12
        let cr = cosf(rot), sr = sinf(rot)
        for i in 0..<n {
            let a = x.frame.waveform[i], b = x.frame.waveform[(i + delay) % n]
            let px = a * 0.8 * x.scale * x.reactivity, py = b * 0.8 * x.scale * x.reactivity
            pts.append(P2(px * cr - py * sr, px * sr + py * cr))
            cols.append(x.color(Float(i) / Float(n), alpha: 0.9))
        }
        c.gradientPolyline(pts, colors: cols, width: x.lw * (1.2 + x.frame.level * 1.2))
        if x.params.variant >= 2 {
            c.gradientPolyline(pts.map { P2(-$0.x, $0.y) }, colors: cols.map { var k = $0; k.w = 0.5; return k }, width: x.lw * 0.8, halo: false)
        }
    }
}

final class EnergyWaves: Visualizer {
    func draw(_ c: Canvas, _ x: VisualContext) {
        let layers = 5 + x.params.variant, span = x.aspect * 0.95, samples = 120
        for l in 0..<layers {
            let band = x.frame.bands[min(l, 5)]
            let amp = (0.06 + band * 0.42) * x.scale * x.reactivity
            let freq = 1.6 + Float(l) * 0.9
            let off = x.phase * (0.8 + Float(l) * 0.35) * (l % 2 == 0 ? 1 : -1)
            var pts: [P2] = []
            for i in 0..<samples {
                let t = Float(i) / Float(samples - 1)
                let env = sinf(t * .pi)
                let y = (sinf(t * freq * 2 * .pi + off) + 0.4 * sinf(t * freq * 5 + off * 1.7)) * amp * env
                pts.append(P2(-span + 2 * span * t, y + (Float(l) - Float(layers - 1) / 2) * 0.08))
            }
            c.polyline(pts, width: x.lw * (1.6 - Float(l) * 0.1), x.color(Float(l) / Float(layers - 1), alpha: 0.85))
        }
    }
}

// MARK: - Geometry family

final class ReactiveRings: Visualizer {
    var pulses: [Float] = []
    func draw(_ c: Canvas, _ x: VisualContext) {
        if x.frame.beat > 0.95 { pulses.append(0) }
        pulses = pulses.map { $0 + x.dt * 0.9 * Float(x.params.speed + 0.3) }.filter { $0 < 1 }
        let rings = 6, spin = Float(x.params.rotation) * .pi / 180 + x.phase * 0.2
        for i in 0..<rings {
            let v = x.frame.bands[i] * x.reactivity
            let r = (0.12 + Float(i) * 0.115 + v * 0.07) * x.scale
            let segs = 120
            var pts: [P2] = []
            for k in 0..<segs {
                let a = Float(k) / Float(segs) * 2 * .pi + spin * (i % 2 == 0 ? 1 : -1)
                let wob: Float = 1 + 0.06 * v * sinf(a * Float(3 + i) + x.phase * 2)
                let radius: Float = r * wob
                pts.append(P2(cosf(a) * radius, sinf(a) * radius))
            }
            c.polyline(pts, width: x.lw * (0.9 + v * 1.8), x.color(Float(i) / Float(rings - 1), alpha: 0.5 + min(v, 1) * 0.5), closed: true)
        }
        for p in pulses {
            c.ring(.zero, radius: 0.1 + p * 0.95 * x.scale, width: x.lw * (1 - p) * 1.2, x.color(0.5, alpha: (1 - p) * 0.8), segments: 90)
        }
        if x.params.variant >= 1 { c.disc(.zero, radius: 0.025 + x.frame.level * 0.05, x.color(1, alpha: 0.9)) }
    }
}

final class FrequencyMountains: Visualizer {
    var rows: [[Float]] = []
    var acc: Float = 0
    func draw(_ c: Canvas, _ x: VisualContext) {
        acc += x.dt * Float(0.6 + x.params.speed)
        while acc > 1.0 / 20 { acc -= 1.0 / 20; rows.append(x.frame.spectrum); if rows.count > 36 { rows.removeFirst() } }
        let count = rows.count, span = x.aspect * 0.95, samples = 56
        for r in 0..<count {
            let depth = Float(count - 1 - r) / 36           // 0 = newest/front
            let persp = 1 - depth * 0.55
            let baseY = -0.62 + depth * 1.05
            var pts: [P2] = []
            for i in 0..<samples {
                let t = Float(i) / Float(samples - 1)
                let idx = min(63, Int(t * 63))
                let v = rows[r][idx] * x.reactivity
                let edge = sinf(t * .pi)
                pts.append(P2((-span + 2 * span * t) * persp, baseY + v * 0.55 * x.scale * persp * (0.3 + 0.7 * edge)))
            }
            // Dark fill below the ridge hides the rows behind it.
            for i in 0..<(samples - 1) {
                c.quad(P2(pts[i].x, baseY - 0.5), pts[i], pts[i + 1], P2(pts[i + 1].x, baseY - 0.5), RGBA(0.02, 0.02, 0.05, 0.9))
            }
            c.polyline(pts, width: x.lw * persp, x.color(depth, alpha: 0.95 - depth * 0.5), halo: r >= count - 3)
        }
    }
}

final class ReactiveSphere: Visualizer {
    func draw(_ c: Canvas, _ x: VisualContext) {
        let lats = 11, lons = 22, steps = 48
        let yaw = x.phase * 0.5 + Float(x.params.rotation) * .pi / 180, pitch: Float = 0.45
        let base = 0.5 * x.scale * (1 + x.frame.bands[1] * 0.35 * x.reactivity)
        func project(_ p: SIMD3<Float>) -> P2 {
            let cy = cosf(yaw), sy = sinf(yaw), cp = cosf(pitch), sp = sinf(pitch)
            let x1 = p.x * cy + p.z * sy, z1 = -p.x * sy + p.z * cy
            let y2 = p.y * cp - z1 * sp, z2 = p.y * sp + z1 * cp
            let persp = 1 / (1 + z2 * 0.25)
            return P2(x1, y2) * persp
        }
        func radius(_ lat: Float) -> Float { base * (1 + 0.28 * x.bin(abs(lat) / .pi * 2) * x.reactivity) }
        for i in 1..<lats {
            let lat = -.pi / 2 + Float(i) / Float(lats) * .pi
            var pts: [P2] = []
            let r = radius(lat)
            for k in 0...steps {
                let lon = Float(k) / Float(steps) * 2 * .pi
                pts.append(project(SIMD3<Float>(cosf(lat) * cosf(lon), sinf(lat), cosf(lat) * sinf(lon)) * r))
            }
            c.polyline(pts, width: x.lw * 0.9, x.color(Float(i) / Float(lats), alpha: 0.9), halo: i % 2 == 0)
        }
        for j in 0..<lons {
            let lon = Float(j) / Float(lons) * 2 * .pi
            var pts: [P2] = []
            for k in 0...steps {
                let lat = -.pi / 2 + Float(k) / Float(steps) * .pi
                pts.append(project(SIMD3<Float>(cosf(lat) * cosf(lon), sinf(lat), cosf(lat) * sinf(lon)) * radius(lat)))
            }
            c.polyline(pts, width: x.lw * 0.6, x.color(Float(j) / Float(lons), alpha: 0.55), halo: false)
        }
    }
}

// MARK: - Particle family

final class Starfield: Visualizer {
    struct Star { var x: Float, y: Float, z: Float }
    var stars: [Star] = []
    var rng = SplitMix(seed: 7)
    func draw(_ c: Canvas, _ x: VisualContext) {
        let target = Int(420 * x.params.particleDensity)
        while stars.count < target { stars.append(Star(x: rng.range(-1, 1), y: rng.range(-1, 1), z: rng.range(0.05, 1))) }
        if stars.count > target { stars.removeLast(stars.count - target) }
        let speed = (0.12 + x.frame.level * 0.9 + x.frame.beat * 0.5) * Float(x.params.speed + 0.2) * x.reactivity.clamped(0.3, 3)
        for i in stars.indices {
            let prevZ = stars[i].z
            stars[i].z -= speed * x.dt
            if stars[i].z < 0.03 { stars[i] = Star(x: rng.range(-1, 1), y: rng.range(-1, 1), z: 1); continue }
            let a = P2(stars[i].x / prevZ, stars[i].y / prevZ) * 0.55 * x.scale
            let b = P2(stars[i].x / stars[i].z, stars[i].y / stars[i].z) * 0.55 * x.scale
            let bright = (1 - stars[i].z).clamped(0, 1)
            if abs(b.x) > x.aspect * 1.2 || abs(b.y) > 1.2 { stars[i] = Star(x: rng.range(-1, 1), y: rng.range(-1, 1), z: 1); continue }
            c.line(a, b, width: x.lw * 0.5 * (0.4 + bright * 1.6), x.color(Float(i % 7) / 6, alpha: 0.25 + bright * 0.75), halo: false)
            if bright > 0.7 { c.softDot(b, radius: 0.012 + bright * 0.02 * x.glow, x.color(Float(i % 7) / 6, alpha: 0.5)) }
        }
    }
}

final class Fireflies: Visualizer {
    struct Fly { var p: P2, v: P2, seed: Float }
    var flies: [Fly] = []
    var rng = SplitMix(seed: 11)
    func draw(_ c: Canvas, _ x: VisualContext) {
        let target = Int(70 * x.params.particleDensity)
        while flies.count < target { flies.append(Fly(p: P2(rng.range(-x.aspect, x.aspect), rng.range(-1, 1)), v: .zero, seed: rng.float())) }
        if flies.count > target { flies.removeLast(flies.count - target) }
        let treble = (x.frame.bands[4] + x.frame.bands[5]) / 2 * x.reactivity
        for i in flies.indices {
            var f = flies[i]
            let a = x.phase * 0.7 + f.seed * 40
            f.v += P2(cosf(a * 1.3 + f.seed * 9), sinf(a * 0.9 + f.seed * 5)) * (0.25 * x.dt)
            f.v *= 0.985
            f.p += f.v * x.dt * 4 * Float(x.params.speed + 0.3)
            if f.p.x > x.aspect { f.p.x = -x.aspect } else if f.p.x < -x.aspect { f.p.x = x.aspect }
            if f.p.y > 1 { f.p.y = -1 } else if f.p.y < -1 { f.p.y = 1 }
            flies[i] = f
            let tw = 0.5 + 0.5 * sinf(x.time * (1.2 + f.seed * 2) + f.seed * 30)
            let b = (0.2 + tw * 0.4 + treble * 0.9).clamped(0, 1.4)
            c.softDot(f.p, radius: (0.025 + treble * 0.04) * x.scale * (0.6 + f.seed * 0.8) * (0.7 + x.glow * 0.3), x.color(f.seed, alpha: b))
            c.disc(f.p, radius: 0.004 * x.scale, x.color(f.seed, alpha: 0.9), segments: 8)
        }
    }
}

final class ParticleExplosion: Visualizer {
    struct P { var p: P2, v: P2, life: Float, hue: Float }
    var parts: [P] = []
    var rng = SplitMix(seed: 3)
    func draw(_ c: Canvas, _ x: VisualContext) {
        if x.frame.beat > 0.95 {
            let n = Int(70 * Float(x.params.particleDensity) * (0.6 + x.frame.level))
            let origin = x.params.variant == 1 ? P2(rng.range(-0.5, 0.5) * x.aspect, rng.range(-0.4, 0.4)) : .zero
            for _ in 0..<n {
                let a = rng.range(0, 2 * .pi), sp = rng.range(0.25, 1.1) * (0.6 + x.frame.level) * x.reactivity.clamped(0.3, 3)
                parts.append(P(p: origin, v: P2(cosf(a), sinf(a)) * sp, life: 1, hue: rng.float()))
            }
            if parts.count > 3000 { parts.removeFirst(parts.count - 3000) }
        }
        let g: Float = x.params.variant == 2 ? -0.5 : 0
        for i in parts.indices.reversed() {
            parts[i].life -= x.dt * 0.55
            if parts[i].life <= 0 { parts.remove(at: i); continue }
            parts[i].v.y += g * x.dt
            parts[i].v *= expf(-x.dt * 0.6)
            let prev = parts[i].p
            parts[i].p += parts[i].v * x.dt * Float(x.params.speed + 0.3) * x.scale
            let l = parts[i].life
            c.line(prev, parts[i].p, width: x.lw * (0.4 + l), x.color(parts[i].hue, alpha: l * 0.9), halo: false)
            if l > 0.6 { c.softDot(parts[i].p, radius: 0.012 * x.glow + 0.006, x.color(parts[i].hue, alpha: (l - 0.6) * 1.5)) }
        }
    }
}

final class Vortex: Visualizer {
    func draw(_ c: Canvas, _ x: VisualContext) {
        let arms = 3 + x.params.variant, perArm = Int(60 * x.params.particleDensity) + 12
        let spin = x.phase * (0.5 + x.frame.bands[1] * 1.5) + Float(x.params.rotation) * .pi / 180
        for a in 0..<arms {
            let armOffset = Float(a) / Float(arms) * 2 * .pi
            var prev: P2? = nil
            for i in 0..<perArm {
                let t = Float(i) / Float(perArm - 1)
                let v = x.bin(t) * x.reactivity
                let r = (0.04 + t * 0.95) * x.scale * (1 + 0.08 * x.frame.beat)
                let ang = armOffset + t * 5.2 + spin * (1.2 - t * 0.6)
                let p = P2(cosf(ang), sinf(ang)) * r
                let col = x.color(t, alpha: 0.9)
                if let q = prev { c.line(q, p, width: x.lw * 0.5, x.color(t, alpha: 0.35), halo: false) }
                c.softDot(p, radius: (0.01 + v * 0.045) * x.scale, col)
                prev = p
            }
        }
        c.softDot(.zero, radius: 0.06 + x.frame.level * 0.1, x.color(0.0, alpha: 0.9))
    }
}
