namespace AudioDimension.Visuals.Visualizers;

/// <summary>Catalogue of rendering implementations. New visualizers are added here only; the renderer is untouched.</summary>
public static class VisualizerRegistry
{
    private static readonly Dictionary<string, Func<Visualizer>> Factories = new();

    static VisualizerRegistry()
    {
        Register(() => new ClassicSpectrum());
        Register(() => new CircularSpectrum());
        Register(() => new Bars3D());
        Register(() => new ParticleExplosion());
        Register(() => new ParticleGalaxy());
        Register(() => new ParticleTunnel());
        Register(() => new ReactiveSphere());
        Register(() => new NeonWaveform());
        Register(() => new WaveformLandscape3D());
        Register(() => new Oscilloscope());
        Register(() => new CircularOscilloscope());
        Register(() => new ReactiveRings());
        Register(() => new GeometricTunnel());
        Register(() => new RotatingGeometry());
        Register(() => new NeonEqualizer());
        Register(() => new Plasma());
        Register(() => new EnergyWaves());
        Register(() => new FrequencyMountains());
        Register(() => new Starfield());
        Register(() => new SpaceEnvironment());
        Register(() => new CyberGrid());
        Register(() => new DigitalRain());
        Register(() => new Vortex());
        Register(() => new Fireflies());
        Register(() => new LiquidSpectrum());
        Register(() => new AudioRibbon());
        Register(() => new FrequencyOcean());
        Register(() => new ElectricWeb());
        Register(() => new DnaHelix());
        Register(() => new CrystalCore());
        Register(() => new FractalViz());
        Register(() => new FluidField());
        Register(() => new SacredGeometry());
        Register(() => new SolarFlare());
        Register(() => new CubeField());
    }

    public static void Register(Func<Visualizer> factory) => Factories[factory().Id] = factory;

    public static IReadOnlyList<string> Ids => Factories.Keys.ToList();

    public static bool Exists(string id) => Factories.ContainsKey(id);

    /// <summary>Creates a fresh instance (each preview/export owns its own state).</summary>
    public static Visualizer Create(string id) =>
        Factories.TryGetValue(id, out var f) ? f() : throw new KeyNotFoundException($"Unknown visualizer '{id}'");

    public static IEnumerable<Visualizer> CreateAll() => Factories.Values.Select(f => f());
}
