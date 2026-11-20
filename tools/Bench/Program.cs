// Development tool: measures render cost of every visualizer (CPU+GPU frame time, managed allocations, geometry, GPU memory).
// Usage: Bench [width] [height] [frames] [qualityLevel]
using System.Diagnostics;
using AudioDimension.Audio.Analysis;
using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Gl;
using AudioDimension.Visuals.Render;
using AudioDimension.Visuals.Visualizers;
using OpenTK.Graphics.OpenGL4;

int W = args.Length > 0 ? int.Parse(args[0]) : 1920, H = args.Length > 1 ? int.Parse(args[1]) : 1080, N = args.Length > 2 ? int.Parse(args[2]) : 120;
var level = args.Length > 3 ? Enum.Parse<QualityLevel>(args[3]) : QualityLevel.High;

var frames = new List<AnalysisFrame>();
{
    const int rate = 48000; const double dt = 1.0 / 60;
    var a = new AudioAnalyzer(rate); a.Params.AutoGain = false;
    int per = (int)(rate * dt); var buf = new float[per]; long pos = 0;
    for (int t = 0; t < 240; t++)
    {
        bool kick = (t % 30) < 6;
        for (int i = 0; i < per; i++, pos++)
        {
            double s = 0.18 * Math.Sin(2 * Math.PI * 880 * pos / rate) + 0.10 * Math.Sin(2 * Math.PI * 5000 * pos / rate) + 0.12 * Math.Sin(2 * Math.PI * 220 * pos / rate)
                     + 0.07 * Math.Sin(2 * Math.PI * 1800 * pos / rate) + 0.05 * Math.Sin(2 * Math.PI * 340 * pos / rate);
            if (kick) s += 0.6 * Math.Sin(2 * Math.PI * 60 * pos / rate);
            buf[i] = (float)s;
        }
        a.Push(buf); frames.Add(a.Analyze(dt));
    }
}

using var ctx = new OffscreenContext();
using var r = new VisualRenderer();
using var target = new OffscreenTarget(W, H);
Console.WriteLine($"GPU {r.Gpu.Renderer}  VRAM {r.Gpu.VramMb} MB   {W}x{H}  quality {level}  frames {N}");
Console.WriteLine($"{"visualizer",-24}{"ms/frame",9}{"fps",8}{"KB alloc",10}{"verts",10}");

var rows = new List<(string id, double ms, double kb, int verts)>();
foreach (var viz in VisualizerRegistry.CreateAll())
{
    var cfg = new VisualConfig { Quality = QualitySettings.For(level) }; cfg.Camera.Reset(viz.DefaultCamera);
    viz.Reset(); r.ResetTime();
    for (int i = 0; i < 40; i++) r.Render(viz, cfg, frames[i % frames.Count], 1.0 / 60, target.Fbo, W, H);
    GL.Finish();
    GC.Collect(); GC.WaitForPendingFinalizers();
    long before = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < N; i++) { r.Render(viz, cfg, frames[(i + 40) % frames.Count], 1.0 / 60, target.Fbo, W, H); }
    GL.Finish();
    sw.Stop();
    double ms = sw.Elapsed.TotalMilliseconds / N, kb = (GC.GetAllocatedBytesForCurrentThread() - before) / 1024.0 / N;
    rows.Add((viz.Id, ms, kb, r.LastVertexCount));
}
foreach (var x in rows.OrderByDescending(x => x.ms))
    Console.WriteLine($"{x.id,-24}{x.ms,9:0.00}{1000 / x.ms,8:0}{x.kb,10:0.0}{x.verts,10:N0}");
Console.WriteLine($"mean {rows.Average(x => x.ms):0.00} ms, worst {rows.Max(x => x.ms):0.00} ms, total managed alloc/frame (sum) {rows.Sum(x => x.kb):0} KB");
