# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/).

## [1.0.0-preview.1] - 2026-11-20 (installer added the same day)

First public preview. Built in stages; stages 1 to 7 are complete and tested, stages 8 (polish and optimization) and 9 (installer and release verification) are only partly done.

### Installer
- Added `AudioDimension_Setup.exe` (Inno Setup 6): per-user install, Start Menu and optional desktop shortcut, uninstaller, bundled runtime and FFmpeg. Uninstall keeps user data unless you ask to delete it, and silent uninstalls never delete it (a test caught an early version that did).

### Added
- **Foundation:** WPF shell with dark theme, SQLite storage, Serilog logging, settings, collapsible inspector, DPI-aware UI scaling.
- **Audio:** microphone and WASAPI loopback capture, FFT with six frequency bands, level and peak meters, noise gate, automatic gain, beat detection, device-loss recovery, clear error messages, optional monitoring.
- **Rendering:** OpenGL renderer with HDR bloom and a full effects chain, 35 visualizers, particle systems, quality tiers, adaptive preview resolution, hardware warnings, fullscreen and detached preview windows, screenshots (PNG/JPEG, up to 4K, transparent).
- **Presets:** 60 built-in presets in 7 categories with thumbnails; search, filter, sort, favourites; custom presets with duplicate, rename, delete, import and export; documented and validated schema.
- **Text and titles:** text layers with full styling, 20 animation types with easing, delay, loop and direction, timeline-synchronised playback, drag positioning with snapping guides, 10 title templates and custom templates.
- **Recording and export:** audio recording with pause/resume, offline deterministic video rendering through bundled FFmpeg to MP4 (H.264, HEVC) and WebM (VP9), 720p to 4K and custom sizes, 24 to 60 fps, hardware encoders, progress with ETA, cancel, export history.
- **Projects:** `.adproj` packages with embedded assets, recent projects, duplicate, import, export, unsaved-changes tracking and save prompt; picture backgrounds.
- 360 automated tests, documentation for the preset and project formats, manual test checklist.

### Known limitations
- Windows only; installer tested on the development machine only.
- Anti-aliasing and VSync settings are not functional; video backgrounds are not implemented.
- Tested on one machine (RTX 4060); low-end (2 GB VRAM) hardware not verified; memory soak testing not done.
