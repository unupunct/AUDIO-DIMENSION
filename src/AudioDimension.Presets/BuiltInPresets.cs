using AudioDimension.Visuals.Config;
using AudioDimension.Visuals.Visualizers;

namespace AudioDimension.Presets;

/// <summary>The 60 shipped presets. Each pairs a rendering implementation + variant with its own palette, camera, background, effects and audio tuning.</summary>
public static class BuiltInPresets
{
    private static readonly Lazy<IReadOnlyList<PresetDefinition>> Cache = new(Build);
    public static IReadOnlyList<PresetDefinition> All => Cache.Value;

    public static string Slug(string name) =>
        new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Replace("--", "-").Trim('-');

    private sealed class B
    {
        public readonly PresetDefinition P;
        public B(string name, string cat, string viz, string desc)
        {
            var v = VisualizerRegistry.Create(viz);
            P = new PresetDefinition
            {
                Id = Slug(name), Name = name, Category = cat, Description = desc, VisualizerId = viz, Author = "Audio Dimension",
                Tags = new[] { cat.ToLowerInvariant(), v.Name.ToLowerInvariant() }
            };
            P.Visual.Camera.Reset(v.DefaultCamera);
            P.Visual.Effects.Vignette = true;
            Customize(v);
        }

        private void Customize(Visualizer v)
        {
            var k = new List<string> { "speed", "reactivity", "scale", "glow", "palette", "background", "effects", "sensitivity", "frequencyResponse", "variant" };
            string id = v.Id;
            if (id is not ("plasma" or "fractal" or "fluid-field")) { k.Add("lineWidth"); k.Add("elements"); }
            if (id is "particle-explosion" or "particle-galaxy" or "particle-tunnel" or "reactive-sphere" or "starfield" or "vortex" or "fireflies" or "solar-flare" or "space-environment")
                k.Add("particleDensity");
            if (v.Is3D) k.Add("camera");
            if (id is "classic-spectrum" or "neon-waveform" or "liquid-spectrum") k.Add("mirror");
            if (id is "circular-spectrum" or "particle-galaxy" or "reactive-sphere" or "circular-oscilloscope") k.Add("rotation");
            P.Customizable = k.Distinct().ToArray();
        }

        public B Pal(params string[] c) { P.Visual.Palette.Colors = c; return this; }
        public B Var(int variant, int segments = 0) { P.Visual.Params.Variant = variant; P.Visual.Params.Segments = segments; return this; }
        public B Anim(double speed = 1, double react = 1, double scale = 1, double line = 3, double glow = 1, double dens = 1, double rot = 0, bool mirror = false)
        {
            var p = P.Visual.Params; p.Speed = speed; p.Reactivity = react; p.Scale = scale; p.LineWidth = line; p.Glow = glow; p.ParticleDensity = dens; p.Rotation = rot; p.Mirror = mirror;
            return this;
        }
        public B Bg(BackgroundMode m, string c1, string c2, double anim = 0.3, double react = 0.5)
        {
            var b = P.Visual.Background; b.Mode = m; b.Color1 = c1; b.Color2 = c2; b.AnimationSpeed = anim; b.AudioReactive = react;
            return this;
        }
        public B Fx(double bloom = 0.9, double vignette = 0.45, Action<EffectSettings>? more = null)
        {
            var f = P.Visual.Effects; f.Bloom = bloom > 0; f.BloomAmount = bloom; f.Vignette = vignette > 0; f.VignetteAmount = vignette; more?.Invoke(f);
            return this;
        }
        public B Cam(Action<CameraSettings> a) { a(P.Visual.Camera); return this; }
        public B Aud(double sens = 1, double smooth = 0.6, double bass = 1, double mid = 1, double treble = 1, double tilt = 0, bool agc = true, bool peaks = true)
        {
            P.Audio = new PresetAudio { Sensitivity = sens, Smoothing = smooth, BassSensitivity = bass, MidSensitivity = mid, TrebleSensitivity = treble, FrequencyTilt = tilt, AutoGain = agc, PeakDetection = peaks };
            return this;
        }
        public PresetDefinition Done() => P;
    }

    private static B N(string name, string cat, string viz, string desc) => new(name, cat, viz, desc);
    private const BackgroundMode Grad = BackgroundMode.Gradient, Solid = BackgroundMode.Solid, Anim = BackgroundMode.AnimatedGradient, Stars = BackgroundMode.Starfield, Field = BackgroundMode.ParticleField;

    private static IReadOnlyList<PresetDefinition> Build()
    {
        const string neon = PresetCategories.Neon, space = PresetCategories.Space, geo = PresetCategories.Geometric, par = PresetCategories.Particles,
            wav = PresetCategories.Waveforms, min = PresetCategories.Minimal, exp = PresetCategories.Experimental;
        var l = new List<PresetDefinition>
        {
            // ---- NEON & CYBERPUNK
            N("Neon Pulse", neon, "circular-spectrum", "A ring of neon bars that kicks on every bass hit.").Pal("#FF2BD6", "#00F0FF", "#7C4DFF", "#FFEE58").Var(0, 96).Anim(1, 1.2, 1, 3.5, 1.3)
                .Bg(Grad, "#07030F", "#1B0630").Fx(1.3, 0.5, f => { f.ChromaticAberration = true; f.ChromaticAmount = 0.008; }).Aud(1.1, 0.5, 1.3).Done(),
            N("Cyber Grid", neon, "cyber-grid", "A neon grid racing toward a striped sun; bass swells the sun.").Pal("#00F0FF", "#FF2BD6", "#7C4DFF", "#FFD54F").Var(0)
                .Bg(Grad, "#05010F", "#240A3C").Fx(1.2, 0.5, f => { f.Scanlines = true; f.ScanlineAmount = 0.15; }).Aud(1, 0.5, 1.2).Done(),
            N("Digital Storm", neon, "digital-rain", "A diagonal storm of data streams with lightning bolts on the beat.").Pal("#00FF9C", "#00B8FF", "#B388FF", "#FFFFFF").Var(1, 90).Anim(1.3, 1.1, 1, 3, 1.7)
                .Bg(Solid, "#02060A", "#02060A").Fx(1.1, 0.5, f => { f.ChromaticAberration = true; f.RgbSeparation = true; f.Noise = true; f.NoiseAmount = 0.04; }).Aud(1.1, 0.4, 1, 1, 1.3).Done(),
            N("Synthwave Tunnel", neon, "geometric-tunnel", "Square frames rush past in a retro sunset tunnel.").Pal("#FF3CAC", "#784BA0", "#2B86C5", "#FFD166").Var(1, 4).Anim(1, 1, 1, 3.5, 1.2)
                .Bg(Anim, "#12002B", "#3A0CA3", 0.4).Fx(1.4, 0.55, f => { f.Scanlines = true; f.ScanlineAmount = 0.25; }).Aud(1, 0.5, 1.3).Done(),
            N("Electric Horizon", neon, "cyber-grid", "Neon terrain rises on both sides of the road, shaped by the spectrum.").Pal("#FFB703", "#FB5607", "#FF006E", "#8338EC").Var(1)
                .Bg(Grad, "#12002B", "#3A0CA3").Fx(1.2, 0.5, f => { f.LightRays = true; f.LightRaysAmount = 0.5; }).Cam(c => c.Pitch = 12).Aud(1, 0.55, 1.1, 1, 1.2).Done(),
            N("Neon Reactor", neon, "reactive-rings", "Six band rings spin around a core and shockwaves fire on every beat.").Pal("#00FFC6", "#00A3FF", "#FF2E88", "#FFE600").Var(0).Anim(1, 1.1, 1, 3.2, 1.3)
                .Bg(Solid, "#04040A", "#04040A").Fx(1.4, 0.5, f => { f.ChromaticAberration = true; f.ChromaticAmount = 0.01; }).Aud(1.1, 0.45, 1.2).Done(),
            N("Cyber Rain", neon, "digital-rain", "Green-cyan code rain; each column falls faster when its frequency is active.").Pal("#00FF66", "#00D1FF", "#B0FFE0", "#FFFFFF").Var(0, 96).Anim(1, 1, 1, 2.5)
                .Bg(Grad, "#000A05", "#001F14").Fx(1.0, 0.55).Aud(1, 0.5, 1, 1, 1.2).Done(),
            N("Laser Geometry", neon, "rotating-geometry", "Razor-thin laser wireframes nested inside each other.").Pal("#FF0055", "#00FFEA", "#FFE600", "#7C4DFF").Var(1, 4).Anim(1.1, 1.2, 1, 3.5, 1.5)
                .Bg(Solid, "#03030A", "#03030A").Fx(1.6, 0.5, f => { f.LightRays = true; f.LightRaysAmount = 0.6; }).Aud(1.1, 0.4, 1.2, 1, 1.2).Done(),

            // ---- SPACE & COSMOS
            N("Cosmic Galaxy", space, "particle-galaxy", "A spiral galaxy; each radius glows with its own frequency.").Pal("#FFD9A0", "#FF8FB1", "#8F7CFF", "#3EC6FF").Var(0, 3).Anim(1, 1, 1, 3, 1, 1.2)
                .Bg(Stars, "#02030A", "#0A0620", 0.15).Fx(1.0, 0.5).Aud(1, 0.65, 1.2).Done(),
            N("Black Hole", space, "vortex", "Matter spirals into an event horizon; louder music speeds the infall.").Pal("#FFB347", "#FF6A3D", "#C0362C", "#3A0CA3").Var(0).Anim(1, 1.1, 1.1, 3, 1.1, 1.2)
                .Bg(Stars, "#010102", "#0A0414", 0.1).Fx(1.3, 0.6, f => { f.Distortion = true; f.DistortionAmount = 0.06; }).Cam(c => c.Pitch = 26).Aud(1, 0.6, 1.3).Done(),
            N("Solar Explosion", space, "solar-flare", "A star hurls plasma loops into space on each beat.").Pal("#FFF3B0", "#FFB703", "#FB5607", "#9D0208").Var(0).Anim(1, 1.2, 1, 3, 1.2, 1.3)
                .Bg(Grad, "#050000", "#1A0500").Fx(1.8, 0.6, f => { f.LightRays = true; f.LightRaysAmount = 0.8; f.MotionBlur = true; f.MotionBlurAmount = 0.5; }).Aud(1.1, 0.5, 1.4).Done(),
            N("Nebula Drift", space, "fluid-field", "Slow glowing nebula clouds that billow with the music.").Pal("#7B2FF7", "#F107A3", "#00C2FF", "#FFFFFF").Var(1).Anim(0.8, 1, 1, 3, 1)
                .Bg(Solid, "#000000", "#000000").Fx(1.0, 0.5).Aud(1, 0.75, 1.1, 1, 1).Done(),
            N("Starfield Journey", space, "starfield", "Cruise through a star field; volume is your throttle.").Pal("#BFE9FF", "#8FB8FF", "#C7A6FF", "#FFFFFF").Var(0).Anim(1, 1, 1, 3, 1, 1)
                .Bg(Grad, "#000308", "#050B22").Fx(0.9, 0.5).Aud(1, 0.5, 1.1, 1, 1.2).Done(),
            N("Quantum Particles", space, "reactive-sphere", "A shimmering shell of particles that ripples with every band.").Pal("#00E5FF", "#7C4DFF", "#FF4081", "#B2FF59").Var(1).Anim(1, 1.1, 1, 3, 1.1, 1.2)
                .Bg(Grad, "#02020A", "#150A30").Fx(1.2, 0.5, f => { f.ChromaticAberration = true; f.ChromaticAmount = 0.006; }).Aud(1, 0.55, 1.2, 1, 1.2).Done(),
            N("Cosmic Rings", space, "space-environment", "A planet with a sparkling ring in a living nebula sky.").Pal("#7DF9FF", "#B388FF", "#FF8FAB", "#FFE0A3").Var(0).Anim(1, 1, 1, 3, 1.1)
                .Bg(Solid, "#000000", "#000000").Fx(1.1, 0.55).Aud(1, 0.65, 1.2).Done(),
            N("Deep Space", space, "particle-tunnel", "A calm flight down an endless tube of cold blue particles.").Pal("#1E90FF", "#6A5ACD", "#00CED1", "#E0FFFF").Var(0).Anim(0.8, 0.9, 1, 3, 1.8, 1.4)
                .Bg(Grad, "#00020A", "#02133A").Fx(1.0, 0.6).Aud(0.9, 0.7, 1.1, 1, 1).Done(),

            // ---- GEOMETRIC
            N("Crystal Sphere", geo, "crystal-core", "A faceted crystal whose spikes grow with the spectrum.").Pal("#7DF9FF", "#B388FF", "#FF9AD5", "#FFFFFF").Var(0).Anim(1, 1.1, 1, 3, 1)
                .Bg(Grad, "#05050F", "#1A1038").Fx(0.9, 0.5).Aud(1, 0.6, 1.2).Done(),
            N("Fractal Motion", geo, "fractal", "A Julia set that morphs as bass bends its constant.").Pal("#00F5D4", "#00BBF9", "#9B5DE5", "#F15BB5").Var(0).Anim(1, 1, 1, 3, 1)
                .Bg(Solid, "#000000", "#000000").Fx(0.8, 0.4).Aud(1, 0.65, 1.3).Done(),
            N("Geometric Collapse", geo, "cube-field", "Cubes implode toward the centre then burst outward on every beat.").Pal("#FF6B6B", "#FFD93D", "#6BCB77", "#4D96FF").Var(1, 5).Anim(1, 1.2, 1, 3, 1)
                .Bg(Grad, "#06060D", "#141428").Fx(0.9, 0.5).Aud(1.1, 0.45, 1.4).Done(),
            N("Infinite Tunnel", geo, "geometric-tunnel", "Hexagonal frames carry pulses toward you, forever.").Pal("#00E5FF", "#2979FF", "#651FFF", "#F50057").Var(0, 6).Anim(1, 1, 1, 3, 1.1)
                .Bg(Solid, "#02030A", "#02030A").Fx(1.2, 0.55).Aud(1, 0.55, 1.2).Done(),
            N("Rotating Cubes", geo, "cube-field", "A lattice of spinning cubes sized by the spectrum.").Pal("#F72585", "#7209B7", "#3A0CA3", "#4CC9F0").Var(0, 5).Anim(1, 1, 1, 3, 1)
                .Bg(Grad, "#05030D", "#1B0B30").Fx(0.9, 0.5).Aud(1, 0.6).Done(),
            N("Sacred Geometry", geo, "sacred-geometry", "The flower of life, glowing gold, breathing with the bands.").Pal("#FFD700", "#FFB300", "#FFF3C4", "#FF8F00").Var(0).Anim(1, 1, 1, 3, 1.2)
                .Bg(Grad, "#0A0603", "#1F1305").Fx(1.2, 0.55).Aud(1, 0.7, 1.1).Done(),
            N("Morphing Polyhedrons", geo, "rotating-geometry", "Cube, octahedron and icosahedron shells swelling with different bands.").Pal("#84FFFF", "#B388FF", "#FF80AB", "#FFFF8D").Var(0, 5).Anim(1, 1.1, 1, 3, 1.1)
                .Bg(Grad, "#04040C", "#12122A").Fx(1.1, 0.5).Aud(1, 0.6, 1.2).Done(),
            N("Abstract Dimensions", geo, "sacred-geometry", "Counter-rotating star polygons that stretch with every band.").Pal("#00BCD4", "#E040FB", "#FFEB3B", "#69F0AE").Var(2).Anim(1, 1.1, 1, 2.8, 1.2)
                .Bg(Anim, "#05020F", "#101A3A", 0.3).Fx(1.2, 0.5).Aud(1, 0.55).Done(),

            // ---- PARTICLES
            N("Particle Universe", par, "fireflies", "Thousands of points orbit a hidden centre like a tiny universe.").Pal("#A0C4FF", "#BDB2FF", "#FFC6FF", "#FDFFB6").Var(3).Anim(1, 1, 1, 3, 1.6, 1.4)
                .Bg(Grad, "#02030C", "#0A0F2A").Cam(c => { c.Pitch = 30; c.Distance = 9; }).Fx(1.0, 0.5).Aud(1, 0.65).Done(),
            N("Fireflies", par, "fireflies", "Warm lights wander through a dark forest and blink with the music.").Pal("#FFF59D", "#FFD54F", "#B9F6CA", "#FFAB40").Var(0).Anim(1, 1, 1, 3, 1.7)
                .Bg(Grad, "#020A05", "#0B2B14").Fx(1.2, 0.6).Aud(0.9, 0.7, 1, 1.1, 1.2).Done(),
            N("Particle Storm", par, "particle-explosion", "Beats blast a flat shockwave of particles outward.").Pal("#FF5252", "#FF9100", "#FFEA00", "#FFFFFF").Var(2).Anim(1.2, 1.3, 1, 3, 1.1, 1.4)
                .Bg(Solid, "#050202", "#050202").Fx(1.4, 0.55).Aud(1.2, 0.4, 1.4).Done(),
            N("Magnetic Particles", par, "vortex", "Particles stream along the field lines of a magnet.").Pal("#40C4FF", "#00E676", "#EA80FC", "#FFFFFF").Var(1).Anim(1, 1, 1, 3, 1, 1)
                .Bg(Solid, "#02050A", "#02050A").Fx(1.1, 0.5).Aud(1, 0.6, 1.1, 1.1, 1.1).Done(),
            N("Energy Explosion", par, "particle-explosion", "Spherical bursts of colour detonate on every kick.").Pal("#00E5FF", "#76FF03", "#FFEA00", "#FF1744").Var(0).Anim(1, 1.2, 1, 3, 1.2, 1.2)
                .Bg(Solid, "#030308", "#030308").Fx(1.5, 0.5).Aud(1.1, 0.45, 1.4).Done(),
            N("Particle Rain", par, "digital-rain", "Soft glowing drops fall faster in the active frequencies.").Pal("#80D8FF", "#40C4FF", "#B3E5FC", "#FFFFFF").Var(2, 128).Anim(1, 1, 1, 3, 1.7)
                .Bg(Grad, "#020813", "#0C2540").Fx(1.0, 0.55).Aud(1, 0.6, 1, 1, 1.2).Done(),
            N("Floating Dust", par, "fireflies", "Fine dust motes drift in warm light and brighten with volume.").Pal("#FFE0B2", "#FFCC80", "#FFFFFF", "#FFAB91").Var(1).Anim(0.8, 0.9, 1, 3, 1.5, 1.2)
                .Bg(Grad, "#0E0905", "#2A1A0C").Fx(0.8, 0.6).Aud(0.9, 0.8).Done(),
            N("Particle Vortex", par, "vortex", "A glowing tornado of particles climbing into the dark.").Pal("#E040FB", "#7C4DFF", "#448AFF", "#18FFFF").Var(2).Anim(1.1, 1.1, 1, 3, 1.1, 1.1)
                .Bg(Grad, "#04020C", "#150A30").Cam(c => { c.Pitch = 12; c.Distance = 11; }).Fx(1.2, 0.5).Aud(1, 0.55, 1.2).Done(),

            // ---- WAVEFORMS
            N("Classic Wave", wav, "neon-waveform", "A clean single waveform line.").Pal("#4CAF50", "#8BC34A", "#CDDC39", "#FFFFFF").Var(0).Anim(1, 1, 1, 2, 0.9)
                .Bg(Solid, "#050805", "#050805").Fx(0.5, 0.4).Aud(1, 0.5).Done(),
            N("Neon Wave", wav, "neon-waveform", "A thick mirrored waveform glowing in magenta and cyan.").Pal("#FF00CC", "#7B2FF7", "#00E5FF", "#FFFFFF").Var(0).Anim(1, 1.1, 1, 4, 1.3, 1, 0, true)
                .Bg(Grad, "#06020F", "#1A0830").Fx(1.5, 0.5).Aud(1.1, 0.45).Done(),
            N("Circular Wave", wav, "circular-oscilloscope", "The waveform bent into a spinning ring.").Pal("#00F5FF", "#FF2BD6", "#FFD60A", "#FFFFFF").Var(0).Anim(1, 1.1, 1, 3, 1.2, 1, 10)
                .Bg(Grad, "#030309", "#101030").Fx(1.2, 0.5).Aud(1.1, 0.5).Done(),
            N("Wave Mountain", wav, "frequency-mountains", "A rolling mountain range built from recent spectra.").Pal("#2E8B57", "#00C9A7", "#92E3A9", "#E0FFFF").Var(0).Anim(1, 1.1, 1, 2.5, 1)
                .Bg(Grad, "#020A0A", "#0A2B2B").Fx(0.9, 0.55).Aud(1, 0.6, 1.2).Done(),
            N("Liquid Spectrum", wav, "liquid-spectrum", "A silky filled spectrum curve with a watery reflection.").Pal("#00C6FF", "#0072FF", "#7F00FF", "#E100FF").Var(0).Anim(1, 1, 1, 3, 1.1)
                .Bg(Grad, "#03030F", "#0A1040").Fx(1.1, 0.5).Aud(1, 0.65).Done(),
            N("Frequency Ocean", wav, "frequency-ocean", "A lit ocean where each band raises its own swell; beats make ripples.").Pal("#0A6EBD", "#2FA4E7", "#7FD1FF", "#E0F7FF").Var(0).Anim(1, 1.1, 1, 3, 1)
                .Bg(Grad, "#020812", "#0A2540").Fx(0.9, 0.55).Aud(1, 0.65, 1.3).Done(),
            N("Audio Ribbon", wav, "audio-ribbon", "A pastel ribbon woven from the last second of waveform.").Pal("#FF9A9E", "#FAD0C4", "#A18CD1", "#FBC2EB").Var(0).Anim(1, 1.1, 1, 3, 1)
                .Bg(Grad, "#0B0612", "#241838").Fx(1.0, 0.5).Aud(1.1, 0.5).Done(),
            N("Waveform Tunnel", wav, "audio-ribbon", "Fly inside a tunnel whose walls are the waveform.").Pal("#43E97B", "#38F9D7", "#4FACFE", "#F093FB").Var(1).Anim(1, 1.1, 1, 3, 1.1)
                .Bg(Solid, "#02060A", "#02060A").Cam(c => { c.Distance = 1.5; c.Pitch = 0; c.Yaw = 0; c.AutoRotateSpeed = 0; c.Fov = 80; }).Fx(1.2, 0.55).Aud(1.1, 0.5).Done(),

            // ---- MINIMAL & ELEGANT
            N("Minimal Spectrum", min, "classic-spectrum", "A few quiet white bars. Nothing else.").Pal("#FFFFFF", "#E0E0E0", "#BDBDBD", "#FFFFFF").Var(0, 24).Anim(1, 1, 1, 2, 0.8)
                .Bg(Solid, "#0B0B0D", "#0B0B0D").Fx(0.3, 0.3).Aud(1, 0.7, 1, 1, 1, 0).Done(),
            N("Soft Pulse", min, "circular-spectrum", "A gentle pastel ring that breathes inward.").Pal("#FFD6E0", "#E0BBE4", "#BDE0FE", "#FFFFFF").Var(1, 48).Anim(0.6, 0.8, 1.6, 2.5, 0.9)
                .Bg(Grad, "#0D0D14", "#1B1B2A").Fx(0.6, 0.4).Aud(0.9, 0.85, 1).Done(),
            N("Ambient Rings", min, "reactive-rings", "Slow concentric rings, no flashes, just calm motion.").Pal("#A8DADC", "#BDB2FF", "#F1FAEE", "#FFC8DD").Var(1).Anim(0.7, 0.8, 1, 2.5, 0.9)
                .Bg(Grad, "#0A0D12", "#16202B").Fx(0.6, 0.45).Aud(0.9, 0.85).Done(),
            N("Minimal Lines", min, "energy-waves", "Five hairline waves in monochrome.").Pal("#FFFFFF", "#CFD8DC", "#90A4AE", "#FFFFFF").Var(1, 5).Anim(0.8, 0.9, 1, 1.5, 0.9)
                .Bg(Solid, "#0A0A0C", "#0A0A0C").Fx(0.4, 0.35).Aud(0.9, 0.8).Done(),
            N("Floating Orbs", min, "fireflies", "Large soft bokeh orbs that swell with the bass.").Pal("#FFADAD", "#FFD6A5", "#CAFFBF", "#9BF6FF").Var(2).Anim(0.8, 0.9, 1, 3, 0.9)
                .Bg(Grad, "#0C0C12", "#1D1D2B").Fx(0.8, 0.45).Aud(0.9, 0.85, 1.2).Done(),
            N("Smooth Equalizer", min, "neon-equalizer", "Soft pastel LED columns.").Pal("#FF9AA2", "#FFDAC1", "#B5EAD7", "#C7CEEA").Var(1, 24).Anim(1, 0.9, 1, 3, 0.9)
                .Bg(Grad, "#0D0D12", "#1C1C26").Fx(0.6, 0.4).Aud(0.95, 0.75).Done(),
            N("Glass Spectrum", min, "liquid-spectrum", "Translucent glass bars with a faint reflection.").Pal("#D0F4FF", "#A0E7FF", "#B8C0FF", "#FFFFFF").Var(1, 32).Anim(1, 1, 1, 2.5, 0.9)
                .Bg(Grad, "#101820", "#1B2A3A").Fx(0.7, 0.45).Aud(1, 0.7).Done(),
            N("Elegant Frequencies", min, "liquid-spectrum", "A mirrored golden line with delicate peak markers.").Pal("#F5E6CA", "#E8C547", "#FFFFFF", "#D4AF37").Var(2).Anim(0.9, 1, 1, 2, 0.9)
                .Bg(Solid, "#0A0A0C", "#0A0A0C").Fx(0.6, 0.5).Aud(1, 0.75).Done(),

            // ---- EXPERIMENTAL
            N("Digital DNA", exp, "dna-helix", "A double helix that twists faster with energy.").Pal("#00FFC6", "#00A3FF", "#FF2E88", "#FFE600").Var(0).Anim(1, 1.1, 1, 3, 1.2)
                .Bg(Grad, "#02060C", "#08182E").Fx(1.2, 0.55).Aud(1, 0.55, 1.2).Done(),
            N("Energy Core", exp, "crystal-core", "A white-hot core wrapped in long crystal shards.").Pal("#FFEA00", "#FF6D00", "#FF1744", "#FFFFFF").Var(1).Anim(1, 1.2, 1, 3, 1.3)
                .Bg(Solid, "#050202", "#050202").Fx(1.6, 0.6, f => { f.LightRays = true; f.LightRaysAmount = 0.5; }).Aud(1.1, 0.45, 1.4).Done(),
            N("Magnetic Field", exp, "electric-web", "Dipole field lines with energy flowing along them.").Pal("#00E5FF", "#2979FF", "#D500F9", "#FFFFFF").Var(1).Anim(1, 1, 1, 3, 1.1)
                .Bg(Solid, "#02030A", "#02030A").Fx(1.2, 0.5).Aud(1, 0.6, 1.2).Done(),
            N("Fluid Universe", exp, "fluid-field", "Warped liquid colour that swirls with the bands.").Pal("#00C9FF", "#92FE9D", "#F7971E", "#FF4E50").Var(0).Anim(1, 1, 1, 3, 1)
                .Bg(Solid, "#000000", "#000000").Fx(0.8, 0.5).Aud(1, 0.7, 1.2).Done(),
            N("Plasma World", exp, "plasma", "A glowing plasma planet-sea with a drifting second sun.").Pal("#12C2E9", "#C471ED", "#F64F59", "#FFD200").Var(1).Anim(1, 1, 1, 3, 1)
                .Bg(Solid, "#000000", "#000000").Fx(0.9, 0.5).Aud(1, 0.65, 1.2).Done(),
            N("Dimensional Portal", exp, "fractal", "A log-polar fractal wormhole that opens on the bass.").Pal("#00F5FF", "#7C4DFF", "#FF2BD6", "#FFFFFF").Var(2).Anim(1, 1.1, 1, 3, 1.1)
                .Bg(Solid, "#000000", "#000000").Fx(1.1, 0.55, f => { f.ChromaticAberration = true; f.ChromaticAmount = 0.008; }).Aud(1, 0.55, 1.3).Done(),
            N("Electric Web", exp, "electric-web", "Glowing nodes link into a web when the music gets loud.").Pal("#B9F3FC", "#7B9CFF", "#E4B1FF", "#FFFFFF").Var(0).Anim(1, 1.1, 1, 3, 1.1)
                .Bg(Grad, "#02030C", "#0C1030").Fx(1.2, 0.5).Aud(1, 0.5, 1.2, 1, 1.2).Done(),
            N("Organic Geometry", exp, "fluid-field", "Living cells whose walls pulse with the bands.").Pal("#B5EAD7", "#FF9AA2", "#C7CEEA", "#FFDAC1").Var(2).Anim(1, 1, 1, 3, 1)
                .Bg(Solid, "#050505", "#050505").Fx(0.8, 0.5).Aud(1, 0.7, 1.1, 1.2, 1).Done(),
            N("Hyperdrive", exp, "starfield", "Stars stretch into light streaks as the jump engages.").Pal("#9BF6FF", "#A0C4FF", "#BDB2FF", "#FFFFFF").Var(1).Anim(1.2, 1.2, 1, 3, 1.2)
                .Bg(Solid, "#000208", "#000208").Fx(1.3, 0.6, f => { f.ChromaticAberration = true; f.ChromaticAmount = 0.012; f.MotionBlur = true; f.MotionBlurAmount = 0.45; }).Aud(1.1, 0.4, 1.3).Done(),
            N("Holographic Core", exp, "crystal-core", "A wireframe hologram scanned by a moving ring.").Pal("#00FFFF", "#00B7FF", "#8A7DFF", "#FFFFFF").Var(2).Anim(1, 1.1, 1, 2.5, 1.2)
                .Bg(Grad, "#01060C", "#04152A").Fx(1.3, 0.55, f => { f.Scanlines = true; f.ScanlineAmount = 0.3; f.ChromaticAberration = true; f.ChromaticAmount = 0.006; }).Aud(1, 0.55).Done(),
            N("Fractal Universe", exp, "fractal", "A kaleidoscopic fractal that folds in on itself.").Pal("#FDEB71", "#F8D800", "#FF6B6B", "#6A11CB").Var(1).Anim(1, 1, 1, 3, 1)
                .Bg(Solid, "#000000", "#000000").Fx(0.8, 0.45).Aud(1, 0.65, 1.2, 1.2).Done(),
            N("Dimension Shift", exp, "plasma", "Polar-warped plasma with a warped, shifting lens.").Pal("#FC466B", "#3F5EFB", "#00F2FE", "#FFFFFF").Var(2).Anim(1.1, 1.1, 1, 3, 1)
                .Bg(Solid, "#000000", "#000000").Fx(1.0, 0.5, f => { f.Distortion = true; f.DistortionAmount = 0.15; f.ChromaticAberration = true; f.ChromaticAmount = 0.012; }).Aud(1, 0.5, 1.3).Done(),
        };
        return l;
    }
}
