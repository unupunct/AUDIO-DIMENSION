# Third-party notices

Audio Dimension uses the following third-party software. Each remains under its own licence.

## Bundled program (separate process)

### FFmpeg
- Version: n9.0.2-58-g8c7ef49002 (2026-11-19), Windows x64 shared build `ffmpeg-n9.0-latest-win64-gpl-shared-9.0.zip` from <https://github.com/BtbN/FFmpeg-Builds> (linked from <https://ffmpeg.org/download.html>).
- Archive SHA-256: `04cbd0714a7fad85f799ab049c0594fd7716c89e0c86c915d814c36c4d7924ef`.
- **Licence: GNU General Public License v3** (the build is configured with `--enable-gpl --enable-version3` and includes x264, x265 and other GPL libraries). The full licence text ships with the program in `Tools\ffmpeg\LICENSE.txt`.
- Audio Dimension does **not link** FFmpeg: it starts `ffmpeg.exe` as an external process to encode video, and does not modify it.
- **Source code:** the corresponding source for this build is available from <https://git.ffmpeg.org/ffmpeg.git> (commit `8c7ef49002`), and the build scripts plus exact third-party library sources from <https://github.com/BtbN/FFmpeg-Builds>. See also `third_party/ffmpeg/BUILDINFO.md`.
- FFmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project.

## NuGet libraries

| Package | Version | Licence |
| --- | --- | --- |
| [OpenTK](https://github.com/opentk/opentk) | 4.8.2 | MIT |
| [OpenTK.GLWpfControl](https://github.com/opentk/GLWpfControl) | 4.3.3 | MIT |
| [NAudio](https://github.com/naudio/NAudio) | 3.3.0 | MIT |
| [SkiaSharp](https://github.com/mono/SkiaSharp) (wraps Google Skia) | 3.119.0 | MIT (Skia: BSD 3-Clause) |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MIT |
| [Serilog](https://github.com/serilog/serilog) | 4.4.0 | Apache-2.0 |
| [Serilog.Sinks.File](https://github.com/serilog/serilog-sinks-file) | 7.0.0 | Apache-2.0 |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) | 11.0.0 | MIT |
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) (bundle for the SQLite engine) | 3.0.5 | Apache-2.0 (SQLite itself is public domain) |

Test-only: xUnit 2.9.3 (Apache-2.0), Microsoft.NET.Test.Sdk (MIT), coverlet (MIT).

## Runtime
The release build is self-contained and includes the .NET runtime and the Windows Desktop runtime, which are licensed under the MIT licence (<https://github.com/dotnet/runtime>).
GLFW (bundled with OpenTK as `glfw3.dll`) is licensed under the zlib/libpng licence.
