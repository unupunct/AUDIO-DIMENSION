# Manual test checklist

Tick each item on a clean Windows 10/11 x64 machine (ideally one without development tools). Items marked **(auto)** are also covered by automated tests, but should still be seen working once.

## Install and start
- [ ] Extract the portable ZIP (or run the installer) and start `AudioDimension.exe`; no .NET or FFmpeg install is required
- [ ] First start creates `%LOCALAPPDATA%\AudioDimension` with Database, Projects, Presets, Templates, Exports, Cache, Logs, Temp
- [ ] Window resizes, maximises, and the UI scales at 100%, 125%, 150% display scaling and on a second monitor
- [ ] Settings page: change theme, accent colour and UI scale; Save; restart; values persist
- [ ] Nothing is captured or uploaded at startup (no red capture pill)

## Audio
- [ ] Audio Settings lists every microphone and output device with status, sample rate and channels
- [ ] Microphone: Start capture shows the red MICROPHONE LIVE pill; level and peak meters move when you speak; spectrum and bands react
- [ ] System audio: play music, choose the output device, Start capture; meters and visuals follow the music **(auto)**
- [ ] Gain, sensitivity, noise gate, smoothing, bass/mid/treble, tilt, auto gain, peak detection, band toggles change the response
- [ ] Unplug a USB microphone or headset during capture: a clear message appears, capture reconnects when it is plugged back in
- [ ] Windows microphone privacy switch off: a readable message and a button that opens the privacy settings
- [ ] Stop capture / close the app: the pill disappears and nothing keeps recording

## Visualizer Studio and presets
- [ ] All 35 visualizers can be selected and react to music (spot-check at least one per kind)
- [ ] Quality Low/Medium/High/Ultra/Custom changes the look and speed; a warning appears when quality exceeds the GPU
- [ ] Drag to orbit (3D), wheel to zoom, double-click resets the camera; Reset camera and Reset visualization work
- [ ] Aspect ratios 16:9, 9:16, 1:1, 4:3, 21:9; preview resolution 25-100%; guides; info overlay
- [ ] Fullscreen preview (Esc exits) and detached borderless window, also on a second monitor
- [ ] Screenshot PNG and JPEG at preview size and at 4K; transparent PNG; the file opens and includes the text overlay
- [ ] Preset Library: search, category filter, favourites, sort, preview with live audio, Use this preset
- [ ] Save, update, duplicate, rename and delete a custom preset; export it; import it on another PC; an invalid file is rejected with a message
- [ ] Background picture can be chosen, dimmed and removed

## Text and titles
- [ ] Add each layer kind; edit font, size, weight, colour, spacing, shadow, outline, glow, panel, border
- [ ] Drag text in the preview: snap guides appear at the centre, thirds and margins; Ctrl+wheel resizes; alignment presets work
- [ ] Add each of the 20 animations; start, end, delay, duration, easing, loop, direction behave; scrub the timeline
- [ ] Apply all 10 title templates; save, export and import a custom template

## Recording and export
- [ ] Record with microphone and with system audio; pause and resume; the REC indicator and elapsed time are correct
- [ ] The exported MP4 (H.264) plays in Windows Media Player / VLC and the sound is in sync with the picture (clap or beat test) **(auto)**
- [ ] HEVC MP4 and WebM export and play; 720p, 1080p, 1440p, 4K and a custom size; 24, 25, 30, 50, 60 fps
- [ ] Hardware encoder (NVENC/QuickSync/AMF) is detected on supporting GPUs and produces a valid file
- [ ] Cancel an export: no partial file remains; try exporting to a full or read-only drive: a clear message appears
- [ ] "Render from an audio file" with a WAV and an MP3
- [ ] The UI stays responsive during recording and export

## Projects
- [ ] New, Save, Save As, Open, recent list, Duplicate, Import, Export a copy; shortcuts Ctrl+N/O/S/Shift+S
- [ ] Title bar shows `*` for unsaved changes; closing with unsaved changes asks to save
- [ ] Move the project to another PC: look, text, picture and audio settings come back; a missing audio device falls back to the default with a note
- [ ] Open a damaged or renamed file: a clear error and the current work is untouched

## Uninstall
- [ ] Uninstaller removes the program; your projects and presets are kept unless you choose to delete them
