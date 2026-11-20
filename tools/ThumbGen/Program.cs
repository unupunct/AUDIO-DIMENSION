// Development tool: renders one preview thumbnail per built-in preset into the app's asset folder.
// The signal below is a synthetic test tone used ONLY to produce static artwork at build time; the shipped app never fabricates audio.
using AudioDimension.Audio.Analysis;
using AudioDimension.Presets;
using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Gl;
using AudioDimension.Visuals.Render;
using AudioDimension.Visuals.Visualizers;

string outDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "thumbs");
Directory.CreateDirectory(outDir);
const int W = 640, H = 360, TW = 320, TH = 180;

// Music-like demo signal: a 60 Hz kick every half second over steady mid/treble tones (analysed by the real analyzer).
var frames = new List<AnalysisFrame>();
{
    const int rate = 48000; const double dt = 1.0 / 60;
    var a = new AudioAnalyzer(rate); a.Params.AutoGain = false;
    int per = (int)(rate * dt); var buf = new float[per]; long pos = 0;
    for (int t = 0; t < 125; t++)
    {
        bool kick = (t % 30) < 6;
        for (int i = 0; i < per; i++, pos++)
        {
            double s = 0.18 * Math.Sin(2 * Math.PI * 880 * pos / rate) + 0.10 * Math.Sin(2 * Math.PI * 5000 * pos / rate)
                     + 0.12 * Math.Sin(2 * Math.PI * 220 * pos / rate) + 0.07 * Math.Sin(2 * Math.PI * 1800 * pos / rate) + 0.05 * Math.Sin(2 * Math.PI * 340 * pos / rate);
            if (kick) s += 0.6 * Math.Sin(2 * Math.PI * 60 * pos / rate);
            buf[i] = (float)s;
        }
        a.Push(buf); frames.Add(a.Analyze(dt));
    }
}

using var ctx = new OffscreenContext();
using var renderer = new VisualRenderer();
using var target = new OffscreenTarget(W, H);
Console.WriteLine($"GPU: {renderer.Gpu.Renderer}");

foreach (var preset in BuiltInPresets.All)
{
    var viz = VisualizerRegistry.Create(preset.VisualizerId);
    var cfg = new VisualConfig(); preset.ApplyTo(cfg);
    renderer.ResetTime();
    foreach (var f in frames) renderer.Render(viz, cfg, f, 1.0 / 60, target.Fbo, W, H);
    if (renderer.LastError is { } err) { Console.Error.WriteLine($"{preset.Name}: {err}"); return 1; }
    var px = target.ReadPixels();
    var small = new byte[TW * TH * 4];
    for (int y = 0; y < TH; y++)
        for (int x = 0; x < TW; x++)
            for (int c = 0; c < 4; c++)
            {
                int sum = px[((y * 2) * W + x * 2) * 4 + c] + px[((y * 2) * W + x * 2 + 1) * 4 + c] + px[((y * 2 + 1) * W + x * 2) * 4 + c] + px[((y * 2 + 1) * W + x * 2 + 1) * 4 + c];
                small[(y * TW + x) * 4 + c] = (byte)(sum / 4);
            }
    PngWriter.Save(Path.Combine(outDir, preset.Id + ".png"), small, TW, TH);
    Console.WriteLine($"  {preset.Id}.png");
}
Console.WriteLine($"Wrote {BuiltInPresets.All.Count} thumbnails to {outDir}");
return 0;
