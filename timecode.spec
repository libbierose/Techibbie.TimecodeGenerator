# timecode.spec
# PyInstaller build specification for the Techibbie Timecode Generator application.
# Produces a single-file executable for Windows, Linux, and macOS.
#
# Usage (from the project root):
#   pyinstaller timecode.spec
#
# Output: dist/Techibbie.TimecodeGenerator  (dist/Techibbie.TimecodeGenerator.exe on Windows)

from PyInstaller.utils.hooks import collect_data_files, collect_all

block_cipher = None

# qtawesome ships font and icon data files that must travel with the binary.
datas = collect_data_files('qtawesome')

# collect_all picks up the sounddevice extension module, the bundled
# PortAudio shared library (all platforms), and any hidden imports.
sd_binaries, sd_datas, sd_hiddenimports = collect_all('sounddevice')
datas    += sd_datas
binaries  = sd_binaries

a = Analysis(
    ['src/main.py'],
    pathex=['src'],          # make src/ importable so gui/audio/timecode resolve
    binaries=binaries,
    datas=datas,
    hiddenimports=sd_hiddenimports + [
        'PyQt6.QtCore',
        'PyQt6.QtWidgets',
        'PyQt6.QtGui',
        'PyQt6.sip',
        'qtawesome',
        'numpy',
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name='Techibbie.TimecodeGenerator',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,           # UPX disabled — unreliable across CI environments
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,       # no console window on Windows; no effect on Linux/macOS
    disable_windowed_traceback=False,
    argv_emulation=False,  # must be False for --onefile mode on macOS
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
