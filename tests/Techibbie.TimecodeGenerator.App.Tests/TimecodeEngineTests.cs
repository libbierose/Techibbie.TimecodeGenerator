using System;
using System.IO;
using Techibbie.TimecodeGenerator.App.Services;
using Techibbie.TimecodeGenerator.App.Views;
using Xunit;

namespace Techibbie.TimecodeGenerator.App.Tests;

/// <summary>
/// Tests the engine's state machine, settings apply/persist, and formatting logic —
/// deliberately without ever letting it touch real audio/MIDI/network I/O:
///   - EnableAudio is forced false immediately after construction, so Start() never
///     opens a real PortAudio LTC stream (that needs actual hardware and isn't
///     something a unit test should depend on).
///   - Tests never set ObsEnabled/MtcEnabled to true, so the background OBS reconnect
///     loop and MTC MIDI output are never actually started (they'd otherwise spawn a
///     real network retry loop or try to open a real MIDI port per test).
///   - SettingsStore.OverrideFilePathForTests points every test at an isolated temp
///     file instead of the real per-user settings.json.
/// What's intentionally NOT covered here: actual LTC/click audio playback, actual MTC
/// MIDI output, and actual OBS WebSocket connectivity — those need real hardware/a
/// real OBS instance and aren't unit-testable in CI.
/// </summary>
public class TimecodeEngineTests : IDisposable
{
    private readonly string _settingsPath;

    public TimecodeEngineTests()
    {
        _settingsPath = Path.Combine(Path.GetTempPath(), $"tcg-engine-tests-{Guid.NewGuid():N}.json");
        SettingsStore.OverrideFilePathForTests = _settingsPath;
    }

    public void Dispose()
    {
        SettingsStore.OverrideFilePathForTests = null;
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
    }

    private static TimecodeEngine CreateEngine()
    {
        var engine = new TimecodeEngine();
        engine.EnableAudio = false; // see class remarks — avoids touching real audio hardware
        return engine;
    }

    [Fact]
    public void NewEngine_StartsStoppedWithZeroedTimecode()
    {
        var engine = CreateEngine();

        Assert.False(engine.IsRunning);
        Assert.False(engine.CanSkip);
        Assert.Equal("00:00:00:00", engine.TimecodeText);
    }

    [Fact]
    public void Start_SetsIsRunningAndCanSkip()
    {
        var engine = CreateEngine();

        engine.Start();

        Assert.True(engine.IsRunning);
        Assert.True(engine.CanSkip);
    }

    [Fact]
    public void Start_IsIdempotentWhileAlreadyRunning()
    {
        var engine = CreateEngine();
        engine.Start();

        engine.Start(); // should just no-op, not throw or restart anything

        Assert.True(engine.IsRunning);
    }

    [Fact]
    public void TogglePlayPause_StartsThenPauses()
    {
        var engine = CreateEngine();

        engine.TogglePlayPause();
        Assert.True(engine.IsRunning);

        engine.TogglePlayPause();
        Assert.False(engine.IsRunning);
        // Paused (not stopped) still keeps skip enabled, per the original app's behavior.
        Assert.True(engine.CanSkip);
    }

    [Fact]
    public void Stop_ClearsRunningAndSkipAndResetsTimecodeText()
    {
        var engine = CreateEngine();
        engine.Start();

        engine.Stop();

        Assert.False(engine.IsRunning);
        Assert.False(engine.CanSkip);
        Assert.Equal("00:00:00:00", engine.TimecodeText);
    }

    [Fact]
    public void SkipBackAndForward_DoNotThrowWhileStoppedOrRunning()
    {
        var engine = CreateEngine();

        // Stopped: no active start time, should be a harmless no-op.
        engine.SkipBack();
        engine.SkipForward();
        Assert.False(engine.CanSkip);

        engine.Start();
        engine.SkipBack();
        engine.SkipForward();
        Assert.True(engine.IsRunning);
        Assert.True(engine.CanSkip);
    }

    [Fact]
    public void ToggleLtc_FlipsIsLtcOnBothWays()
    {
        var engine = CreateEngine();
        var initial = engine.IsLtcOn;

        engine.ToggleLtc();
        Assert.Equal(!initial, engine.IsLtcOn);

        engine.ToggleLtc();
        Assert.Equal(initial, engine.IsLtcOn);
    }

    [Fact]
    public void ToggleUtc_FlipsIsUtcOnBothWays()
    {
        var engine = CreateEngine();
        var initial = engine.IsUtcOn;

        engine.ToggleUtc();
        Assert.Equal(!initial, engine.IsUtcOn);

        engine.ToggleUtc();
        Assert.Equal(initial, engine.IsUtcOn);
    }

    [Fact]
    public void StateChanged_FiresOnStartPauseStopAndToggles()
    {
        var engine = CreateEngine();
        var fireCount = 0;
        engine.StateChanged += () => fireCount++;

        engine.Start();
        engine.TogglePlayPause(); // pause
        engine.Stop();
        engine.ToggleLtc();
        engine.ToggleUtc();

        Assert.Equal(5, fireCount);
    }

    [Fact]
    public void StatusText_ReflectsRunningPausedAndStoppedStates()
    {
        var engine = CreateEngine();
        Assert.StartsWith("Stopped", engine.StatusText);

        engine.Start();
        Assert.StartsWith("Running", engine.StatusText);

        engine.TogglePlayPause(); // pause
        Assert.StartsWith("Paused", engine.StatusText);

        engine.Stop();
        Assert.StartsWith("Stopped", engine.StatusText);
    }

    [Theory]
    [InlineData(30.0, "30")]
    [InlineData(29.97, "29.97")]
    [InlineData(24.0, "24")]
    public void FormatFps_OmitsDecimalsOnlyForWholeNumbers(double fps, string expected)
    {
        var engine = CreateEngine();
        engine.ApplySettings(new SettingsResult(
            fps, null, EnableAudio: false, CheckForUpdates: true,
            ObsEnabled: false, ObsHost: "localhost", ObsPort: 4455, ObsPassword: "",
            MtcEnabled: false, MtcDeviceName: ""));

        Assert.Equal(expected, engine.FormatFps());
    }

    [Fact]
    public void ApplySettings_CopiesAllFieldsAndPersistsAndNotifies()
    {
        var engine = CreateEngine();
        var stateChangedFired = false;
        engine.StateChanged += () => stateChangedFired = true;

        var result = new SettingsResult(
            Fps: 25.0, AudioDeviceId: 7, EnableAudio: true, CheckForUpdates: false,
            ObsEnabled: false, ObsHost: "192.168.1.50", ObsPort: 4444, ObsPassword: "secret",
            MtcEnabled: false, MtcDeviceName: "loopMIDI Port");

        engine.ApplySettings(result);

        Assert.Equal(25.0, engine.TimecodeHandler.Fps);
        Assert.Equal(7, engine.SelectedAudioDeviceId);
        Assert.True(engine.EnableAudio);
        Assert.False(engine.CheckForUpdates);
        Assert.Equal("192.168.1.50", engine.ObsHost);
        Assert.Equal(4444, engine.ObsPort);
        Assert.Equal("secret", engine.ObsPassword);
        Assert.Equal("loopMIDI Port", engine.MtcDeviceName);
        Assert.True(stateChangedFired);
    }

    [Fact]
    public void SaveSettings_ThenLoadingAFreshEngine_RoundTripsAllFields()
    {
        var first = CreateEngine();
        first.ApplySettings(new SettingsResult(
            Fps: 59.94, AudioDeviceId: 3, EnableAudio: true, CheckForUpdates: false,
            ObsEnabled: false, ObsHost: "obs.local", ObsPort: 4455, ObsPassword: "p@ss",
            MtcEnabled: false, MtcDeviceName: "Some MIDI Port"));

        // A brand-new engine reading the same (overridden) settings file should pick up
        // everything the first one saved — this is the actual persistence contract.
        var second = CreateEngine();

        Assert.Equal(59.94, second.TimecodeHandler.Fps);
        Assert.Equal("obs.local", second.ObsHost);
        Assert.Equal(4455, second.ObsPort);
        Assert.Equal("p@ss", second.ObsPassword);
        Assert.Equal("Some MIDI Port", second.MtcDeviceName);
        Assert.False(second.CheckForUpdates);
    }

    [Fact]
    public void SelectedAudioDeviceName_FallsBackWhenNoDeviceMatches()
    {
        var engine = CreateEngine();
        engine.SelectedAudioDeviceId = -12345; // guaranteed not to match any real device

        Assert.Equal("Default device", engine.SelectedAudioDeviceName);
    }
}
