import Foundation
import Metal
import simd

public enum RendererError: Error, LocalizedError {
    case noDevice, shader(String), pipeline(String)
    public var errorDescription: String? {
        switch self {
        case .noDevice: return "No Metal-capable graphics device was found."
        case .shader(let m): return "Shader compilation failed: \(m)"
        case .pipeline(let m): return "Could not create render pipeline: \(m)"
        }
    }
}

/// Metal renderer: background + MSAA geometry -> bloom (threshold, 2x separable blur at half size) -> composite with effects.
/// The shaders are compiled from source at start-up so the app builds with only the Command Line Tools (no `metal` compiler needed).
public final class MetalRenderer {
    public let device: MTLDevice
    private let queue: MTLCommandQueue
    private var pBG: MTLRenderPipelineState!, pSolid: MTLRenderPipelineState!, pAdd: MTLRenderPipelineState!
    private var pBright: MTLRenderPipelineState!, pBlur: MTLRenderPipelineState!, pFinal: MTLRenderPipelineState!
    private var sceneTex: MTLTexture?, msaaTex: MTLTexture?, bloomA: MTLTexture?, bloomB: MTLTexture?
    private var texSize = (0, 0)
    private let samples: Int
    public static let pixelFormat = MTLPixelFormat.bgra8Unorm

    public init(device: MTLDevice? = nil) throws {
        guard let d = device ?? MTLCreateSystemDefaultDevice() else { throw RendererError.noDevice }
        self.device = d
        queue = d.makeCommandQueue()!
        samples = d.supportsTextureSampleCount(4) ? 4 : 1
        let lib: MTLLibrary
        do { lib = try d.makeLibrary(source: MetalRenderer.shaderSource, options: nil) }
        catch { throw RendererError.shader(error.localizedDescription) }

        func pipe(_ vs: String, _ fs: String, blend: Int, count: Int) throws -> MTLRenderPipelineState {
            let desc = MTLRenderPipelineDescriptor()
            desc.vertexFunction = lib.makeFunction(name: vs)
            desc.fragmentFunction = lib.makeFunction(name: fs)
            desc.rasterSampleCount = count
            let a = desc.colorAttachments[0]!
            a.pixelFormat = MetalRenderer.pixelFormat
            if blend != 0 {
                a.isBlendingEnabled = true
                a.rgbBlendOperation = .add; a.alphaBlendOperation = .add
                a.sourceRGBBlendFactor = .sourceAlpha
                a.sourceAlphaBlendFactor = .one
                a.destinationRGBBlendFactor = blend == 1 ? .oneMinusSourceAlpha : .one
                a.destinationAlphaBlendFactor = blend == 1 ? .oneMinusSourceAlpha : .one
            }
            do { return try d.makeRenderPipelineState(descriptor: desc) } catch { throw RendererError.pipeline("\(fs): \(error.localizedDescription)") }
        }
        pBG = try pipe("fs_vertex", "bg_fragment", blend: 0, count: samples)
        pSolid = try pipe("geo_vertex", "geo_fragment", blend: 1, count: samples)
        pAdd = try pipe("geo_vertex", "geo_fragment", blend: 2, count: samples)
        pBright = try pipe("fs_vertex", "bright_fragment", blend: 0, count: 1)
        pBlur = try pipe("fs_vertex", "blur_fragment", blend: 0, count: 1)
        pFinal = try pipe("fs_vertex", "final_fragment", blend: 0, count: 1)
    }

    private func texture(_ w: Int, _ h: Int, samples: Int = 1) -> MTLTexture {
        let d = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: MetalRenderer.pixelFormat, width: max(w, 1), height: max(h, 1), mipmapped: false)
        d.usage = [.renderTarget, .shaderRead]
        if samples > 1 { d.textureType = .type2DMultisample; d.sampleCount = samples; d.storageMode = .private }
        else { d.storageMode = .private }
        return device.makeTexture(descriptor: d)!
    }

    private func ensureTextures(_ w: Int, _ h: Int) {
        if texSize == (w, h), sceneTex != nil { return }
        texSize = (w, h)
        sceneTex = texture(w, h)
        msaaTex = samples > 1 ? texture(w, h, samples: samples) : nil
        bloomA = texture(w / 2, h / 2)
        bloomB = texture(w / 2, h / 2)
    }

    private func buffer(_ v: [Vertex]) -> MTLBuffer? {
        guard !v.isEmpty else { return nil }
        return v.withUnsafeBytes { device.makeBuffer(bytes: $0.baseAddress!, length: $0.count, options: .storageModeShared) }
    }

    private struct SceneU { var aspect, time, mode, reactive, speed, level, beat, pad: Float; var c1: SIMD4<Float>; var c2: SIMD4<Float> }
    private struct FinalU { var aspect, time, bloom, vignette, chroma, grain, scan, sat, contrast, bright, pad1, pad2: Float }

    /// Encode a full frame into `target` (must be bgra8Unorm, render-target capable). Returns the committed command buffer.
    @discardableResult
    public func render(canvas: Canvas, params p: SceneParams, target: MTLTexture, readbackSync: Bool = false, present: MTLDrawable? = nil) -> MTLCommandBuffer {
        ensureTextures(target.width, target.height)
        let cb = queue.makeCommandBuffer()!
        let w = Float(target.width), h = Float(target.height)

        // 1. Background + geometry into the (MSAA) scene texture.
        let pd = MTLRenderPassDescriptor()
        if let ms = msaaTex {
            pd.colorAttachments[0].texture = ms
            pd.colorAttachments[0].resolveTexture = sceneTex
            pd.colorAttachments[0].storeAction = .multisampleResolve
        } else {
            pd.colorAttachments[0].texture = sceneTex
            pd.colorAttachments[0].storeAction = .store
        }
        pd.colorAttachments[0].loadAction = .clear
        pd.colorAttachments[0].clearColor = MTLClearColor(red: 0, green: 0, blue: 0, alpha: 1)
        var su = SceneU(aspect: p.aspect, time: p.time, mode: p.bgMode, reactive: p.bgReactive, speed: p.bgSpeed, level: p.level, beat: p.beat, pad: 0, c1: p.bg1, c2: p.bg2)
        var aspect = p.aspect
        let enc = cb.makeRenderCommandEncoder(descriptor: pd)!
        enc.setRenderPipelineState(pBG)
        enc.setFragmentBytes(&su, length: MemoryLayout<SceneU>.stride, index: 0)
        enc.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 3)
        if let b = buffer(canvas.solid) {
            enc.setRenderPipelineState(pSolid)
            enc.setVertexBuffer(b, offset: 0, index: 0)
            enc.setVertexBytes(&aspect, length: 4, index: 1)
            enc.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: canvas.solid.count)
        }
        if let b = buffer(canvas.glowV) {
            enc.setRenderPipelineState(pAdd)
            enc.setVertexBuffer(b, offset: 0, index: 0)
            enc.setVertexBytes(&aspect, length: 4, index: 1)
            enc.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: canvas.glowV.count)
        }
        enc.endEncoding()

        // 2. Bloom: bright pass (half size), then two separable blur iterations.
        func fullscreen(_ dst: MTLTexture, _ pipeline: MTLRenderPipelineState, _ src: [MTLTexture], bytes: inout [Float]) {
            let d = MTLRenderPassDescriptor()
            d.colorAttachments[0].texture = dst
            d.colorAttachments[0].loadAction = .dontCare
            d.colorAttachments[0].storeAction = .store
            let e = cb.makeRenderCommandEncoder(descriptor: d)!
            e.setRenderPipelineState(pipeline)
            for (i, t) in src.enumerated() { e.setFragmentTexture(t, index: i) }
            bytes.withUnsafeBytes { e.setFragmentBytes($0.baseAddress!, length: $0.count, index: 0) }
            e.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 3)
            e.endEncoding()
        }
        let bw = Float(bloomA!.width), bh = Float(bloomA!.height)
        var thr = [p.bloomThreshold]
        fullscreen(bloomA!, pBright, [sceneTex!], bytes: &thr)
        for it in 0..<2 {
            let r: Float = it == 0 ? 1.0 : 2.2
            var dirH = [r / bw, 0]; fullscreen(bloomB!, pBlur, [bloomA!], bytes: &dirH)
            var dirV = [0, r / bh]; fullscreen(bloomA!, pBlur, [bloomB!], bytes: &dirV)
        }

        // 3. Composite with effects into the target.
        var fu = [p.aspect, p.time, p.bloom, p.vignette, p.chroma, p.grain, p.scanlines, p.saturation, p.contrast, p.brightness, w, h]
        fullscreen(target, pFinal, [sceneTex!, bloomA!], bytes: &fu)

        if readbackSync, let blit = cb.makeBlitCommandEncoder() {
            blit.synchronize(resource: target)
            blit.endEncoding()
        }
        if let d = present { cb.present(d) }
        cb.commit()
        return cb
    }

    /// Render one frame to a BGRA byte array (offline export, screenshots, tests).
    public func renderToBytes(canvas: Canvas, params: SceneParams, width: Int, height: Int) -> [UInt8] {
        let d = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: MetalRenderer.pixelFormat, width: width, height: height, mipmapped: false)
        d.usage = [.renderTarget, .shaderRead]
        d.storageMode = .managed
        let t = device.makeTexture(descriptor: d)!
        let cb = render(canvas: canvas, params: params, target: t, readbackSync: true)
        cb.waitUntilCompleted()
        var bytes = [UInt8](repeating: 0, count: width * height * 4)
        t.getBytes(&bytes, bytesPerRow: width * 4, from: MTLRegionMake2D(0, 0, width, height), mipmapLevel: 0)
        return bytes
    }

    static let shaderSource = """
    #include <metal_stdlib>
    using namespace metal;

    struct Vertex { packed_float2 pos; packed_float4 color; };
    struct GeoOut { float4 position [[position]]; float4 color; };
    vertex GeoOut geo_vertex(uint vid [[vertex_id]], const device Vertex* v [[buffer(0)]], constant float& aspect [[buffer(1)]]) {
        GeoOut o;
        float2 p = float2(v[vid].pos);
        o.position = float4(p.x / aspect, p.y, 0.0, 1.0);
        o.color = float4(v[vid].color);
        return o;
    }
    fragment float4 geo_fragment(GeoOut in [[stage_in]]) { return in.color; }

    struct FSOut { float4 position [[position]]; float2 uv; };
    vertex FSOut fs_vertex(uint vid [[vertex_id]]) {
        float2 p = float2(float((vid << 1) & 2u), float(vid & 2u));
        FSOut o;
        o.position = float4(p * 2.0 - 1.0, 0.0, 1.0);
        o.uv = float2(p.x, 1.0 - p.y);
        return o;
    }

    float hash21(float2 p) {
        p = fract(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return fract(p.x * p.y);
    }

    struct SceneU { float aspect, time, mode, reactive, speed, level, beat, pad; float4 c1; float4 c2; };
    fragment float4 bg_fragment(FSOut in [[stage_in]], constant SceneU& u [[buffer(0)]]) {
        float2 uv = in.uv;
        int mode = int(u.mode + 0.5);
        float pulse = 1.0 + u.reactive * 0.25 * (u.level * 0.6 + u.beat * 0.4);
        float3 col;
        if (mode == 0) {
            col = u.c1.rgb * pulse;
        } else if (mode == 1) {
            col = mix(u.c1.rgb, u.c2.rgb, uv.y) * pulse;
        } else if (mode == 2) {
            float t = u.time * u.speed;
            float2 d = float2(cos(t * 0.7), sin(t * 0.5));
            float k = 0.5 + 0.5 * sin(dot(uv, d) * 3.0 + t);
            col = mix(u.c1.rgb, u.c2.rgb, k) * pulse;
        } else {
            col = mix(u.c1.rgb, u.c2.rgb, uv.y) * pulse;
            for (int l = 0; l < 3; l++) {
                float s = 40.0 + 35.0 * float(l);
                float2 g = uv * float2(s * u.aspect, s);
                float2 id = floor(g);
                float h = hash21(id + float(l) * 17.0);
                if (h > 0.965) {
                    float2 f = fract(g) - 0.5;
                    float tw = 0.6 + 0.4 * sin(u.time * (1.0 + h * 3.0) * max(u.speed, 0.2) + h * 50.0);
                    col += (1.0 - smoothstep(0.0, 0.35, length(f))) * tw * (0.4 + 0.2 * float(l));
                }
            }
        }
        return float4(col, 1.0);
    }

    fragment float4 bright_fragment(FSOut in [[stage_in]], texture2d<float> tex [[texture(0)]], constant float& thr [[buffer(0)]]) {
        constexpr sampler s(filter::linear, address::clamp_to_edge);
        float2 px = 1.0 / float2(tex.get_width(), tex.get_height());
        float3 c = (tex.sample(s, in.uv + px * float2(-0.5, -0.5)).rgb + tex.sample(s, in.uv + px * float2(0.5, -0.5)).rgb +
                    tex.sample(s, in.uv + px * float2(-0.5, 0.5)).rgb + tex.sample(s, in.uv + px * float2(0.5, 0.5)).rgb) * 0.25;
        float l = max(c.r, max(c.g, c.b));
        float k = max(l - thr, 0.0) / max(l, 0.0001);
        return float4(c * k, 1.0);
    }

    fragment float4 blur_fragment(FSOut in [[stage_in]], texture2d<float> tex [[texture(0)]], constant float2& dir [[buffer(0)]]) {
        constexpr sampler s(filter::linear, address::clamp_to_edge);
        const float w[5] = { 0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216 };
        float3 c = tex.sample(s, in.uv).rgb * w[0];
        for (int i = 1; i < 5; i++) {
            c += tex.sample(s, in.uv + dir * float(i)).rgb * w[i];
            c += tex.sample(s, in.uv - dir * float(i)).rgb * w[i];
        }
        return float4(c, 1.0);
    }

    struct FinalU { float aspect, time, bloom, vignette, chroma, grain, scan, sat, contrast, bright, w, h; };
    fragment float4 final_fragment(FSOut in [[stage_in]], texture2d<float> scene [[texture(0)]], texture2d<float> bloom [[texture(1)]], constant FinalU& u [[buffer(0)]]) {
        constexpr sampler s(filter::linear, address::clamp_to_edge);
        float2 uv = in.uv;
        float2 cen = uv - 0.5;
        float3 col;
        col.r = scene.sample(s, uv + cen * u.chroma).r;
        col.g = scene.sample(s, uv).g;
        col.b = scene.sample(s, uv - cen * u.chroma).b;
        col += bloom.sample(s, uv).rgb * u.bloom;
        float l = dot(col, float3(0.299, 0.587, 0.114));
        col = mix(float3(l), col, u.sat);
        col = (col - 0.5) * u.contrast + 0.5 + u.bright;
        float v = 1.0 - u.vignette * smoothstep(0.35, 0.95, length(cen * float2(u.aspect, 1.0) * 0.9));
        col *= v;
        if (u.scan > 0.0) col *= 1.0 - u.scan * 0.35 * (0.5 + 0.5 * sin(uv.y * u.h * 3.14159));
        if (u.grain > 0.0) col += (hash21(uv * 1000.0 + fract(u.time)) - 0.5) * u.grain;
        return float4(clamp(col, 0.0, 1.0), 1.0);
    }
    """
}
