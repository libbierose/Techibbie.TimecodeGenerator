using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Techibbie.TimecodeGenerator.App.Services;

public sealed class AppSettings
{
    public double Fps { get; set; } = 30.0;
    public bool LtcMode { get; set; } = true;
    public bool EnableAudio { get; set; } = true;
    public bool UseUtcTime { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public int? AudioDeviceId { get; set; }

    // OBS integration: auto-start/stop LTC when OBS starts/stops streaming or recording.
    public bool ObsEnabled { get; set; }
    public string ObsHost { get; set; } = "localhost";
    public int ObsPort { get; set; } = 4455;
    public string ObsPassword { get; set; } = "";

    // MTC (MIDI Time Code): a second, independent sync output alongside LTC.
    public bool MtcEnabled { get; set; }
    public string MtcDeviceName { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Persists app settings as JSON under the platform's per-user config directory:
/// fps, ltc/audio/UTC/update-check flags, and the selected audio device.
/// </summary>
public static class SettingsStore
{
    /// <summary>
    /// Overrides the settings file path — for tests only, so exercising Save/Load doesn't
    /// clobber the real user's settings.json on the dev/CI machine. Null (the default) uses
    /// the real per-user config path.
    /// </summary>
    public static string? OverrideFilePathForTests { get; set; }

    private static string FilePath
    {
        get
        {
            if (OverrideFilePathForTests is { } overridePath) return overridePath;

            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(baseDir, "Techibbie", "TimecodeGenerator");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path)) return new AppSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Settings persistence is best-effort; a failed save should never crash the app.
        }
    }
}
