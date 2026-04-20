# Changelog

All notable changes to this project are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versions use CalVer: `YYYY.MM.N` (year, zero-padded month, release counter).

---

<!-- Releases are prepended here automatically by CI after each merge to release. -->

## 2026.04.1 -- 2026-04-20
﻿## What''s Changed

### Added
- Real-time SMPTE LTC (Linear Timecode) output over any audio device
- Support for 8 frame rates: 24, 25, 29.97 DF, 30, 48, 50, 59.94 DF, 60 fps
- Dark full-screen timecode display with large digit readout
- Play/pause and reset controls
- UTC real-time clock mode (runs from system clock instead of elapsed time)
- Audio click tones when LTC output is disabled
- Settings dialog: audio device selection, frame rate, LTC/audio/UTC toggles
- Export LTC signal to WAV file for use in video editors and NLEs
- Compatible with DaVinci Resolve and other NLE timecode readers
- Persistent settings saved between sessions
- Single-file executables for Windows, Linux, and macOS

---
