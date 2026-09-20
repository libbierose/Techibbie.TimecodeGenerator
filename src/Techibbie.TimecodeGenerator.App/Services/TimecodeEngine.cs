using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Techibbie.TimecodeGenerator.App.Views;
using Techibbie.TimecodeGenerator.Audio;
using Techibbie.TimecodeGenerator.Core.Ltc;
using Techibbie.TimecodeGenerator.Core.Timecode;

namespace Techibbie.TimecodeGenerator.App.Services;

/// <summary>
/// Owns all playback/LTC/UTC/audio-device state and the timer loop. Headless — there is
/// no window here at all; the tray flyout is the only UI, so this class exposes plain
/// state + events for it to render rather than touching any Avalonia visual APIs itself.
/// </summary>
public sealed class TimecodeEngine
{
    private readonly AudioEngine _audioEngine = new();
    private readonly DispatcherTimer _timer;
    private readonly ObsWebSocketClient _obsClient = new();
    private readonly MtcGenerator _mtcGenerator = new();

    private TimecodeHandler _timecodeHandler = new(30.0);
    private IReadOnlyList<AudioDeviceInfo> _audioDevices = Array.Empty<AudioDeviceInfo>();

    private bool _isRunning;
    private DateTime? _startTime;
    private long _lastFrame = -1;
    private string _timecodeText = "00:00:00:00";

    // OBS auto-start/stop tracks stream and record independently: LTC starts as soon as
    // either one goes active, and only stops once BOTH are inactive, so e.g. stopping the
    // stream while a recording is still running doesn't cut LTC out from under it.
    private bool _obsStreaming;
    private bool _obsRecording;

    /// <summary>Fires on discrete state transitions (play/pause/stop/LTC/UTC) — subscribe for a full UI refresh.</summary>
    public event Action? StateChanged;

    /// <summary>Fires every timer tick while running — cheap, text-only digit refresh.</summary>
    public event Action? TimecodeTicked;

    public bool IsRunning => _isRunning;
    /// <summary>True whenever there's an active or paused run to nudge — mirrors the original app's back/forward buttons being enabled from Start() through Stop(), including while paused.</summary>
    public bool CanSkip => _startTime != null;
    public bool IsLtcOn { get; private set; }
    public bool IsUtcOn { get; private set; }
    public bool EnableAudio { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public string TimecodeText => _timecodeText;
    public TimecodeHandler TimecodeHandler => _timecodeHandler;
    public IReadOnlyList<AudioDeviceInfo> AudioDevices => _audioDevices;
    public int? SelectedAudioDeviceId { get; set; }
    public string SelectedAudioDeviceName =>
        _audioDevices.FirstOrDefault(d => d.Id == SelectedAudioDeviceId)?.Name ?? "Default device";

    public bool ObsEnabled { get; set; }
    public string ObsHost { get; set; } = "localhost";
    public int ObsPort { get; set; } = 4455;
    public string ObsPassword { get; set; } = "";
    public bool ObsConnected { get; private set; }

    public bool MtcEnabled { get; set; }
    public string MtcDeviceName { get; set; } = "";
    public IReadOnlyList<string> MidiOutputDevices => MtcGenerator.GetOutputDeviceNames();

    public string StatusText
    {
        get
        {
            var drop = _timecodeHandler.IsDropFrame() ? "DF" : "NDF";
            var state = _isRunning ? "Running" : _startTime != null ? "Paused" : "Stopped";
            return $"{state}  ·  {drop}";
        }
    }

    public TimecodeEngine()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
        _timer.Tick += (_, _) => UpdateTimecode();

        _obsClient.StreamStateChanged += active => Dispatcher.UIThread.Post(() => OnObsOutputStateChanged(isStream: true, active));
        _obsClient.RecordStateChanged += active => Dispatcher.UIThread.Post(() => OnObsOutputStateChanged(isStream: false, active));
        _obsClient.ConnectionChanged += connected => Dispatcher.UIThread.Post(() =>
        {
            ObsConnected = connected;
            StateChanged?.Invoke();
        });

        RefreshDevices();
        LoadSettings();
        ApplyObsConnectionState();
    }

    private void OnObsOutputStateChanged(bool isStream, bool active)
    {
        if (isStream) _obsStreaming = active; else _obsRecording = active;

        if (active)
        {
            Start();
        }
        else if (!_obsStreaming && !_obsRecording)
        {
            Stop();
        }
    }

    private void ApplyObsConnectionState()
    {
        if (ObsEnabled)
        {
            _obsClient.Start(ObsHost, ObsPort, ObsPassword);
        }
        else
        {
            _obsClient.Stop();
            _obsStreaming = false;
            _obsRecording = false;
            ObsConnected = false;
        }
    }

    private void RefreshDevices()
    {
        _audioDevices = _audioEngine.GetOutputDevices();
        if (_audioDevices.Count > 0 && SelectedAudioDeviceId == null)
        {
            // With nothing saved yet, prefer a "Digital" output (e.g. a motherboard's
            // S/PDIF out) over whatever device happens to be first — that's rarely
            // anyone's actual headphones/speakers, so a first-run default of "just
            // start playing LTC" doesn't risk blasting a buzzing tone into someone's
            // ears. Falls back to the first device if nothing matches.
            var digital = _audioDevices.FirstOrDefault(d => d.Name.Contains("digital", StringComparison.OrdinalIgnoreCase));
            SelectedAudioDeviceId = (digital ?? _audioDevices[0]).Id;
        }
    }

    // ── Settings persistence ─────────────────────────────────────────────

    public void SaveSettings()
    {
        var s = new AppSettings
        {
            Fps = _timecodeHandler.Fps,
            LtcMode = IsLtcOn,
            EnableAudio = EnableAudio,
            UseUtcTime = IsUtcOn,
            CheckForUpdates = CheckForUpdates,
            AudioDeviceId = SelectedAudioDeviceId,
            ObsEnabled = ObsEnabled,
            ObsHost = ObsHost,
            ObsPort = ObsPort,
            ObsPassword = ObsPassword,
            MtcEnabled = MtcEnabled,
            MtcDeviceName = MtcDeviceName,
        };
        SettingsStore.Save(s);
    }

    private void LoadSettings()
    {
        var s = SettingsStore.Load();

        _timecodeHandler = new TimecodeHandler(s.Fps);

        if (s.AudioDeviceId is { } deviceId && _audioDevices.Any(d => d.Id == deviceId))
        {
            SelectedAudioDeviceId = deviceId;
        }

        IsLtcOn = s.LtcMode;
        EnableAudio = s.EnableAudio;
        IsUtcOn = s.UseUtcTime;
        CheckForUpdates = s.CheckForUpdates;
        ObsEnabled = s.ObsEnabled;
        ObsHost = s.ObsHost;
        ObsPort = s.ObsPort;
        ObsPassword = s.ObsPassword;
        MtcEnabled = s.MtcEnabled;
        MtcDeviceName = s.MtcDeviceName;
    }

    public void ApplySettings(SettingsResult result)
    {
        if (result.Fps != _timecodeHandler.Fps)
        {
            _timecodeHandler = new TimecodeHandler(result.Fps);
        }
        SelectedAudioDeviceId = result.AudioDeviceId;
        EnableAudio = result.EnableAudio;
        CheckForUpdates = result.CheckForUpdates;

        var obsSettingsChanged = ObsEnabled != result.ObsEnabled || ObsHost != result.ObsHost
            || ObsPort != result.ObsPort || ObsPassword != result.ObsPassword;
        ObsEnabled = result.ObsEnabled;
        ObsHost = result.ObsHost;
        ObsPort = result.ObsPort;
        ObsPassword = result.ObsPassword;
        if (obsSettingsChanged) ApplyObsConnectionState();

        var mtcSettingsChanged = MtcEnabled != result.MtcEnabled || MtcDeviceName != result.MtcDeviceName;
        MtcEnabled = result.MtcEnabled;
        MtcDeviceName = result.MtcDeviceName;
        if (mtcSettingsChanged && _isRunning)
        {
            if (MtcEnabled && !string.IsNullOrEmpty(MtcDeviceName)) StartOrRestartMtc();
            else _mtcGenerator.Stop();
        }

        SaveSettings();
        StateChanged?.Invoke();
    }

    // ── Playback ─────────────────────────────────────────────────────────

    public void TogglePlayPause()
    {
        if (_isRunning) Pause(); else Start();
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _startTime = DateTime.Now;
        _timer.Start();

        if (EnableAudio && IsLtcOn)
        {
            StartLtcStreamFromNow();
        }
        if (MtcEnabled && !string.IsNullOrEmpty(MtcDeviceName))
        {
            StartOrRestartMtc();
        }
        StateChanged?.Invoke();
    }

    private void Pause()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _timer.Stop();
        _audioEngine.StopLtcStream();
        _mtcGenerator.Stop();
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        _isRunning = false;
        _timer.Stop();
        _audioEngine.StopLtcStream();
        _mtcGenerator.Stop();
        _startTime = null;
        _lastFrame = -1;
        _timecodeText = "00:00:00:00";
        StateChanged?.Invoke();
    }

    public void SkipBack()
    {
        if (_startTime is { } t) _startTime = t.AddSeconds(30 / _timecodeHandler.Fps);
        StateChanged?.Invoke();
    }

    public void SkipForward()
    {
        if (_startTime is { } t) _startTime = t.AddSeconds(-30 / _timecodeHandler.Fps);
        StateChanged?.Invoke();
    }

    public void ToggleLtc()
    {
        IsLtcOn = !IsLtcOn;

        if (_isRunning && EnableAudio)
        {
            if (IsLtcOn) StartLtcStreamFromNow();
            else _audioEngine.StopLtcStream();
        }
        SaveSettings();
        StateChanged?.Invoke();
    }

    public void ToggleUtc()
    {
        IsUtcOn = !IsUtcOn;

        // Unlike toggling LTC, this never clears _startTime while running — the elapsed
        // clock underneath keeps advancing regardless of which one is displayed. Restart
        // the LTC/MTC streams so they re-seed from the right base (UTC now vs. elapsed zero).
        if (_isRunning)
        {
            if (EnableAudio && IsLtcOn) StartLtcStreamFromNow();
            if (MtcEnabled && !string.IsNullOrEmpty(MtcDeviceName)) StartOrRestartMtc();
        }
        SaveSettings();
        StateChanged?.Invoke();
    }

    private (int Hour, int Minute, int Second, int Frame) GetStreamStartTimecode()
    {
        if (!IsUtcOn) return (0, 0, 0, 0);
        var now = DateTime.UtcNow;
        var frame = (int)(now.Millisecond / 1000.0 * _timecodeHandler.Fps);
        return (now.Hour, now.Minute, now.Second, frame);
    }

    private void StartLtcStreamFromNow()
    {
        var (hh, mm, ss, ff) = GetStreamStartTimecode();
        _audioEngine.StartLtcStream(hh, mm, ss, ff, _timecodeHandler.Fps, _timecodeHandler.IsDropFrame(), SelectedAudioDeviceId);
    }

    private void StartOrRestartMtc()
    {
        var (hh, mm, ss, ff) = GetStreamStartTimecode();
        _mtcGenerator.Start(MtcDeviceName, hh, mm, ss, ff, _timecodeHandler.Fps, _timecodeHandler.IsDropFrame());
    }

    private void UpdateTimecode()
    {
        if (!_isRunning || _startTime is not { } start) return;

        var elapsed = (DateTime.Now - start).TotalSeconds;
        var baseTc = _timecodeHandler.ElapsedTimeToTimecode(elapsed);

        if (IsUtcOn)
        {
            var now = DateTime.UtcNow;
            var frame = (int)(now.Millisecond / 1000.0 * _timecodeHandler.Fps);
            _timecodeText = $"{now.Hour:D2}:{now.Minute:D2}:{now.Second:D2}:{frame:D2}";
        }
        else
        {
            _timecodeText = baseTc;
        }

        var currentFrame = (long)(elapsed * _timecodeHandler.Fps);
        if (EnableAudio && currentFrame != _lastFrame)
        {
            if (!IsLtcOn)
            {
                _audioEngine.PlayFrameClick((int)currentFrame, _timecodeHandler.Fps, SelectedAudioDeviceId);
            }
            _lastFrame = currentFrame;
        }

        TimecodeTicked?.Invoke();
    }

    // ── WAV export ───────────────────────────────────────────────────────

    public void ExportWav(string path, double durationSeconds)
    {
        var fps = _timecodeHandler.Fps;
        var drop = _timecodeHandler.IsDropFrame();
        var gen = new LtcGenerator(48000);
        var audio = gen.GenerateContinuousLtc(1, 0, 0, 0, fps, drop, durationSeconds);
        Core.Wav.WavWriter.WriteMono16(path, audio, 48000);
    }

    /// <summary>What LTC/MTC is really transmitted at for the current frame rate, e.g. "30 fps LTC" or "29.97 fps LTC (drop-frame)".</summary>
    public string LtcRateText
    {
        get
        {
            var rate = LtcRate.FromFrameRate(_timecodeHandler.Fps, _timecodeHandler.IsDropFrame());
            var fps = rate.DropFrame ? "29.97" : rate.ExactFps.ToString("0.###");
            return $"{fps} fps LTC{(rate.DropFrame ? " (drop-frame)" : "")}";
        }
    }

    public string FormatFps() => _timecodeHandler.Fps == Math.Floor(_timecodeHandler.Fps)
        ? ((long)_timecodeHandler.Fps).ToString()
        : _timecodeHandler.Fps.ToString("0.##");

    // ── Lifecycle ────────────────────────────────────────────────────────

    public void Shutdown()
    {
        SaveSettings();
        Stop();
        _obsClient.Stop();
        _mtcGenerator.Dispose();
        _audioEngine.Shutdown();
    }
}
