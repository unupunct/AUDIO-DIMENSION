using AudioDimension.Visuals.Gl;
using AudioDimension.Visuals.Text;
using OpenTK.Graphics.OpenGL4;
using SkiaSharp;

namespace AudioDimension.Visuals.Render;

/// <summary>Rasterises text layers with Skia and alpha-blends them over the finished frame (after post-processing, so text stays crisp).</summary>
internal sealed class TextOverlay : IDisposable
{
    private const string Vert = """
        #version 330 core
        out vec2 vUV;
        void main(){ vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2); vUV = vec2(p.x, 1.0 - p.y); gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0); }
        """;
    private const string Frag = """
        #version 330 core
        in vec2 vUV; out vec4 o; uniform sampler2D uTex;
        void main(){ o = texture(uTex, vUV); }
        """;

    private readonly GlShader _shader = new("text-overlay", Vert, Frag);
    private readonly int _vao = GL.GenVertexArray();
    private int _tex, _w, _h;
    private SKBitmap? _bitmap;
    private SKCanvas? _canvas;

    public void Draw(IEnumerable<TextLayer> layers, double t, int w, int h, int targetFbo)
    {
        var list = layers as IReadOnlyCollection<TextLayer> ?? layers.ToList();
        if (list.Count == 0 || !list.Any(l => TextAnimator.IsActive(l, t))) return;
        if (_bitmap is null || _w != w || _h != h) Allocate(w, h);

        _canvas!.Clear(SKColors.Transparent);
        TextRenderer.Render(_canvas, w, h, list, t);

        GL.BindTexture(TextureTarget.Texture2D, _tex);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, w, h, PixelFormat.Rgba, PixelType.UnsignedByte, _bitmap!.GetPixels());

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFbo);
        GL.Viewport(0, 0, w, h);
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);   // Skia output is premultiplied
        _shader.Use(); _shader.Set("uTex", 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.Disable(EnableCap.Blend);
    }

    private void Allocate(int w, int h)
    {
        _canvas?.Dispose(); _bitmap?.Dispose();
        _bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        _canvas = new SKCanvas(_bitmap);
        if (_tex == 0) _tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, w, h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _w = w; _h = h;
    }

    public void Dispose()
    {
        _canvas?.Dispose(); _bitmap?.Dispose();
        if (_tex != 0) GL.DeleteTexture(_tex);
        GL.DeleteVertexArray(_vao); _shader.Dispose();
    }
}
