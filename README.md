# Timecode Generator

A cross-platform desktop application that generates **SMPTE Linear Timecode (LTC)** and displays a real-time timecode clock. Designed for film/video production workflows where a reliable, professional-grade timecode source is needed — including direct integration with **DaVinci Resolve** and other NLEs via audio output.

---

## Features

- **Real-time timecode display** — Full-screen dark UI with HH:MM:SS:FF readout
- **SMPTE LTC audio output** — Bit-accurate Biphase Mark Code (BMC) stream sent to any audio device; immediately lockable by DaVinci Resolve, Tentacle Sync, and hardware LTC readers
- **Multiple frame rates** — 24, 25, 29.97 (DF), 30, 48, 50, 59.94 (DF), 60, 120 fps with correct drop-frame handling
- **Gapless callback stream** — Uses a PortAudio low-latency callback to guarantee zero-gap LTC with no buffer underruns
- **Device native sample rate** — Generates LTC at the device's native rate to avoid WASAPI resampling that would corrupt bit timing
- **Click-tone fallback** — Optional audible click track when LTC mode is off, useful for manual sync
- **UTC clock mode** — Lock the timecode display to real-time UTC instead of elapsed time
- **WAV export** — Save any length of LTC to a 48 kHz 16-bit mono WAV file (e.g. for use as a reference track)
- **Persistent settings** — Window geometry, FPS, audio device, and mode are saved and restored between sessions

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

- **Python 3.11+**
- A system audio output device (built-in speakers or dedicated audio interface)
- Windows 10/11, macOS 12+, or Linux with ALSA/PulseAudio

### Python dependencies

| Package       | Purpose                                      |
| ------------- | -------------------------------------------- |
| `PyQt6`       | GUI framework                                |
| `qtawesome`   | Font Awesome icons for toolbar               |
| `sounddevice` | PortAudio bindings for low-latency audio I/O |
| `numpy`       | Fast array-based LTC waveform generation     |

---

## Download (pre-built binaries)

Pre-built single-file executables for Windows, Linux, and macOS are attached to every [GitHub Release](../../releases/latest).

| Platform | File                                                                              |
| -------- | --------------------------------------------------------------------------------- |
| Windows  | `Timecode-Windows.exe` — double-click to run                                      |
| Linux    | `Timecode-Linux` — `chmod +x Timecode-Linux && ./Timecode-Linux`                  |
| macOS    | `Timecode-macOS` — right-click → Open (required the first time due to Gatekeeper) |

---

## Installation (from source)

```bash
# 1. Clone the repo
git clone https://github.com/your-username/timecode.git
cd timecode

# 2. Create and activate a virtual environment (recommended)
python -m venv .venv

# Windows
.venv\Scripts\activate

# macOS / Linux
source .venv/bin/activate

# 3. Install dependencies
pip install -r requirements.txt
```

---

## Running

```bash
python src/main.py
```

---

## Usage

| Control                  | Action                                          |
| ------------------------ | ----------------------------------------------- |
| **Play / Pause** (▶ / ⏸) | Start or pause the timecode clock               |
| **Stop** (⏹)             | Stop and reset the clock to 00:00:00:00         |
| **LTC button** (●)       | Toggle LTC audio output on/off (green = active) |
| **Skip back / forward**  | Offset the clock by 30 frames                   |
| **Save WAV** (💾)        | Export LTC to a WAV file                        |
| **Settings** (⚙)         | Change FPS, audio device, UTC mode, and more    |

### Connecting to DaVinci Resolve

1. Connect your computer's audio output to a camera or audio recorder's timecode input (or use a virtual cable on the same machine).
2. In Resolve: **Preferences → System → Capture and Playback → Timecode Source → LTC**.
3. Press **Play** in this app and enable the **LTC** button — Resolve will lock within a few frames.

---

## Exporting an LTC WAV

1. Open **Settings → Save LTC to WAV File…** (or click the floppy-disk icon in the toolbar).
2. Set the desired duration (1–3600 seconds).
3. Choose a save location. The exported file starts at `01:00:00:00` at 48 kHz / 16-bit mono.

---

## Project Structure

```
timecode/
├── src/
│   ├── main.py                    # Application entry point
│   ├── gui/
│   │   ├── __init__.py
│   │   └── main_window.py         # Main GUI window and settings dialog
│   ├── timecode/
│   │   ├── __init__.py
│   │   └── timecode_handler.py    # Timecode arithmetic (NDF & drop-frame)
│   └── audio/
│       ├── __init__.py
│       ├── audio_handler.py       # Audio device management and LTC streaming
│       └── ltc_generator.py       # SMPTE 12M LTC waveform generator
├── tools/
│   ├── list_devices.py            # Print available audio output devices
│   ├── analyze_ltc.py             # Human-readable SMPTE frame field breakdown
│   ├── test_ltc.py                # Verify LTC bit patterns for known timecodes
│   ├── test_ltc_decode.py         # Round-trip encode→decode test at 24 & 30fps
│   └── test_ltc_detailed.py       # Full BCD + sync-word + waveform verification
├── timecode.spec                  # PyInstaller build spec (single-file exe)
├── .github/
│   └── workflows/
│       └── release.yml            # CI/CD: build & publish GitHub Releases
├── requirements.txt
├── .gitignore
└── README.md
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

| Branch    | Purpose                    | CI                                                               |
| --------- | -------------------------- | ---------------------------------------------------------------- |
| `dev`     | Day-to-day development     | Syntax check + LTC smoke test on every push                      |
| `release` | Stable, ready-to-ship code | Full 3-platform build + pre-release binaries on every push/merge |

### First-time setup

After pushing the repo to GitHub, run the included script to create both branches:

```powershell
.\setup-branches.ps1
```

Then in **GitHub → Settings → Branches**, set `dev` as the default branch and add a branch protection rule on `release` requiring a pull request before merging.

### Day-to-day workflow

```bash
# Work on dev
git checkout dev
git commit -m "my change"
git push                    # triggers CI checks

# Ship to release
git checkout release
git merge dev
git push                    # triggers 3-platform build + updates pre-release binaries
```

### Versioned releases

Tag any commit on `release` to publish a permanent versioned release:

```bash
git tag v1.0.0
git push origin v1.0.0
```

---

## Building locally

Requires [PyInstaller](https://pyinstaller.org):

```bash
pip install pyinstaller
pyinstaller timecode.spec
```

The output lands in `dist/timecode` (`dist/timecode.exe` on Windows).

---

## License

MIT
