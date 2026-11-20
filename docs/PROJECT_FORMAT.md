# Audio Dimension project format (version 1)

A project is a single file with the extension **`.adproj`**. It is an ordinary **zip archive**, so it can be copied, emailed or backed up like any file, and opened with any zip tool for inspection.

```
MyShow.adproj  (zip)
├── project.json              the project description (UTF-8 JSON, camelCase)
└── assets/
    ├── a3f9c1d2e7b4.png      embedded background picture
    └── a81c0d55f2e9.wav      embedded recording (optional)
```

Nothing in a project is an absolute path to a file on your computer. Pictures and recordings are **copied into the package** and referenced by asset id, so a project keeps working when the original files move or are deleted, and can be opened on another PC.

## project.json

| Field | Meaning |
| --- | --- |
| `formatVersion` | `1`. Newer versions are refused with a clear message instead of being half-read. |
| `id`, `name`, `description` | identity and display name (name: 1-100 printable characters) |
| `createdUtc`, `modifiedUtc`, `appVersion` | bookkeeping |
| `visual` | the whole look, see below |
| `textLayers` | the overlay text layers (same schema as title templates, see `PRESET_SCHEMA.md` and the text editor) including animations |
| `audio` | audio source and analysis settings |
| `recording` | export choices: canvas size, frame rate, format, quality... |
| `timeline` | `textLoopSeconds` for the text-animation preview loop |
| `assets` | manifest of embedded files |

### visual
`visualizerId`, `presetId` / `presetName` (where the look started from - informational), `params`, `palette`, `camera`, `background`, `effects`, `aspectName`.
The configuration objects are stored **in full**, so a project never depends on a preset still existing or being unchanged. Value ranges are the same as for presets (see `PRESET_SCHEMA.md`).
`background.mode` may be `Image`; `background.assetId` then names an entry in `assets` (presets cannot use images).

### audio
`source` (`Microphone` or `SystemAudio`), `deviceId` and `deviceName`, `inputGain`, `noiseGateDb` and `analysis` (sensitivity, smoothing, bass/mid/treble, frequency tilt, auto gain, peak detection, enabled bands).
Device ids differ between computers. On open the id is tried first, then the name, then the PC's default device is used and a note explains what happened. **Opening a project never starts capturing audio.**

### recording
`format` (`Mp4H264`, `Mp4Hevc`, `WebM`), `width`, `height` (128-7680 x 128-4320), `fps` (1-120), `quality` (1-100), `encoder`, `includeAudio`, `autoRender`, `filePrefix`. The output folder is deliberately not stored (it is a per-PC setting).

### assets
Each entry: `id` (4-40 chars `a-z 0-9 -`), `kind` (`Image` or `Audio`), `path` (always `assets/<name>`), `fileName` (original name, informational), `sha256`, `bytes`.

## Opening is defensive
* Only `project.json` and files under `assets/` are accepted; absolute paths, `..`, backslashes and other entries reject the whole file (no "zip-slip").
* Limits: at most 100 entries, 4 MB for `project.json`, 3 GB declared size, 40 assets.
* Every value is range-checked; unknown JSON members are ignored; nothing in a project is executed.
* A runtime-only `background.imagePath` is never written and is ignored if a hand-edited file contains one.
* Each asset's size and SHA-256 are verified. A **missing or damaged asset does not stop the project from opening**: it is skipped with a warning and the dependent feature falls back (for example the default gradient replaces a missing background picture).

## Saving is atomic
The package is written to `<name>.adproj.saving` and moved over the old file only when complete, so a failed save (disk full, locked file, invalid data) never damages an existing project.

## Where projects live
Default folder: `%LOCALAPPDATA%\AudioDimension\Projects` (changeable in Settings). Recent projects and a catalogue of saved projects are kept in the local SQLite database (`recent_projects`, `projects`).
