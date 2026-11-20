using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace AudioDimension.Visuals.Gl;

/// <summary>Hidden OpenGL 3.3 core context for offline rendering (export, thumbnails, tests).</summary>
public sealed class OffscreenContext : IDisposable
{
    private readonly NativeWindow _window;

    public OffscreenContext()
    {
        // Export rendering runs on a worker thread; GLFW window creation works off the main thread on Windows.
        GLFWProvider.CheckForMainThread = false;
        var settings = new NativeWindowSettings
        {
            ClientSize = new OpenTK.Mathematics.Vector2i(64, 64),
            StartVisible = false,
            StartFocused = false,
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core,
            Flags = ContextFlags.ForwardCompatible,
            Title = "AudioDimension offscreen"
        };
        _window = new NativeWindow(settings);
        _window.MakeCurrent();
        GL.LoadBindings(new GLFWBindingsContext());
    }

    public void MakeCurrent() => _window.MakeCurrent();

    public void Dispose() => _window.Dispose();
}

/// <summary>RGBA8 colour framebuffer used as a render target and for pixel readback.</summary>
public sealed class OffscreenTarget : IDisposable
{
    public int Fbo { get; }
    public int Width { get; }
    public int Height { get; }
    private readonly int _tex;

    public OffscreenTarget(int width, int height)
    {
        Width = width; Height = height;
        _tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        Fbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _tex, 0);
        var st = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        if (st != FramebufferErrorCode.FramebufferComplete) throw new InvalidOperationException("Offscreen framebuffer incomplete: " + st);
    }

    /// <summary>Reads RGBA bytes into a caller-owned buffer, bottom row first (OpenGL order) - no allocation, used by video export.</summary>
    public void ReadPixelsInto(byte[] dst)
    {
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, Fbo);
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.ReadPixels(0, 0, Width, Height, PixelFormat.Rgba, PixelType.UnsignedByte, dst);
    }

    /// <summary>Reads RGBA bytes, top row first.</summary>
    public byte[] ReadPixels()
    {
        var data = new byte[Width * Height * 4];
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, Fbo);
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.ReadPixels(0, 0, Width, Height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        int stride = Width * 4;
        var row = new byte[stride];
        for (int y = 0; y < Height / 2; y++)
        {
            System.Buffer.BlockCopy(data, y * stride, row, 0, stride);
            System.Buffer.BlockCopy(data, (Height - 1 - y) * stride, data, y * stride, stride);
            System.Buffer.BlockCopy(row, 0, data, (Height - 1 - y) * stride, stride);
        }
        return data;
    }

    public void Dispose()
    {
        GL.DeleteFramebuffer(Fbo);
        GL.DeleteTexture(_tex);
    }
}
