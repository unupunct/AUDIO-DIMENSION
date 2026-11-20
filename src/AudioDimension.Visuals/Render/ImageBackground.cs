using AudioDimension.Visuals.Gl;
using OpenTK.Graphics.OpenGL4;
using SkiaSharp;

namespace AudioDimension.Visuals.Render;

/// <summary>Loads a picture into a GL texture and draws it "cover"-fitted behind the visualization, with a gentle audio-driven zoom.</summary>
internal sealed class ImageBackground : IDisposable
{
    private const string Frag = """
        #version 330 core
        in vec2 vUV; out vec4 o;
        uniform sampler2D uTex; uniform float uScreenAspect; uniform float uImageAspect; uniform float uZoom; uniform float uDim;
        void main(){
            vec2 st = vUV - 0.5;
            if (uScreenAspect > uImageAspect) st.y *= uImageAspect / uScreenAspect; else st.x *= uScreenAspect / uImageAspect;   // cover
            st = st / uZoom + 0.5;
            vec3 c = texture(uTex, vec2(st.x, 1.0 - st.y)).rgb;
            o = vec4(c * (1.0 - uDim), 1.0);
        }
        """;

    private readonly GlShader _shader = new("image-bg", Shaders.FullscreenVert, Frag);
    private readonly int _vao = GL.GenVertexArray();
    private int _tex;
    private string? _loaded; private DateTime _stamp;
    private float _aspect = 1;
    public string? Error { get; private set; }
    public bool Ready => _tex != 0 && Error is null;

    /// <summary>Ensures the texture for <paramref name="path"/> is loaded; returns false (with Error set) when it cannot be shown.</summary>
    public bool Prepare(string? path)
    {
        if (string.IsNullOrEmpty(path)) { Error = "No background image is set."; return false; }
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(path); }
        catch (Exception ex) { Error = "The background image could not be accessed: " + ex.Message; return false; }
        if (_loaded == path && _stamp == stamp && _tex != 0) { Error = null; return true; }
        try
        {
            if (!File.Exists(path)) { Error = "The background image file is missing."; return false; }
            using var bmp = SKBitmap.Decode(path) ?? throw new InvalidDataException("The file is not a supported image.");
            SKBitmap src = bmp;
            const int max = 4096;
            SKBitmap? scaled = null;
            if (bmp.Width > max || bmp.Height > max)
            {
                float k = Math.Min((float)max / bmp.Width, (float)max / bmp.Height);
                scaled = bmp.Resize(new SKImageInfo((int)(bmp.Width * k), (int)(bmp.Height * k), SKColorType.Rgba8888, SKAlphaType.Premul), SKSamplingOptions.Default);
                src = scaled ?? bmp;
            }
            using var rgba = src.Copy(SKColorType.Rgba8888) ?? throw new InvalidDataException("The image could not be converted.");
            if (_tex == 0) _tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _tex);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, rgba.Width, rgba.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba.GetPixels());
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            _aspect = (float)rgba.Width / rgba.Height; _loaded = path; _stamp = stamp; Error = null;
            scaled?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Error = "The background image could not be loaded: " + ex.Message; _loaded = null; return false;
        }
    }

    public void Draw(float screenAspect, float zoom, float dim)
    {
        _shader.Use();
        GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, _tex);
        _shader.Set("uTex", 0); _shader.Set("uScreenAspect", screenAspect); _shader.Set("uImageAspect", _aspect);
        _shader.Set("uZoom", zoom); _shader.Set("uDim", Math.Clamp(dim, 0f, 1f));
        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    public void Dispose() { if (_tex != 0) GL.DeleteTexture(_tex); GL.DeleteVertexArray(_vao); _shader.Dispose(); }
}
