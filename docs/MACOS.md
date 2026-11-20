# Audio Dimension for macOS

The Mac version is a separate, native app (Swift, SwiftUI, Metal, Core Audio, AVFoundation) in the [`mac/`](../mac) folder.
The Windows app is built on WPF and WASAPI, which do not exist on macOS, so the Mac app is a port of the idea and the data formats,
not of the code. It shares the **preset format** with the Windows app: the built-in presets are the same JSON, exported by
`tools/PresetDump`.

## Status: preview

| Area | Windows | macOS preview |
| --- | --- | --- |
| Visualizers | 35 | 14 |
| Built-in presets | 60 | 25 (the ones that use those 14 visualizers) |
| Live audio | microphone + system audio | microphone + any input device, including virtual drivers such as BlackHole |
| Effects | full chain | bloom, vignette, chromatic aberration, film grain, scanlines, colour grading, 4x MSAA |
| Text and titles | yes | **not yet** |
| Video export | FFmpeg (MP4/WebM, H.264/HEVC/VP9) | AVFoundation MP4 (H.264 + AAC), up to 4K, 24/30/60 fps |
| Projects (`.adproj`) | yes | **not yet** |
| Custom presets, import/export | yes | yes (same `.adpreset` format) |

## Requirements
macOS 12 Monterey or newer, any Mac with Metal. The release build is **Intel (x86_64)**; on Apple Silicon Macs it runs under Rosetta 2.

## System audio on a Mac
macOS has no built-in way to capture what the Mac is playing. Install a free virtual audio driver such as
[BlackHole](https://existential.audio/blackhole/), create a *Multi-Output Device* in Audio MIDI Setup that sends sound to both your
speakers and BlackHole, set it as the output, and choose **BlackHole** as the source in Audio Dimension. Without such a driver only
microphones and other input devices are available. The app lists virtual drivers as "(system audio)" and shows
**SYSTEM AUDIO LIVE** while one is capturing.

## First start
The app is not notarized (no paid Apple developer account). If macOS says it cannot verify the developer, right-click the app,
choose **Open**, then **Open** again. Microphone access is requested the first time you press Start or Record; the red pill
appears whenever audio is being captured.

## Build from source
Needs only the Xcode Command Line Tools (`xcode-select --install`); full Xcode is not required.

```bash
cd mac
scripts/test.sh      # unit, rendering and export tests (uses a small XCTest shim because the Command Line Tools have no XCTest)
scripts/build.sh     # produces build/Audio Dimension.app (ad-hoc signed)
scripts/dmg.sh       # produces build/AudioDimension-<version>-macOS.dmg
```

Shaders are compiled from source when the app starts, which is why no `metal` compiler is needed at build time.

## How it works
`AudioCapture` (AVAudioEngine tap) -> `AudioAnalyzer` (Accelerate FFT, 64 log bins, 6 bands, beat detection) -> `VisualEngine`
(a visualizer emits triangles into a `Canvas`) -> `MetalRenderer` (MSAA scene, bloom, composite). Export runs the same chain offline,
one video frame per `1/fps` seconds of audio, then writes H.264 + AAC with `AVAssetWriter`, so picture and sound cannot drift apart.
