[![CI](https://github.com/libbierose/Techibbie.TimecodeGenerator/actions/workflows/ci.yml/badge.svg?branch=dev)](https://github.com/libbierose/Techibbie.TimecodeGenerator/actions/workflows/ci.yml) [![Build & Release](https://github.com/libbierose/Techibbie.TimecodeGenerator/actions/workflows/release.yml/badge.svg?branch=release)](https://github.com/libbierose/Techibbie.TimecodeGenerator/actions/workflows/release.yml) [![ko-fi](https://img.shields.io/badge/Support%20me%20on-Ko--fi-FF5E5B?logo=kofi&logoColor=white)](https://ko-fi.com/G2G5IPEXX)

# Techibbie Timecode Generator

A cross-platform desktop application that generates **SMPTE Linear Timecode (LTC)** and displays a real-time timecode clock. Designed for film/video production workflows where a reliable, professional-grade timecode source is needed — with LTC that DaVinci Resolve and other NLEs can read back from a recorded audio track.

Built with **C# / .NET 8** and **Avalonia UI**.

---

## Features

- **Real-time timecode display** — dark, modern HH:MM:SS:FF readout that scales with the window
- **SMPTE LTC audio output** — bit-accurate Biphase Mark Code (BMC) stream sent to any audio device, verified in the test suite against an independent from-the-spec decoder
- **Multiple frame rates** — 24, 25, 29.97 (DF), 30, 48, 50, 59.94 (DF), 60, 120 fps. SMPTE LTC only exists at 24/25/30, so 48/50/60/120 fps (and 59.94 DF) are transmitted at the standard half/quarter rate (24/25/30/29.97 DF) — the convention LTC readers expect. Drop-frame numbering is exact.
- **Gapless callback stream** — a PortAudio low-latency callback guarantees zero-gap LTC with no buffer underruns
- **Device native sample rate** — generates LTC at the device's native rate to avoid resampling that would corrupt bit timing
- **Click-tone fallback** — optional audible click track when LTC mode is off, useful for manual sync
- **UTC clock mode** — lock the timecode display to real-time UTC instead of elapsed time
- **WAV export** — save any length of LTC to a 48 kHz 16-bit mono WAV file (e.g. for use as a reference track)
- **Persistent settings** — window geometry, FPS, audio device, and mode are saved and restored between sessions

---

## What is LTC?

**Linear Timecode (SMPTE 12M)** encodes timecode as an audio signal. Each frame of video is represented by 80 bits of Biphase Mark Code (BMC): a `0` bit has no mid-cell transition; a `1` bit has an additional transition at the midpoint. The signal is self-clocking and direction-independent, making it robust over audio cables and consumer sound cards.

This application generates a fully spec-compliant LTC stream including:

- BCD-encoded hours, minutes, seconds, and frames
- Drop-frame flag for 29.97 / 59.94 fps
- Biphase Mark Phase Correction (BMPC) parity bit
- Fixed SMPTE sync word (bits 64–79)

---

## Requirements

- **.NET 8 SDK** (to build/run from source) — pre-built releases are fully self-contained and need nothing installed
- A system audio output device (built-in speakers or dedicated audio interface)
- Windows 10/11, macOS 12+, or Linux (X11/Wayland with ALSA/PulseAudio)

### Key dependencies

| Package                       | Purpose                                                      |
| ------------------------------ | -------------------------------------------------------------|
| `Avalonia` / `Avalonia.Desktop`| Cross-platform UI framework                                  |
| `Avalonia.Themes.Fluent`       | Base theme, customized with a dark accent palette             |
| `PortAudioSharp2`              | Cross-platform PortAudio bindings for low-latency audio I/O   |

---

## Download (pre-built binaries)

Pre-built single-file, self-contained executables for Windows, Linux, and macOS are attached to every [GitHub Release](../../releases/latest). No .NET runtime install is required.

| Platform | File                                                                                                                      |
| -------- | -------------------------------------------------------------------------------------------------------------------------- |
| Windows  | `Techibbie.TimecodeGenerator-Windows.exe` — double-click to run                                                            |
| Linux    | `Techibbie.TimecodeGenerator-Linux` — `chmod +x Techibbie.TimecodeGenerator-Linux && ./Techibbie.TimecodeGenerator-Linux`  |
| macOS    | `Techibbie.TimecodeGenerator-macOS` — right-click → Open (required the first time due to Gatekeeper)                      |

---

## Building & running from source

```bash
# 1. Clone the repo
git clone https://github.com/libbierose/Techibbie.TimecodeGenerator.git
cd Techibbie.TimecodeGenerator

# 2. Restore and run the app
dotnet restore
dotnet run --project src/Techibbie.TimecodeGenerator.App
```

### Running the tests

```bash
dotnet test tests/Techibbie.TimecodeGenerator.Core.Tests
```

The test project verifies NDF/drop-frame timecode math, SMPTE 12M bit encoding (BCD fields, sync word, BMPC parity), and WAV output — including values cross-checked against the original reference implementation.

---

## Usage

| Control                  | Action                                          |
| ------------------------ | ------------------------------------------------ |
| **Play / Pause** (▶ / ⏸) | Start or pause the timecode clock               |
| **Stop** (⏹)             | Stop and reset the clock to 00:00:00:00         |
| **LTC button** (●)       | Toggle LTC audio output on/off (green = active) |
| **Skip back / forward**  | Offset the clock by 30 frames                   |
| **Save WAV** (⭳)         | Export LTC to a WAV file                        |
| **Settings** (⚙)         | Change FPS, audio device, UTC mode, and more    |

### Using the LTC with DaVinci Resolve

The workflow Resolve documents for audio LTC is reading it back from a **recorded audio track** (*Update Timecode from Audio – LTC*; see e.g. [RØDE's Resolve timecode guide](https://edge.rode.com/pdf/page/2218/modules/9192/Wireless%20PRO_Timecode%20Guide_Davinci%20Resolve%2018-5.pdf)):

1. Feed this app's LTC output into a camera or recorder's audio input (or record it from a loopback/virtual cable), so the LTC is captured on its own audio channel alongside your footage.
2. Import those clips into Resolve, select them in the Media Pool, right-click → **Update Timecode from Audio – LTC**.
3. Set your project frame rate to match the footage.

Tips: keep the LTC on its own dedicated channel, and record it at a moderate level — Resolve's LTC reader is sensitive to signals that are too quiet or too hot (this app outputs at about −6 dBFS). Use a standard LTC rate (24/25/29.97 DF/30). This app hasn't been verified against a live Resolve install; if you're expecting Resolve to *chase* LTC live from a sound card rather than read it from a recorded track, note that we found no documentation of that being supported.

---

## Exporting an LTC WAV

1. Click the save icon in the toolbar.
2. Set the desired duration (1–3600 seconds).
3. Choose a save location. The exported file starts at `01:00:00:00` at 48 kHz / 16-bit mono.

---

## Project structure

```
Techibbie.TimecodeGenerator.sln
src/
  Techibbie.TimecodeGenerator.Core/     # Pure logic: timecode math, LTC/BMC waveform generation, WAV writer
    Timecode/TimecodeHandler.cs
    Ltc/LtcGenerator.cs
    Wav/WavWriter.cs
  Techibbie.TimecodeGenerator.Audio/    # PortAudioSharp2-backed device enumeration and gapless LTC streaming
    AudioEngine.cs
  Techibbie.TimecodeGenerator.App/      # Avalonia UI application
    Views/MainWindow.axaml(.cs)
    Views/SettingsWindow.axaml(.cs)
    Views/AboutWindow.axaml(.cs)
    Services/SettingsStore.cs           # JSON settings persistence
    Services/UpdateService.cs           # GitHub release check + self-update
    Styles/Theme.axaml                  # Dark palette and control themes
tests/
  Techibbie.TimecodeGenerator.Core.Tests/  # xUnit tests for the Core library
.github/workflows/
  ci.yml                                # Build + test on push/PR
  release.yml                           # CI/CD: 3-platform self-contained publish & GitHub Release
```

---

## Branch strategy

```
dev  ──────────────────────────────────►  ongoing work
       │                    │
       │  PR / merge        │  PR / merge
       ▼                    ▼
     release ──────────────────────────►  always stable + built
```

| Branch    | Purpose                    | CI                                                                        |
| --------- | --------------------------- | --------------------------------------------------------------------------|
| `dev`     | Day-to-day development      | Build + Core unit tests on every push                                     |
| `release` | Stable, ready-to-ship code   | Full 3-platform build + auto-versioned GitHub Release on every push/merge |

### Day-to-day workflow

```bash
# Work on dev
git checkout dev
git commit -m "my change"
git push                    # triggers CI checks

# Ship to release
git checkout release
git merge dev
git push                    # triggers 3-platform build + publishes a new versioned release
```

### Versioning

Versions are assigned automatically using [CalVer](https://calver.org/) format **`YYYY.MM.N`**:

| Part   | Meaning                                     | Example         |
| ------ | -------------------------------------------- | --------------- |
| `YYYY` | Four-digit year                              | `2026`          |
| `MM`   | Zero-padded month                            | `04`            |
| `N`    | Release counter for that month, starts at 1  | `1`, `2`, `3` … |

Examples: `2026.04.1` (first April 2026 release), `2026.04.2` (second), `2026.05.1` (first May 2026 release).

The version is computed automatically on every push to `release` — no manual tagging needed.

---

## Building release binaries locally

```bash
dotnet publish src/Techibbie.TimecodeGenerator.App \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish/win-x64
```

Swap `win-x64` for `linux-x64` or `osx-x64` to build for another platform. The output is a single self-contained executable with no external .NET runtime dependency.

---

## License

MIT
