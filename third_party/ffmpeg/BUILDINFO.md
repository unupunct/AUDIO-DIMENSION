# Bundled FFmpeg

Audio Dimension runs FFmpeg as a **separate process** (it is not linked into the application) to encode MP4 (H.264/HEVC) and WebM video and to mux audio.

| | |
|---|---|
| Build | `ffmpeg-n9.0-latest-win64-gpl-shared-9.0.zip` |
| Version | FFmpeg n9.0.2-58-g8c7ef49002 (2026-11-19) |
| Distributor | BtbN FFmpeg-Builds - https://github.com/BtbN/FFmpeg-Builds (linked from https://ffmpeg.org/download.html) |
| SHA-256 of the archive | `04cbd0714a7fad85f799ab049c0594fd7716c89e0c86c915d814c36c4d7924ef` (matches the release's `checksums.sha256`) |
| Licence | **GPL v3** (built with `--enable-gpl --enable-version3`, includes x264 and x265) - see `LICENSE.txt` |
| Source | FFmpeg source for this build: https://git.ffmpeg.org/ffmpeg.git (commit 8c7ef49002); build scripts and the exact third-party library sources: https://github.com/BtbN/FFmpeg-Builds |

The FFmpeg binaries are not stored in this git repository (they are ~180 MB). Run `tools/get-ffmpeg.ps1` to download and verify them into `third_party/ffmpeg/bin/`; the release installer already contains them.

Because the GPL applies to the FFmpeg binaries, anyone receiving them is entitled to the corresponding source (links above). Audio Dimension's own code remains under its own licence because it only starts FFmpeg as an external program.
