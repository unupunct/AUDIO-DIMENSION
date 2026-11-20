using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace AudioDimension.Visuals.Gl;

public sealed class ShaderCompileException : Exception
{
    public ShaderCompileException(string name, string log) : base($"Shader '{name}' failed to compile/link: {log}") { }
}

/// <summary>Compiled GL program with cached uniform locations.</summary>
public sealed class GlShader : IDisposable
{
    public int Handle { get; private set; }
    public string Name { get; }
    private readonly Dictionary<string, int> _locs = new();

    public GlShader(string name, string vertex, string fragment)
    {
        Name = name;
        int v = Compile(ShaderType.VertexShader, vertex, name + ".vert");
        int f = Compile(ShaderType.FragmentShader, fragment, name + ".frag");
        Handle = GL.CreateProgram();
        GL.AttachShader(Handle, v); GL.AttachShader(Handle, f);
        GL.LinkProgram(Handle);
        GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int ok);
        string log = GL.GetProgramInfoLog(Handle);
        GL.DetachShader(Handle, v); GL.DetachShader(Handle, f);
        GL.DeleteShader(v); GL.DeleteShader(f);
        if (ok == 0) { GL.DeleteProgram(Handle); Handle = 0; throw new ShaderCompileException(name, log); }
    }

    private static int Compile(ShaderType type, string src, string name)
    {
        int s = GL.CreateShader(type);
        GL.ShaderSource(s, src);
        GL.CompileShader(s);
        GL.GetShader(s, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0)
        {
            string log = GL.GetShaderInfoLog(s);
            GL.DeleteShader(s);
            throw new ShaderCompileException(name, log);
        }
        return s;
    }

    public void Use() => GL.UseProgram(Handle);

    private int Loc(string n)
    {
        if (!_locs.TryGetValue(n, out int l)) { l = GL.GetUniformLocation(Handle, n); _locs[n] = l; }
        return l;
    }

    public void Set(string n, float v) { int l = Loc(n); if (l >= 0) GL.Uniform1(l, v); }
    public void Set(string n, int v) { int l = Loc(n); if (l >= 0) GL.Uniform1(l, v); }
    public void Set(string n, Vector2 v) { int l = Loc(n); if (l >= 0) GL.Uniform2(l, v.X, v.Y); }
    public void Set(string n, Vector3 v) { int l = Loc(n); if (l >= 0) GL.Uniform3(l, v.X, v.Y, v.Z); }
    public void Set(string n, Vector4 v) { int l = Loc(n); if (l >= 0) GL.Uniform4(l, v.X, v.Y, v.Z, v.W); }
    public void Set(string n, ref Matrix4 m) { int l = Loc(n); if (l >= 0) GL.UniformMatrix4(l, false, ref m); }
    public void Set(string n, float[] arr) { int l = Loc(n); if (l >= 0) GL.Uniform1(l, arr.Length, arr); }
    public void SetVec4Array(string n, Vector4[] arr)
    {
        int l = Loc(n);
        if (l < 0) return;
        var f = new float[arr.Length * 4];
        for (int i = 0; i < arr.Length; i++) { f[i * 4] = arr[i].X; f[i * 4 + 1] = arr[i].Y; f[i * 4 + 2] = arr[i].Z; f[i * 4 + 3] = arr[i].W; }
        GL.Uniform4(l, arr.Length, f);
    }

    public void Dispose()
    {
        if (Handle != 0) { GL.DeleteProgram(Handle); Handle = 0; }
    }
}
