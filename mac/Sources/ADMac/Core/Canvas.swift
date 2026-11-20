import Foundation
import simd

/// Matches the `Vertex` struct in the Metal shaders (24 bytes, packed).
public struct Vertex {
    public var x: Float, y: Float
    public var r: Float, g: Float, b: Float, a: Float
}

public typealias RGBA = SIMD4<Float>
public typealias P2 = SIMD2<Float>

/// Geometry collector. World space: y in -1...1, x in -aspect...aspect. Everything becomes triangles in two layers:
/// `solid` (alpha blended) and `glow` (additive). Thick lines are expanded on the CPU.
public final class Canvas {
    public private(set) var solid: [Vertex] = []
    public private(set) var glowV: [Vertex] = []
    public var aspect: Float = 16.0 / 9.0
    /// 0 disables the halo, 4 is the strongest.
    public var glow: Float = 1
    public static let maxVertices = 900_000

    public init() { solid.reserveCapacity(60_000); glowV.reserveCapacity(60_000) }

    public func clear() { solid.removeAll(keepingCapacity: true); glowV.removeAll(keepingCapacity: true) }
    public var vertexCount: Int { solid.count + glowV.count }

    @inline(__always) private func add(_ layer: Int, _ p: P2, _ c: RGBA) {
        let v = Vertex(x: p.x, y: p.y, r: c.x, g: c.y, b: c.z, a: c.w)
        if layer == 0 { solid.append(v) } else { glowV.append(v) }
    }

    private func tri(_ layer: Int, _ a: P2, _ b: P2, _ c: P2, _ ca: RGBA, _ cb: RGBA, _ cc: RGBA) {
        guard vertexCount < Canvas.maxVertices else { return }
        add(layer, a, ca); add(layer, b, cb); add(layer, c, cc)
    }

    // MARK: primitives (all take a layer so visualizers can choose solid or additive)

    public func quad(_ a: P2, _ b: P2, _ c: P2, _ d: P2, _ col: RGBA, additive: Bool = false) {
        let l = additive ? 1 : 0
        tri(l, a, b, c, col, col, col); tri(l, a, c, d, col, col, col)
    }

    public func rect(x: Float, y: Float, w: Float, h: Float, _ col: RGBA, additive: Bool = false) {
        quad(P2(x, y), P2(x + w, y), P2(x + w, y + h), P2(x, y + h), col, additive: additive)
    }

    /// Thick line segment with a soft halo when `glow` > 0.
    public func line(_ a: P2, _ b: P2, width: Float, _ col: RGBA, halo: Bool = true) {
        segment(0, a, b, width, col)
        if halo, glow > 0.01 {
            var g = col; g.w *= 0.20 * min(glow, 4)
            segment(1, a, b, width * (2.6 + glow), g)
        }
    }

    private func segment(_ layer: Int, _ a: P2, _ b: P2, _ width: Float, _ col: RGBA) {
        var d = b - a
        let len = simd_length(d)
        if len < 1e-6 { return }
        d /= len
        let n = P2(-d.y, d.x) * (width * 0.5)
        let e = d * (width * 0.5) * 0.0   // butt caps; overlapping segments hide the joins
        tri(layer, a - n - e, a + n - e, b + n + e, col, col, col)
        tri(layer, a - n - e, b + n + e, b - n + e, col, col, col)
    }

    public func polyline(_ pts: [P2], width: Float, _ col: RGBA, closed: Bool = false, halo: Bool = true) {
        guard pts.count >= 2 else { return }
        for i in 0..<(pts.count - 1) { line(pts[i], pts[i + 1], width: width, col, halo: false) }
        if closed { line(pts[pts.count - 1], pts[0], width: width, col, halo: false) }
        if halo, glow > 0.01 {
            var g = col; g.w *= 0.20 * min(glow, 4)
            let w = width * (2.6 + glow)
            for i in 0..<(pts.count - 1) { segment(1, pts[i], pts[i + 1], w, g) }
            if closed { segment(1, pts[pts.count - 1], pts[0], w, g) }
        }
    }

    /// Polyline whose colour varies along its length.
    public func gradientPolyline(_ pts: [P2], colors: [RGBA], width: Float, halo: Bool = true) {
        guard pts.count >= 2, colors.count == pts.count else { return }
        for i in 0..<(pts.count - 1) {
            var c = colors[i]; c.w = colors[i].w
            line(pts[i], pts[i + 1], width: width, c, halo: halo && i % 2 == 0)
        }
    }

    public func disc(_ c: P2, radius: Float, _ col: RGBA, segments: Int = 20, additive: Bool = false) {
        let l = additive ? 1 : 0
        var prev = c + P2(radius, 0)
        for i in 1...segments {
            let a = Float(i) / Float(segments) * 2 * .pi
            let p = c + P2(cosf(a), sinf(a)) * radius
            tri(l, c, prev, p, col, col, col)
            prev = p
        }
    }

    /// Soft additive blob: opaque centre fading to nothing at the rim.
    public func softDot(_ c: P2, radius: Float, _ col: RGBA, segments: Int = 14) {
        var edge = col; edge.w = 0
        var prev = c + P2(radius, 0)
        for i in 1...segments {
            let a = Float(i) / Float(segments) * 2 * .pi
            let p = c + P2(cosf(a), sinf(a)) * radius
            tri(1, c, prev, p, col, edge, edge)
            prev = p
        }
    }

    public func ring(_ c: P2, radius: Float, width: Float, _ col: RGBA, segments: Int = 96, halo: Bool = true) {
        var pts: [P2] = []
        pts.reserveCapacity(segments)
        for i in 0..<segments {
            let a = Float(i) / Float(segments) * 2 * .pi
            pts.append(c + P2(cosf(a), sinf(a)) * radius)
        }
        polyline(pts, width: width, col, closed: true, halo: halo)
    }
}
