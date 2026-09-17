using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Techibbie.TimecodeGenerator.App.Services;
using Techibbie.TimecodeGenerator.Audio;
using Techibbie.TimecodeGenerator.Core.Timecode;

namespace Techibbie.TimecodeGenerator.App.Views;

public sealed record SettingsResult(
    double Fps, int? AudioDeviceId, bool EnableAudio, bool CheckForUpdates,
    bool ObsEnabled, string ObsHost, int ObsPort, string ObsPassword,
    bool MtcEnabled, string MtcDeviceName);

public partial class SettingsPanel : UserControl
{
    /// <summary>Fires once with the chosen settings, or null if the panel was cancelled/closed.</summary>
    public event Action<SettingsResult?>? Completed;

    // Parameterless constructor required by the Avalonia XAML loader/previewer.
    public SettingsPanel() : this(new TimecodeHandler(30.0), Array.Empty<AudioDeviceInfo>(), null, true, true,
        false, "localhost", 4455, "", false, "", Array.Empty<string>())
    {
    }

    public SettingsPanel(TimecodeHandler currentTimecode, IReadOnlyList<AudioDeviceInfo> devices, int? selectedDeviceId,
        bool enableAudio, bool checkForUpdates,
        bool obsEnabled, string obsHost, int obsPort, string obsPassword,
        bool mtcEnabled, string mtcDeviceName, IReadOnlyList<string> midiDevices)
    {
        InitializeComponent();

        var supportedFps = currentTimecode.GetSupportedFps();
        foreach (var fps in supportedFps)
        {
            FpsCombo.Items.Add(new FpsItem(fps));
        }
        FpsCombo.SelectedIndex = Math.Max(0, supportedFps.ToList().FindIndex(f => f == currentTimecode.Fps));

        foreach (var dev in devices)
        {
            DeviceCombo.Items.Add(new DeviceItem(dev));
        }
        var idx = devices.ToList().FindIndex(d => d.Id == selectedDeviceId);
        DeviceCombo.SelectedIndex = idx >= 0 ? idx : (devices.Count > 0 ? 0 : -1);

        DeviceCombo.SelectionChanged += (_, _) => RefreshSampleRateHint();
        RefreshSampleRateHint();

        AudioCheck.IsChecked = enableAudio;
        UpdateCheck.IsChecked = checkForUpdates;

        StartWithWindowsCheck.IsChecked = StartupService.IsEnabled();
        if (!StartupService.IsSupported)
        {
            StartWithWindowsCheck.IsEnabled = false;
            StartWithWindowsCheck.Content = "Start automatically at login (Windows only)";
        }

        ObsEnabledCheck.IsChecked = obsEnabled;
        ObsHostBox.Text = obsHost;
        ObsPortBox.Text = obsPort.ToString();
        ObsPasswordBox.Text = obsPassword;
        ObsFieldsPanel.IsEnabled = obsEnabled;
        ObsEnabledCheck.IsCheckedChanged += (_, _) => ObsFieldsPanel.IsEnabled = ObsEnabledCheck.IsChecked ?? false;

        MtcEnabledCheck.IsChecked = mtcEnabled;
        foreach (var name in midiDevices)
        {
            MtcDeviceCombo.Items.Add(name);
        }
        MtcDeviceCombo.SelectedIndex = midiDevices.ToList().IndexOf(mtcDeviceName) is var mi && mi >= 0
            ? mi
            : (midiDevices.Count > 0 ? 0 : -1);
        if (midiDevices.Count == 0)
        {
            MtcNoDevicesHint.Text = "No MIDI output devices found. Install a virtual MIDI port (e.g. loopMIDI) to use MTC.";
            MtcEnabledCheck.IsEnabled = false;
        }
        MtcFieldsPanel.IsEnabled = mtcEnabled;
        MtcEnabledCheck.IsCheckedChanged += (_, _) => MtcFieldsPanel.IsEnabled = MtcEnabledCheck.IsChecked ?? false;

        OkButton.Click += (_, _) =>
        {
            StartupService.SetEnabled(StartWithWindowsCheck.IsChecked ?? false);
            Completed?.Invoke(BuildResult());
        };
        CancelButton.Click += (_, _) => Completed?.Invoke(null);
    }

    private void RefreshSampleRateHint()
    {
        if (DeviceCombo.SelectedItem is DeviceItem item)
        {
            var sr = (int)item.Device.SampleRate;
            SampleRateHint.Text = sr != 48000
                ? $"Device native rate: {sr} Hz — LTC will be generated at this rate."
                : "";
        }
        else
        {
            SampleRateHint.Text = "";
        }
    }

    private SettingsResult BuildResult()
    {
        var fps = FpsCombo.SelectedItem is FpsItem f ? f.Fps : 30.0;
        var deviceId = DeviceCombo.SelectedItem is DeviceItem d ? d.Device.Id : (int?)null;
        var obsPort = int.TryParse(ObsPortBox.Text, out var p) ? p : 4455;
        var mtcDevice = MtcDeviceCombo.SelectedItem as string ?? "";
        return new SettingsResult(
            fps, deviceId, AudioCheck.IsChecked ?? true, UpdateCheck.IsChecked ?? true,
            ObsEnabledCheck.IsChecked ?? false, ObsHostBox.Text ?? "localhost", obsPort, ObsPasswordBox.Text ?? "",
            MtcEnabledCheck.IsChecked ?? false, mtcDevice);
    }

    private sealed record FpsItem(double Fps)
    {
        public override string ToString() => Fps == Math.Floor(Fps) ? ((long)Fps).ToString() : Fps.ToString("0.##");
    }

    private sealed record DeviceItem(AudioDeviceInfo Device)
    {
        public override string ToString() => $"{Device.Name}  ({Device.Channels}ch · {(int)Device.SampleRate} Hz)";
    }
}
