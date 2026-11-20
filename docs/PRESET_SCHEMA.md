# Audio Dimension preset schema (version 1)

A preset is **plain data**. It selects a rendering implementation (a "visualizer") by id and supplies configuration for it.
Presets never contain code; the importer only reads the typed fields below, ignores anything else, bounds every number and
rejects the whole file if anything is invalid. Nothing in a preset file is ever executed or loaded as a module.

## Separation of concerns

| Concern | Where it lives |
| --- | --- |
| Preset metadata (name, category, description, tags, author) | `PresetDefinition` fields |
| Rendering implementation | C# `Visualizer` classes registered in `VisualizerRegistry`; referenced by `visualizerId` |
| Default configuration (look + audio response) | `visual` and `audio` sections |
| Custom configuration (user presets) | the same schema, stored in SQLite (`presets` table) |
| Preview image | built-ins: embedded `Assets/Thumbnails/<id>.png`; custom: `Presets/Thumbs/<id>.png` |

Quality (render scale, particle budget) is intentionally **not** part of a preset: it belongs to the machine, not the look.

## File format

Files use the extension `.adpreset` (JSON). Two shapes are accepted:

```jsonc
// 1. A pack (what Export writes)
{ "format": "AudioDimension.Presets", "schemaVersion": 1, "presets": [ { ...preset... }, ... ] }

// 2. A single bare preset object
{ "schemaVersion": 1, "name": "...", ... }
```

Limits: at most 512 KB per file, 200 presets per pack, JSON nesting depth 16.

## Preset object

| Field | Type | Rules |
| --- | --- | --- |
| `schemaVersion` | int | must be `1` |
| `id` | string | assigned on import/save (`custom-xxxxxxxxxx`); built-ins use a slug of the name |
| `name` | string | 1-80 printable characters |
| `category` | string | 1-40 printable characters. Built-in categories: *Neon & Cyberpunk, Space & Cosmos, Geometric, Particles, Waveforms, Minimal & Elegant, Experimental*; custom presets default to *Custom* |
| `description` | string | up to 600 characters |
| `author` | string | up to 80 characters |
| `tags` | string[] | up to 16 entries of up to 32 characters |
| `visualizerId` | string | must exist in the registry (e.g. `circular-spectrum`, `cyber-grid`, `fractal`) |
| `customizable` | string[] | subset of: `speed reactivity scale lineWidth glow particleDensity rotation elements variant mirror palette camera background effects sensitivity frequencyResponse` |
| `visual` | object | see below |
| `audio` | object | see below |

### `visual`

* `params`: `speed` 0-4, `reactivity` 0-3, `scale` 0.2-3, `lineWidth` 0.5-16, `particleDensity` 0.05-2, `glow` 0-4,
  `segments` 0-128 (0 = visualizer default), `variant` 0-3, `mirror` bool, `rotation` -360..360
* `palette.colors`: 2-8 colours, each `#RRGGBB`
* `camera`: `yaw` ±360, `pitch` ±90, `distance` 0.5-80, `fov` 10-130, `autoRotateSpeed` ±180, `audioShake` 0-1, `zoom2D` 0.2-5, `orthographic` bool
* `background`: `mode` (`Solid`, `Gradient`, `AnimatedGradient`, `Starfield`, `ParticleField`, `Transparent`), `color1`, `color2` (`#RRGGBB`), `animationSpeed` 0-4, `audioReactive` 0-4
* `effects`: on/off flag plus amount for each of bloom, vignette, chromatic aberration, RGB separation, film grain, scanlines, colour grading
  (saturation, contrast, brightness, hue shift), distortion, blur, noise, motion blur and light rays. Amounts are range-checked.

### `audio`

`sensitivity` 0.05-8, `smoothing` 0-0.99, `bassSensitivity` / `midSensitivity` / `trebleSensitivity` 0-4, `frequencyTilt` -1..1,
`autoGain` bool, `peakDetection` bool, `bandEnabled` bool[6] (sub-bass, bass, low-mid, mid, high-mid, treble).

## Behaviour on import

1. The text is parsed with a depth-limited JSON reader; unknown members are ignored.
2. Every preset is validated against the rules above. **If any preset is invalid, nothing is imported** and the reasons are shown.
3. Valid presets are stored as new custom presets with fresh ids and de-duplicated names. Built-in presets can never be overwritten.

## Adding a new rendering implementation

Write a `Visualizer` subclass that emits geometry through `Canvas`, register it in `VisualizerRegistry`, then reference its id from
presets. No change to the renderer is required.
