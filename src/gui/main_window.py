"""
Main GUI window – dark full-screen timecode display.
"""

import os
import sys
import tempfile
import subprocess
import shutil
from datetime import datetime, timedelta

from PyQt6.QtWidgets import (
    QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, QLabel, QComboBox,
    QPushButton, QSpinBox, QCheckBox, QMessageBox,
    QDialog, QFormLayout, QDialogButtonBox, QSizePolicy, QFileDialog,
    QProgressDialog,
)
from PyQt6.QtCore import QTimer, Qt, QSize, QSettings, QThread, pyqtSignal
from PyQt6.QtGui import QFont, QFontMetrics
import qtawesome as qta
import json
import urllib.request
import urllib.error
import webbrowser

from timecode.timecode_handler import TimecodeHandler
from audio.audio_handler import AudioHandler

# ── Design tokens ─────────────────────────────────────────────────────────────
TC_GREEN = "#4ADE80"
TC_FONT  = "Consolas"
TC_SIZE  = 80          # pt – digit labels
DARK_BG  = "#191919"

APP_VERSION = "dev"
KOFI_URL    = "https://ko-fi.com/G2G5IPEXX"
GITHUB_REPO = "libbierose/Techibbie.TimecodeGenerator"

STYLE = f"""
QMainWindow, QWidget {{ background: {DARK_BG}; color: #cccccc; }}
QLabel {{ background: transparent; }}

/* ── Toolbar icon buttons ── */
QPushButton#icon_btn {{
    background: transparent;
    border: none;
    color: #606060;
    font-size: 20px;
    border-radius: 8px;
    min-width: 44px; max-width: 44px;
    min-height: 44px; max-height: 44px;
    padding: 0;
}}
QPushButton#icon_btn:hover   {{ background: #252525; color: #aaaaaa; }}
QPushButton#icon_btn:pressed {{ background: #303030; }}
QPushButton#icon_btn:disabled {{ color: #353535; }}
QPushButton#icon_btn[active="true"] {{ color: {TC_GREEN}; }}

/* ── Centre play/pause button ── */
QPushButton#play_btn {{
    background: #2b2b2b;
    border: none;
    color: #cccccc;
    font-size: 22px;
    border-radius: 10px;
    min-width: 52px; max-width: 52px;
    min-height: 52px; max-height: 52px;
    padding: 0;
}}
QPushButton#play_btn:hover   {{ background: #363636; }}
QPushButton#play_btn:pressed {{ background: #424242; }}

/* ── Settings dialog ── */
QDialog, QDialog QWidget {{ background: #222222; color: #cccccc; }}
QDialog QLabel {{ background: transparent; }}
QDialog QComboBox {{
    background: #2d2d2d; border: 1px solid #3a3a3a; border-radius: 5px;
    padding: 5px 10px; color: #ccc; min-height: 30px;
}}
QDialog QComboBox::drop-down {{ border: none; }}
QDialog QComboBox QAbstractItemView {{
    background: #2d2d2d; border: 1px solid #3a3a3a;
    selection-background-color: #383838; color: #ccc;
    outline: none;
}}
QDialog QCheckBox {{ spacing: 8px; }}
QDialog QCheckBox::indicator {{
    width: 17px; height: 17px;
    border: 1px solid #555; border-radius: 4px; background: #2d2d2d;
}}
QDialog QCheckBox::indicator:checked {{ background: {TC_GREEN}; border-color: {TC_GREEN}; }}
QDialog QSpinBox {{
    background: #2d2d2d; border: 1px solid #3a3a3a; border-radius: 5px;
    padding: 5px 10px; color: #ccc; min-height: 30px;
}}
QDialog QPushButton {{
    background: #2d2d2d; border: 1px solid #3a3a3a; border-radius: 5px;
    padding: 7px 18px; color: #ccc; min-height: 30px; min-width: 80px;
}}
QDialog QPushButton:hover {{ background: #383838; }}
"""


# ── Icon size for toolbar buttons ────────────────────────────────────────────
_ICON_SIZE = QSize(20, 20)
_ICON_COLOR = "#b2b2b2"
_ICON_COLOR_ACTIVE = TC_GREEN

# ── Helpers ───────────────────────────────────────────────────────────────────

def _icon_btn(fa_name: str, tooltip: str = "", color: str = _ICON_COLOR) -> QPushButton:
    btn = QPushButton()
    btn.setObjectName("icon_btn")
    btn.setIcon(qta.icon(fa_name, color=color))
    btn.setIconSize(_ICON_SIZE)
    if tooltip:
        btn.setToolTip(tooltip)
    return btn


def _set_btn_icon(btn: QPushButton, fa_name: str, color: str = _ICON_COLOR):
    btn.setIcon(qta.icon(fa_name, color=color))
    btn.setIconSize(_ICON_SIZE)


def _divider() -> QWidget:
    w = QWidget()
    w.setFixedHeight(1)
    w.setStyleSheet("background: #2e2e2e;")
    return w


# ── Update checker ────────────────────────────────────────────────────────────

class _UpdateCheckThread(QThread):
    """Background thread that checks the GitHub API for a newer release."""

    update_available = pyqtSignal(str, str, str)  # (tag, html_url, asset_download_url)
    no_update        = pyqtSignal()           # emitted when already up to date
    check_failed     = pyqtSignal(str)        # emitted on network/parse error (message)

    def __init__(self, parent=None, silent: bool = True):
        super().__init__(parent)
        self.silent = silent   # True = startup check (no feedback if up to date)

    def run(self):
        try:
            api_url = f"https://api.github.com/repos/{GITHUB_REPO}/releases/latest"
            req = urllib.request.Request(
                api_url,
                headers={"User-Agent": "Techibbie-TimecodeGenerator-UpdateCheck"},
            )
            with urllib.request.urlopen(req, timeout=8) as resp:  # nosec B310
                data = json.loads(resp.read().decode())
            tag = data.get("tag_name", "").lstrip("v")
            html_url = data.get(
                "html_url",
                f"https://github.com/{GITHUB_REPO}/releases/latest",
            )
            # Find the platform-specific binary asset
            if sys.platform == "win32":
                asset_suffix = "-Windows.exe"
            elif sys.platform == "darwin":
                asset_suffix = "-macOS"
            else:
                asset_suffix = "-Linux"
            asset_url = ""
            for asset in data.get("assets", []):
                if asset.get("name", "").endswith(asset_suffix):
                    asset_url = asset.get("browser_download_url", "")
                    break
            if tag and tag != APP_VERSION and APP_VERSION != "dev":
                self.update_available.emit(tag, html_url, asset_url)
            elif not self.silent:
                self.no_update.emit()
        except urllib.error.HTTPError as e:
            if not self.silent:
                if e.code == 404:
                    self.check_failed.emit(
                        "No releases found. The repository may be private or have no published releases yet."
                    )
                else:
                    self.check_failed.emit(f"GitHub returned HTTP {e.code}. Please try again later.")
        except Exception:
            if not self.silent:
                self.check_failed.emit("Could not reach GitHub. Check your internet connection and try again.")


# ── Asset downloader ──────────────────────────────────────────────────────────

class _DownloadThread(QThread):
    """Background thread that streams a release asset to disk."""

    progress = pyqtSignal(int)   # 0-100
    finished = pyqtSignal(str)   # path to downloaded file
    failed   = pyqtSignal(str)   # error message

    def __init__(self, url: str, dest: str, parent=None):
        super().__init__(parent)
        self.url  = url
        self.dest = dest
        self._cancelled = False

    def cancel(self):
        self._cancelled = True

    def run(self):
        try:
            req = urllib.request.Request(
                self.url,
                headers={"User-Agent": "Techibbie-TimecodeGenerator-Updater"},
            )
            with urllib.request.urlopen(req, timeout=120) as resp:  # nosec B310
                total      = int(resp.headers.get("Content-Length", 0))
                downloaded = 0
                with open(self.dest, "wb") as f:
                    while True:
                        if self._cancelled:
                            return
                        chunk = resp.read(65536)
                        if not chunk:
                            break
                        f.write(chunk)
                        downloaded += len(chunk)
                        if total:
                            self.progress.emit(int(downloaded * 100 / total))
            if not self._cancelled:
                self.progress.emit(100)
                self.finished.emit(self.dest)
        except Exception as exc:
            if not self._cancelled:
                self.failed.emit(str(exc))


# ── Auto-update helpers ───────────────────────────────────────────────────────

def _start_update_download(parent_widget, asset_url: str, tag: str) -> None:
    """Download *asset_url* and apply the update, showing a progress dialog."""

    suffix   = ".exe" if sys.platform == "win32" else ""
    tmp_fd, tmp_path = tempfile.mkstemp(suffix=suffix, prefix="TcgUpdate_")
    os.close(tmp_fd)

    dlg = QProgressDialog(
        f"Downloading version {tag}…", "Cancel", 0, 100, parent_widget
    )
    dlg.setWindowTitle("Downloading Update")
    dlg.setWindowModality(Qt.WindowModality.WindowModal)
    dlg.setMinimumDuration(0)
    dlg.setValue(0)

    thread = _DownloadThread(asset_url, tmp_path, parent_widget)
    thread.progress.connect(dlg.setValue)

    def _on_finished(path: str):
        dlg.close()
        _apply_update(path)

    def _on_failed(error: str):
        dlg.close()
        try:
            os.unlink(tmp_path)
        except OSError:
            pass
        QMessageBox.warning(
            parent_widget,
            "Download Failed",
            f"Could not download the update:\n{error}",
        )

    thread.finished.connect(_on_finished)
    thread.failed.connect(_on_failed)
    dlg.canceled.connect(thread.cancel)
    thread.finished.connect(thread.deleteLater)
    thread.start()
    parent_widget._dl_thread = thread   # keep alive


def _apply_update(new_exe_path: str) -> None:
    """Replace the running executable with the downloaded one and relaunch."""
    import pathlib

    if getattr(sys, "frozen", False):
        current_exe = sys.executable
    else:
        # Running from source — place the new exe in Downloads so it can be tested
        downloads = pathlib.Path.home() / "Downloads"
        current_exe = str(downloads / pathlib.Path(new_exe_path).name)

    if sys.platform == "win32":
        # Windows cannot replace a running executable — delegate to a PowerShell script.
        # Unblock-File removes the Zone.Identifier ADS that Windows applies to
        # downloaded files, which otherwise prevents PyInstaller from loading DLLs.
        ps_fd, ps_path = tempfile.mkstemp(suffix=".ps1", prefix="TcgSwap_")
        with os.fdopen(ps_fd, "w") as ps:
            ps.write(
                "Start-Sleep -Seconds 2\n"
                f'Move-Item -Force -LiteralPath "{new_exe_path}" -Destination "{current_exe}"\n'
                f'Unblock-File -LiteralPath "{current_exe}"\n'
                f'Start-Process -FilePath "{current_exe}"\n'
                "Remove-Item -LiteralPath $PSCommandPath -Force\n"
            )
        subprocess.Popen(  # noqa: S603
            [
                "powershell.exe",
                "-NonInteractive", "-NoProfile",
                "-ExecutionPolicy", "Bypass",
                "-File", ps_path,
            ],
            creationflags=subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP,
            close_fds=True,
        )
    else:
        # Linux/macOS: safe to replace an open file (inode swap)
        shutil.move(new_exe_path, current_exe)
        os.chmod(current_exe, 0o755)
        subprocess.Popen([current_exe])  # noqa: S603

    from PyQt6.QtWidgets import QApplication
    QApplication.quit()


# ── About dialog ──────────────────────────────────────────────────────────────

class AboutDialog(QDialog):
    """Modal dialog showing app info, version, and Ko-fi support link."""

    def __init__(self, parent: "TimecodeGeneratorWindow"):
        super().__init__(parent)
        self.setWindowTitle("About")
        self.setMinimumWidth(400)
        self.setModal(True)

        layout = QVBoxLayout(self)
        layout.setSpacing(14)
        layout.setContentsMargins(32, 28, 32, 24)

        title = QLabel("Techibbie Timecode Generator")
        title.setAlignment(Qt.AlignmentFlag.AlignCenter)
        title.setStyleSheet("font-size: 15pt; font-weight: 600; color: #cccccc;")
        layout.addWidget(title)

        ver_lbl = QLabel(f"Version {APP_VERSION}")
        ver_lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
        ver_lbl.setStyleSheet("color: #606060; font-size: 10pt;")
        layout.addWidget(ver_lbl)

        layout.addWidget(_divider())

        desc = QLabel(
            "A cross-platform SMPTE Linear Timecode (LTC) generator\n"
            "for film and video production workflows."
        )
        desc.setAlignment(Qt.AlignmentFlag.AlignCenter)
        desc.setStyleSheet("color: #aaaaaa; font-size: 10pt;")
        desc.setWordWrap(True)
        layout.addWidget(desc)

        layout.addWidget(_divider())

        kofi_btn = QPushButton("  Support on Ko-fi  ☕")
        kofi_btn.setStyleSheet(
            "QPushButton { background: #FF5E5B; border: none; border-radius: 6px;"
            " color: white; font-size: 11pt; font-weight: 600; padding: 10px 20px; }"
            "QPushButton:hover { background: #ff7674; }"
            "QPushButton:pressed { background: #e54b48; }"
        )
        kofi_btn.clicked.connect(lambda: webbrowser.open(KOFI_URL))
        layout.addWidget(kofi_btn)

        gh_btn = QPushButton("View on GitHub")
        gh_btn.clicked.connect(
            lambda: webbrowser.open(f"https://github.com/{GITHUB_REPO}")
        )
        layout.addWidget(gh_btn)

        self._update_btn = QPushButton("Check for Updates")
        self._update_btn.clicked.connect(self._check_for_updates)
        layout.addWidget(self._update_btn)

        close_btns = QDialogButtonBox(QDialogButtonBox.StandardButton.Close)
        close_btns.rejected.connect(self.reject)
        layout.addWidget(close_btns)

    def _check_for_updates(self):
        self._update_btn.setText("Checking…")
        self._update_btn.setEnabled(False)
        thread = _UpdateCheckThread(self, silent=False)
        thread.update_available.connect(self._on_update_found)
        thread.no_update.connect(self._on_no_update)
        thread.check_failed.connect(self._on_check_failed)
        thread.finished.connect(thread.deleteLater)
        thread.start()
        self._thread = thread   # keep reference alive

    def _on_update_found(self, tag: str, url: str, asset_url: str):
        self._update_btn.setText("Check for Updates")
        self._update_btn.setEnabled(True)
        msg = QMessageBox(self)
        msg.setWindowTitle("Update Available")
        msg.setText(
            f"A new version is available!\n\n"
            f"Installed:  {APP_VERSION}\n"
            f"Latest:       {tag}"
        )
        frozen = getattr(sys, "frozen", False)
        if asset_url:
            msg.setInformativeText("Would you like to update now? The app will restart.")
            update_btn = msg.addButton("Update Now", QMessageBox.ButtonRole.AcceptRole)
            msg.addButton("Later", QMessageBox.ButtonRole.RejectRole)
            msg.exec()
            if msg.clickedButton() is update_btn:
                _start_update_download(self, asset_url, tag)
        else:
            msg.setInformativeText(
                "No installer was found for your platform. "
                "Visit the releases page to download manually."
            )
            view_btn = msg.addButton("Open Releases Page", QMessageBox.ButtonRole.AcceptRole)
            msg.addButton("Close", QMessageBox.ButtonRole.RejectRole)
            msg.exec()
            if msg.clickedButton() is view_btn:
                webbrowser.open(url)

    def _on_no_update(self):
        self._update_btn.setText("Check for Updates")
        self._update_btn.setEnabled(True)
        QMessageBox.information(self, "Up to Date", "You are running the latest version.")

    def _on_check_failed(self, message: str):
        self._update_btn.setText("Check for Updates")
        self._update_btn.setEnabled(True)
        QMessageBox.warning(self, "Update Check Failed", message)


# ── Settings dialog ───────────────────────────────────────────────────────────

class SettingsDialog(QDialog):
    """All configuration in one modal dialog."""

    def __init__(self, parent: "TimecodeGeneratorWindow"):
        super().__init__(parent)
        self.setWindowTitle("Settings")
        self.setMinimumWidth(500)
        self.setModal(True)

        layout = QVBoxLayout(self)
        layout.setSpacing(14)
        layout.setContentsMargins(28, 24, 28, 20)

        form = QFormLayout()
        form.setSpacing(10)
        form.setLabelAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)

        # FPS
        self.fps_combo = QComboBox()
        for fps in parent.timecode_handler.get_supported_fps():
            self.fps_combo.addItem(str(fps), fps)
        idx = self.fps_combo.findData(parent.timecode_handler.fps)
        if idx >= 0:
            self.fps_combo.setCurrentIndex(idx)
        form.addRow("Frame Rate:", self.fps_combo)

        # Audio device
        self.device_combo = QComboBox()
        for dev in parent._audio_devices:
            sr = int(dev["sample_rate"])
            self.device_combo.addItem(
                f"{dev['name']}  ({dev['channels']}ch · {sr}\u202fHz)",
                dev["id"],
            )
        for i in range(self.device_combo.count()):
            if self.device_combo.itemData(i) == parent.selected_audio_device:
                self.device_combo.setCurrentIndex(i)
                break
        form.addRow("Audio Device:", self.device_combo)

        layout.addLayout(form)

        # Sample-rate info
        self.sr_lbl = QLabel("")
        self.sr_lbl.setStyleSheet("color: #a08000; font-size: 11px;")
        self.sr_lbl.setWordWrap(True)
        layout.addWidget(self.sr_lbl)
        self.device_combo.currentIndexChanged.connect(self._refresh_sr)
        self._refresh_sr()

        layout.addWidget(_divider())

        # Checkboxes
        self.ltc_check    = QCheckBox("Enable LTC output  (for DaVinci Resolve / NLEs)")
        self.audio_check  = QCheckBox("Enable audio  (click tones when LTC is off)")
        self.utc_check    = QCheckBox("Use real-time UTC clock instead of elapsed time")
        self.update_check = QCheckBox("Check for updates on startup")
        self.ltc_check.setChecked(parent.audio_handler.ltc_mode)
        self.audio_check.setChecked(parent.enable_audio)
        self.utc_check.setChecked(parent.use_utc_time)
        self.update_check.setChecked(parent.check_for_updates)
        for cb in (self.ltc_check, self.audio_check, self.utc_check, self.update_check):
            layout.addWidget(cb)

        layout.addWidget(_divider())

        # Save WAV
        save_btn = QPushButton("Save LTC to WAV File…")
        save_btn.clicked.connect(parent.on_save_ltc_wav)
        layout.addWidget(save_btn)

        # OK / Cancel
        btns = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel
        )
        btns.accepted.connect(self.accept)
        btns.rejected.connect(self.reject)
        layout.addWidget(btns)

    def _refresh_sr(self):
        import sounddevice as sd
        try:
            dev_id = self.device_combo.currentData()
            if dev_id is not None:
                sr = int(sd.query_devices(dev_id)["default_samplerate"])
                self.sr_lbl.setText(
                    f"Device native rate: {sr}\u202fHz — LTC will be generated at this rate."
                    if sr != 48000 else ""
                )
        except Exception:
            self.sr_lbl.setText("")


# ── Main window ───────────────────────────────────────────────────────────────

class TimecodeGeneratorWindow(QMainWindow):
    """Main window — dark full-screen timecode display."""

    def __init__(self):
        super().__init__()
        self.setWindowTitle("Techibbie Timecode Generator")
        self.resize(1100, 420)
        self.setMinimumSize(860, 340)

        self.timecode_handler = TimecodeHandler(30.0)
        self.audio_handler    = AudioHandler()

        self.is_running            = False
        self.start_time: datetime | None = None
        self.last_frame            = -1
        self.enable_audio          = True
        self.use_utc_time          = False
        self.check_for_updates     = True
        self.selected_audio_device = None
        self._audio_devices: list  = []
        self._update_thread: _UpdateCheckThread | None = None

        self.setStyleSheet(STYLE)
        self._build_ui()

        self.timer = QTimer()
        self.timer.timeout.connect(self.update_timecode)
        self.timer.setInterval(5)  # 200 Hz – smooth frame display

        self._refresh_devices()
        self._load_settings()
        self._start_update_check()

    def _build_ui(self):
        root_widget = QWidget()
        self.setCentralWidget(root_widget)

        root = QVBoxLayout(root_widget)
        root.setContentsMargins(0, 28, 0, 22)
        root.setSpacing(0)

        root.addStretch(2)

        # ── Timecode digit pairs with field labels ────────────────────────────
        tc_row = QHBoxLayout()
        tc_row.setSpacing(0)

        self.tc_digits:  list[QLabel] = []
        self.tc_colons:  list[QLabel] = []
        self.tc_fields:  list[QLabel] = []

        for idx, fname in enumerate(("HOURS", "MINUTES", "SECONDS", "FRAMES")):
            # Colon separator
            if idx > 0:
                colon_col = QWidget()
                colon_col.setSizePolicy(QSizePolicy.Policy.Fixed, QSizePolicy.Policy.Preferred)
                cv = QVBoxLayout(colon_col)
                cv.setContentsMargins(0, 0, 0, 0)
                cv.setSpacing(0)

                cl = QLabel(":")
                cl.setFont(QFont(TC_FONT, TC_SIZE))
                cl.setAlignment(Qt.AlignmentFlag.AlignCenter)
                cl.setStyleSheet(f"color: {TC_GREEN};")
                cv.addWidget(cl)
                self.tc_colons.append(cl)

                spacer = QLabel()
                spacer.setFixedHeight(22)
                cv.addWidget(spacer)

                tc_row.addWidget(colon_col)

            # Digit + field name stacked
            pair = QWidget()
            pv   = QVBoxLayout(pair)
            pv.setContentsMargins(0, 0, 0, 0)
            pv.setSpacing(4)

            d = QLabel("00")
            d.setFont(QFont(TC_FONT, TC_SIZE))
            d.setAlignment(Qt.AlignmentFlag.AlignCenter)
            d.setStyleSheet(f"color: {TC_GREEN};")
            pv.addWidget(d)
            self.tc_digits.append(d)

            fl = QLabel(fname)
            fl.setAlignment(Qt.AlignmentFlag.AlignCenter)
            fl.setStyleSheet("color: #707070; font-family: 'Segoe UI'; font-size: 10pt; letter-spacing: 2px; font-weight: 400;")
            pv.addWidget(fl)
            self.tc_fields.append(fl)

            tc_row.addWidget(pair)

        root.addLayout(tc_row)

        root.addStretch(3)

        # ── Status line ────────────────────────────────────────────────────────
        self.status_label = QLabel("Stopped  ·  30 fps  ·  NDF")
        self.status_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.status_label.setStyleSheet("color: #383838; font-size: 10pt;")
        root.addWidget(self.status_label)

        root.addSpacing(14)

        # ── Toolbar ────────────────────────────────────────────────────────────
        tb = QHBoxLayout()
        tb.setSpacing(4)

        # Left: save WAV
        self.save_wav_btn = _icon_btn("fa6s.floppy-disk", "Save LTC to WAV file")
        self.save_wav_btn.clicked.connect(self.on_save_ltc_wav)
        tb.addWidget(self.save_wav_btn)

        tb.addStretch(1)

        # Centre cluster
        self.back_btn = _icon_btn("fa6s.rotate-left", "Skip back 30 frames")
        self.back_btn.clicked.connect(self.on_skip_back)
        self.back_btn.setEnabled(False)
        tb.addWidget(self.back_btn)

        self.stop_btn = _icon_btn("fa6s.stop", "Stop")
        self.stop_btn.setEnabled(False)
        self.stop_btn.clicked.connect(self.on_stop_clicked)
        tb.addWidget(self.stop_btn)

        self.play_pause_btn = QPushButton()
        self.play_pause_btn.setObjectName("play_btn")
        self.play_pause_btn.setIcon(qta.icon("fa6s.play", color="#cccccc"))
        self.play_pause_btn.setIconSize(QSize(22, 22))
        self.play_pause_btn.setToolTip("Start / Pause")
        self.play_pause_btn.clicked.connect(self.on_play_pause_clicked)
        tb.addWidget(self.play_pause_btn)

        self.ltc_btn = _icon_btn("fa6s.circle-dot", "Toggle LTC output")
        self.ltc_btn.setProperty("active", "false")
        self.ltc_btn.clicked.connect(self.on_ltc_btn_clicked)
        tb.addWidget(self.ltc_btn)

        self.fwd_btn = _icon_btn("fa6s.rotate-right", "Skip forward 30 frames")
        self.fwd_btn.clicked.connect(self.on_skip_fwd)
        self.fwd_btn.setEnabled(False)
        tb.addWidget(self.fwd_btn)

        tb.addStretch(1)

        # Right: fps badge + settings
        self.fps_label = QLabel("30 fps")
        self.fps_label.setStyleSheet("color: #404040; font-size: 11pt; font-weight: 300;")
        self.fps_label.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        tb.addWidget(self.fps_label)

        self.settings_btn = _icon_btn("fa6s.gear", "Settings")
        self.settings_btn.clicked.connect(self.on_settings_clicked)
        tb.addWidget(self.settings_btn)

        self.about_btn = _icon_btn("fa6s.circle-info", "About")
        self.about_btn.clicked.connect(self.on_about_clicked)
        tb.addWidget(self.about_btn)

        root.addLayout(tb)

        # These widgets are hidden in the main UI — settings are now managed
        # through SettingsDialog — but are kept as instance attributes so that
        # internal enable/disable helpers can reference them without guards.
        self.fps_combo        = QComboBox(root_widget);        self.fps_combo.setVisible(False)
        self.device_combo     = QComboBox(root_widget);        self.device_combo.setVisible(False)
        self.sr_warning_label = QLabel(root_widget);           self.sr_warning_label.setVisible(False)

        for fps in self.timecode_handler.get_supported_fps():
            self.fps_combo.addItem(str(fps), fps)
        self.fps_combo.setCurrentText("30")

    # ── Device management ─────────────────────────────────────────────────────

    def _refresh_devices(self):
        self._audio_devices = self.audio_handler.get_audio_devices()
        self.device_combo.clear()
        for dev in self._audio_devices:
            sr = int(dev["sample_rate"])
            self.device_combo.addItem(
                f"{dev['name']} ({dev['channels']}ch, {sr}Hz)", dev["id"]
            )
        if self._audio_devices:
            self.selected_audio_device = self._audio_devices[0]["id"]

    def populate_audio_devices(self):
        self._refresh_devices()

    def _update_sr_warning(self):
        pass  # handled in SettingsDialog

    # ── Persist settings ──────────────────────────────────────────────────────

    def _save_settings(self):
        s = QSettings("Techibbie", "TimecodeGenerator")
        s.setValue("geometry",          self.saveGeometry())
        s.setValue("fps",               self.timecode_handler.fps)
        s.setValue("ltc_mode",          self.audio_handler.ltc_mode)
        s.setValue("enable_audio",      self.enable_audio)
        s.setValue("use_utc_time",      self.use_utc_time)
        s.setValue("check_for_updates", self.check_for_updates)
        if self.selected_audio_device is not None:
            s.setValue("audio_device", self.selected_audio_device)

    def _load_settings(self):
        s = QSettings("Techibbie", "TimecodeGenerator")
        geom = s.value("geometry")
        if geom:
            self.restoreGeometry(geom)
        fps = s.value("fps", 30.0, type=float)
        self.timecode_handler = TimecodeHandler(fps)
        raw_dev = s.value("audio_device", None)
        if raw_dev is not None:
            dev_id = int(raw_dev)
            if dev_id in [d["id"] for d in self._audio_devices]:
                self.selected_audio_device = dev_id
        ltc = s.value("ltc_mode", False, type=bool)
        self.audio_handler.set_ltc_mode(ltc)
        _set_btn_icon(
            self.ltc_btn, "fa6s.circle-dot",
            color=_ICON_COLOR_ACTIVE if ltc else _ICON_COLOR,
        )
        self.enable_audio      = s.value("enable_audio",      True,  type=bool)
        self.use_utc_time      = s.value("use_utc_time",      False, type=bool)
        self.check_for_updates = s.value("check_for_updates", True,  type=bool)
        self._refresh_status()

    # ── Status helper ─────────────────────────────────────────────────────────

    def _refresh_status(self):
        fps   = self.timecode_handler.fps
        drop  = "DF" if self.timecode_handler.is_drop_frame() else "NDF"
        ltc   = "  ·  LTC ON" if self.audio_handler.ltc_mode else ""
        if self.is_running:
            state = "Running"
        elif self.start_time is not None:
            state = "Paused"
        else:
            state = "Stopped"
        self.status_label.setText(f"{state}  ·  {fps:g} fps  ·  {drop}{ltc}")
        self.fps_label.setText(f"{fps:g} fps")

    # ── Toolbar slots ─────────────────────────────────────────────────────────

    def resizeEvent(self, event):
        super().resizeEvent(event)
        # Target: timecode fills 80% of window width (10% margin each side).
        # Binary-search for the largest pt where "00:00:00:00" fits that width.
        target_px = int(self.width() * 0.80)
        lo, hi = 10, 400
        while lo < hi - 1:
            mid = (lo + hi) // 2
            if QFontMetrics(QFont(TC_FONT, mid)).horizontalAdvance("00:00:00:00") <= target_px:
                lo = mid
            else:
                hi = mid
        pt = max(14, lo)
        f = QFont(TC_FONT, pt)
        for lbl in self.tc_digits:
            lbl.setFont(f)
        for cl in self.tc_colons:
            cl.setFont(f)
        fpt = max(8, int(pt * 0.12))
        for fl in self.tc_fields:
            fl.setStyleSheet(
                f"color: #707070; font-family: 'Segoe UI'; font-size: {fpt}pt; letter-spacing: 2px; font-weight: 400;"
            )

    def on_play_pause_clicked(self):
        if not self.is_running:
            self.on_start_clicked()
        else:
            self.on_pause_clicked()

    def on_ltc_btn_clicked(self):
        ltc = not self.audio_handler.ltc_mode
        self.audio_handler.set_ltc_mode(ltc)
        # Update icon colour directly (property re-polish doesn't affect QIcon)
        _set_btn_icon(
            self.ltc_btn, "fa6s.circle-dot",
            color=_ICON_COLOR_ACTIVE if ltc else _ICON_COLOR,
        )
        if self.is_running and self.enable_audio:
                if ltc:
                    now = datetime.utcnow() if self.use_utc_time else None
                    hh = now.hour   if now else 0
                    mm = now.minute if now else 0
                    ss = now.second if now else 0
                    ff = int(now.microsecond / 1_000_000 * self.timecode_handler.fps) if now else 0
                    self.audio_handler.start_ltc_stream(
                        hh, mm, ss, ff,
                        self.timecode_handler.fps,
                        self.timecode_handler.is_drop_frame(),
                        self.selected_audio_device,
                    )
                else:
                    self.audio_handler.stop_ltc_stream()
        self._refresh_status()

    def on_skip_back(self):
        if self.start_time is not None:
            self.start_time += timedelta(seconds=30 / self.timecode_handler.fps)

    def on_skip_fwd(self):
        if self.start_time is not None:
            self.start_time -= timedelta(seconds=30 / self.timecode_handler.fps)

    def on_settings_clicked(self):
        dlg = SettingsDialog(self)
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return

        # Apply FPS
        fps = float(dlg.fps_combo.currentData())
        if fps != self.timecode_handler.fps:
            self.timecode_handler = TimecodeHandler(fps)

        # Apply device
        self.selected_audio_device = dlg.device_combo.currentData()

        # Apply LTC
        ltc = dlg.ltc_check.isChecked()
        if ltc != self.audio_handler.ltc_mode:
            self.audio_handler.set_ltc_mode(ltc)
            _set_btn_icon(
                self.ltc_btn, "fa6s.circle-dot",
                color=_ICON_COLOR_ACTIVE if ltc else _ICON_COLOR,
            )
            if self.is_running and self.enable_audio:
                if ltc:
                    now = datetime.utcnow() if self.use_utc_time else None
                    hh = now.hour   if now else 0
                    mm = now.minute if now else 0
                    ss = now.second if now else 0
                    ff = int(now.microsecond / 1_000_000 * self.timecode_handler.fps) if now else 0
                    self.audio_handler.start_ltc_stream(
                        hh, mm, ss, ff, fps,
                        self.timecode_handler.is_drop_frame(),
                        self.selected_audio_device,
                    )
                else:
                    self.audio_handler.stop_ltc_stream()

        self.enable_audio      = dlg.audio_check.isChecked()
        self.use_utc_time      = dlg.utc_check.isChecked()
        self.check_for_updates = dlg.update_check.isChecked()
        if self.use_utc_time:
            self.start_time = None
        self._save_settings()
        self._refresh_status()

    # ── Playback slots ────────────────────────────────────────────────────────

    def on_start_clicked(self):
        if not self.is_running:
            self.is_running = True
            self.start_time = datetime.now()
            self.timer.start()
            self.play_pause_btn.setIcon(qta.icon("fa6s.pause", color="#cccccc"))
            self.stop_btn.setEnabled(True)
            self.back_btn.setEnabled(True)
            self.fwd_btn.setEnabled(True)
            self.settings_btn.setEnabled(False)
            self.fps_combo.setEnabled(False)
            self.device_combo.setEnabled(False)
            if self.enable_audio and self.audio_handler.ltc_mode:
                now = datetime.utcnow() if self.use_utc_time else None
                hh = now.hour   if now else 0
                mm = now.minute if now else 0
                ss = now.second if now else 0
                ff = int(now.microsecond / 1_000_000 * self.timecode_handler.fps) if now else 0
                self.audio_handler.start_ltc_stream(
                    hh, mm, ss, ff,
                    self.timecode_handler.fps,
                    self.timecode_handler.is_drop_frame(),
                    self.selected_audio_device,
                )
            self._refresh_status()

    def on_pause_clicked(self):
        if self.is_running:
            self.is_running = False
            self.timer.stop()
            self.audio_handler.stop_ltc_stream()
            self.play_pause_btn.setIcon(qta.icon("fa6s.play", color="#cccccc"))
            self._refresh_status()

    def on_stop_clicked(self):
        self.is_running = False
        self.timer.stop()
        self.audio_handler.stop_ltc_stream()
        self.start_time = None
        self.last_frame = -1
        for d in self.tc_digits:
            d.setText("00")
        self.play_pause_btn.setIcon(qta.icon("fa6s.play", color="#cccccc"))
        self.stop_btn.setEnabled(False)
        self.back_btn.setEnabled(False)
        self.fwd_btn.setEnabled(False)
        self.settings_btn.setEnabled(True)
        self.fps_combo.setEnabled(True)
        self.device_combo.setEnabled(True)
        self._refresh_status()

    def on_reset_clicked(self):
        self.on_stop_clicked()

    # ── Timecode update ───────────────────────────────────────────────────────

    def update_timecode(self):
        if not self.is_running or self.start_time is None:
            return

        elapsed = (datetime.now() - self.start_time).total_seconds()
        base_tc = self.timecode_handler.elapsed_time_to_timecode(elapsed)

        if self.use_utc_time:
            # In UTC mode the display shows the current wall-clock time rather
            # than elapsed time, while base_tc (elapsed) is still used for the
            # LTC audio stream so it remains gapless.
            now = datetime.utcnow()
            display_tc = (
                f"{now.hour:02d}:{now.minute:02d}:{now.second:02d}:"
                f"{int(now.microsecond / 1_000_000 * self.timecode_handler.fps):02d}"
            )
        else:
            display_tc = base_tc

        parts = display_tc.split(":")
        if len(parts) == 4:
            for lbl, val in zip(self.tc_digits, parts):
                lbl.setText(val)

        current_frame = int(elapsed * self.timecode_handler.fps)
        if self.enable_audio and current_frame != self.last_frame:
            if not self.audio_handler.ltc_mode:
                self.audio_handler.play_frame_click(
                    current_frame, self.timecode_handler.fps, self.selected_audio_device
                )
            self.last_frame = current_frame

    # ── WAV export ────────────────────────────────────────────────────────────

    def on_save_ltc_wav(self):
        import wave
        import numpy as np

        dlg = QDialog(self)
        dlg.setWindowTitle("Save LTC WAV")
        form = QFormLayout(dlg)
        dur_spin = QSpinBox()
        dur_spin.setRange(1, 3600)
        dur_spin.setValue(60)
        dur_spin.setSuffix(" seconds")
        form.addRow("Duration:", dur_spin)
        btns = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel
        )
        btns.accepted.connect(dlg.accept)
        btns.rejected.connect(dlg.reject)
        form.addRow(btns)

        if dlg.exec() != QDialog.DialogCode.Accepted:
            return

        duration = dur_spin.value()
        path, _ = QFileDialog.getSaveFileName(
            self, "Save LTC WAV", "ltc_timecode.wav", "WAV Files (*.wav)"
        )
        if not path:
            return

        fps  = self.timecode_handler.fps
        drop = self.timecode_handler.is_drop_frame()
        from audio.ltc_generator import LTCGenerator
        gen   = LTCGenerator(48000)
        audio = gen.generate_continuous_ltc(1, 0, 0, 0, fps, drop, float(duration))
        # Convert float32 [-1, 1] to signed 16-bit PCM for WAV compatibility
        data  = (audio * 32767).astype(np.int16)

        try:
            with wave.open(path, "w") as wf:
                wf.setnchannels(1)      # Mono — LTC is always a single-channel signal
                wf.setsampwidth(2)      # 16-bit PCM (2 bytes per sample)
                wf.setframerate(48000)  # Standard broadcast sample rate
                wf.writeframes(data.tobytes())
            QMessageBox.information(
                self, "Saved",
                f"LTC WAV saved:\n{path}\n\n"
                f"{duration}s · {fps:g}\u202ffps · 48\u202fkHz · starts at 01:00:00:00",
            )
        except Exception as e:
            QMessageBox.critical(self, "Error", f"Failed to save WAV:\n{e}")

    # ── Compat stubs ──────────────────────────────────────────────────────────

    def on_fps_changed(self):
        fps = float(self.fps_combo.currentData())
        self.timecode_handler = TimecodeHandler(fps)
        self._refresh_status()

    def on_device_changed(self):
        self.selected_audio_device = self.device_combo.currentData()

    def on_audio_toggle(self):
        pass

    def on_ltc_mode_toggle(self):
        pass

    def on_utc_mode_toggle(self):
        pass

    def show_obs_help(self):
        pass

    # ── About / update ────────────────────────────────────────────────────────

    def on_about_clicked(self):
        AboutDialog(self).exec()

    def _start_update_check(self):
        if not self.check_for_updates:
            return
        self._update_thread = _UpdateCheckThread(self, silent=True)
        self._update_thread.update_available.connect(self._on_update_available)
        self._update_thread.start()

    def _on_update_available(self, tag: str, url: str, asset_url: str):
        msg = QMessageBox(self)
        msg.setWindowTitle("Update Available")
        msg.setText(
            "A new version of Techibbie Timecode Generator is available.\n\n"
            f"Installed:  {APP_VERSION}\n"
            f"Latest:       {tag}"
        )
        frozen = getattr(sys, "frozen", False)
        if asset_url:
            msg.setInformativeText("Would you like to update now? The app will restart.")
            update_btn = msg.addButton("Update Now", QMessageBox.ButtonRole.AcceptRole)
            msg.addButton("Later", QMessageBox.ButtonRole.RejectRole)
            msg.exec()
            if msg.clickedButton() is update_btn:
                _start_update_download(self, asset_url, tag)
        else:
            msg.setInformativeText(
                "No installer was found for your platform. "
                "Visit the releases page to download manually."
            )
            view_btn = msg.addButton("Open Releases Page", QMessageBox.ButtonRole.AcceptRole)
            msg.addButton("Close", QMessageBox.ButtonRole.RejectRole)
            msg.exec()
            if msg.clickedButton() is view_btn:
                webbrowser.open(url)

    # ── Close ─────────────────────────────────────────────────────────────────

    def closeEvent(self, event):
        self._save_settings()
        self.on_stop_clicked()
        self.audio_handler.shutdown()
        if self._update_thread is not None and self._update_thread.isRunning():
            self._update_thread.quit()
            self._update_thread.wait(1000)
        event.accept()


# ── Entry point ───────────────────────────────────────────────────────────────

def main():
    from PyQt6.QtWidgets import QApplication
    app = QApplication(sys.argv)
    app.setStyleSheet(STYLE)          # apply dark theme to dialogs too
    window = TimecodeGeneratorWindow()
    window.show()
    sys.exit(app.exec())


if __name__ == "__main__":
    main()
