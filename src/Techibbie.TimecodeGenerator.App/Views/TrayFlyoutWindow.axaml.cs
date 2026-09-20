using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Techibbie.TimecodeGenerator.App.Services;

namespace Techibbie.TimecodeGenerator.App.Views;

/// <summary>
/// The entire app UI: a OneDrive-style borderless popup anchored above the system tray.
/// One window, one acrylic panel, with a "Home" view (live mini timecode + quick
/// transport/LTC/UTC controls) that swaps in-place for Settings / Save-to-WAV / Update-check
/// — there is no separate main window anywhere in this app.
/// </summary>
public partial class TrayFlyoutWindow : Window
{
    private enum View { Home, Settings, SaveWav, UpdateCheck }

    private readonly TimecodeEngine _engine;
    private View _currentView = View.Home;

    // Home view controls (rebuilt each time Home is shown, since they reflect live engine state)
    private TextBlock? _miniTimecodeLabel;
    private TextBlock? _miniStatusLabel;
    private TextBlock? _deviceNameLabel;
    private Button? _skipBackButton;
    private Button? _skipForwardButton;
    private Button? _playPauseButton;
    private Button? _ltcButton;
    private Button? _utcButton;

    public TrayFlyoutWindow(TimecodeEngine engine)
    {
        InitializeComponent();
        _engine = engine;

        Deactivated += (_, _) => { Unsubscribe(); Hide(); };
        // SizeToContent means Height changes with every view swap — keep the popup's
        // bottom-right corner anchored to the tray whenever that happens.
        SizeChanged += (_, _) => PositionAboveTray();

        BuildHomeView();
    }

    // ── Public entry points (tray icon left-click / right-click menu) ────

    /// <summary>Show the flyout on the Home view, or hide it if already open.</summary>
    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Unsubscribe();
            Hide();
            return;
        }

        BuildHomeView();
        OpenAt();
    }

    /// <summary>Open directly into the Settings view (used by the tray right-click menu).</summary>
    public void OpenSettings()
    {
        BuildSettingsView();
        OpenAt();
    }

    /// <summary>Open directly into the update-check view (used by the tray right-click menu).</summary>
    public void OpenUpdateCheck()
    {
        BuildUpdateCheckView();
        OpenAt();
    }

    private void OpenAt()
    {
        Subscribe();
        // Position with last-known size first so there's no visible jump to (0,0); once
        // shown, SizeToContent resolves the real height and the SizeChanged handler
        // (wired in the constructor) re-anchors it precisely.
        PositionAboveTray();
        Show();
        Activate();
    }

    protected override void OnClosed(EventArgs e)
    {
        Unsubscribe();
        base.OnClosed(e);
    }

    // ── View switching ───────────────────────────────────────────────────

    private void SwitchTo(View view)
    {
        switch (view)
        {
            case View.Home: BuildHomeView(); break;
            case View.Settings: BuildSettingsView(); break;
            case View.SaveWav: BuildSaveWavView(); break;
            case View.UpdateCheck: BuildUpdateCheckView(); break;
        }
        // No explicit reposition here — SizeToContent changes Height as the new view's
        // content is measured, which fires the SizeChanged handler wired in the
        // constructor, which re-anchors the popup above the tray automatically.
    }

    private void SetHeader(string title, bool showBack)
    {
        HeaderTitle.Text = title;
        var iconBrush = (IBrush)this.FindResource("IconBrush")!;
        HeaderActionButton.Content = showBack ? "✕" : Icons.Create(IconKind.Settings, iconBrush);
        ToolTip.SetTip(HeaderActionButton, showBack ? "Back" : "Settings");

        HeaderActionButton.Click -= OnHeaderBackClick;
        HeaderActionButton.Click -= OnHeaderSettingsClick;
        HeaderActionButton.Click += showBack ? OnHeaderBackClick : OnHeaderSettingsClick;
    }

    private void OnHeaderBackClick(object? sender, RoutedEventArgs e) => SwitchTo(View.Home);
    private void OnHeaderSettingsClick(object? sender, RoutedEventArgs e) => SwitchTo(View.Settings);

    private void BuildHomeView()
    {
        _currentView = View.Home;
        SetHeader("Techibbie Timecode Generator", showBack: false);

        var iconBrush = (IBrush)this.FindResource("IconBrush")!;
        var primaryBrush = (IBrush)this.FindResource("PrimaryTextBrush")!;
        var iconButtonTheme = (ControlTheme)this.FindResource("IconButton")!;

        _miniTimecodeLabel = new TextBlock
        {
            Text = _engine.TimecodeText, HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 30, FontWeight = FontWeight.SemiBold, Foreground = (IBrush)this.FindResource("TcGreenBrush")!,
        };
        _miniStatusLabel = new TextBlock
        {
            Text = _engine.StatusText, HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 12, Foreground = (IBrush)this.FindResource("StatusTextBrush")!,
        };
        _deviceNameLabel = new TextBlock
        {
            Text = _engine.SelectedAudioDeviceName, HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 11, MaxWidth = 300, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (IBrush)this.FindResource("FaintTextBrush")!,
        };
        ToolTip.SetTip(_deviceNameLabel, _engine.SelectedAudioDeviceName);

        var saveButton = new Button { Theme = iconButtonTheme, Content = Icons.Create(IconKind.Save, iconBrush) };
        ToolTip.SetTip(saveButton, "Save LTC to WAV file");
        saveButton.Click += (_, _) => SwitchTo(View.SaveWav);

        _skipBackButton = new Button { Theme = iconButtonTheme, Content = Icons.Create(IconKind.SkipBack, iconBrush), IsEnabled = _engine.CanSkip };
        ToolTip.SetTip(_skipBackButton, "Skip back 30 frames");
        _skipBackButton.Click += (_, _) => _engine.SkipBack();

        var stopButton = new Button { Theme = iconButtonTheme, Content = Icons.Create(IconKind.Stop, iconBrush) };
        ToolTip.SetTip(stopButton, "Stop");
        stopButton.Click += (_, _) => _engine.Stop();

        _playPauseButton = new Button
        {
            Theme = (ControlTheme)this.FindResource("PlayButton")!, Width = 48, Height = 48,
            Content = Icons.Create(_engine.IsRunning ? IconKind.Pause : IconKind.Play, primaryBrush),
        };
        ToolTip.SetTip(_playPauseButton, "Start / Pause");
        _playPauseButton.Click += (_, _) => _engine.TogglePlayPause();

        _ltcButton = new Button
        {
            Theme = iconButtonTheme,
            Content = Icons.Create(IconKind.Record, _engine.IsLtcOn ? (IBrush)this.FindResource("RecRedBrush")! : iconBrush),
        };
        ToolTip.SetTip(_ltcButton, "Toggle LTC output");
        _ltcButton.Click += (_, _) => _engine.ToggleLtc();

        _utcButton = new Button
        {
            Theme = iconButtonTheme,
            Content = Icons.Create(IconKind.Clock, _engine.IsUtcOn ? (IBrush)this.FindResource("UtcBlueBrush")! : iconBrush),
        };
        ToolTip.SetTip(_utcButton, "Toggle UTC clock / elapsed runtime");
        _utcButton.Click += (_, _) => _engine.ToggleUtc();

        _skipForwardButton = new Button { Theme = iconButtonTheme, Content = Icons.Create(IconKind.SkipForward, iconBrush), IsEnabled = _engine.CanSkip };
        ToolTip.SetTip(_skipForwardButton, "Skip forward 30 frames");
        _skipForwardButton.Click += (_, _) => _engine.SkipForward();

        ViewHost.Content = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                new StackPanel
                {
                    Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { _miniTimecodeLabel, _miniStatusLabel, _deviceNameLabel },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { saveButton, _skipBackButton, stopButton, _playPauseButton, _ltcButton, _utcButton, _skipForwardButton },
                },
            },
        };
    }

    private void BuildSettingsView()
    {
        _currentView = View.Settings;
        SetHeader("Settings", showBack: true);

        var panel = new SettingsPanel(_engine.TimecodeHandler, _engine.AudioDevices, _engine.SelectedAudioDeviceId,
            _engine.EnableAudio, _engine.CheckForUpdates,
            _engine.ObsEnabled, _engine.ObsHost, _engine.ObsPort, _engine.ObsPassword,
            _engine.MtcEnabled, _engine.MtcDeviceName, _engine.MidiOutputDevices);
        panel.Completed += result =>
        {
            if (result is not null) _engine.ApplySettings(result);
            SwitchTo(View.Home);
        };
        ViewHost.Content = panel;
    }

    private void BuildSaveWavView()
    {
        _currentView = View.SaveWav;
        SetHeader("Save to WAV", showBack: true);

        var durationBox = new NumericUpDown { Minimum = 1, Maximum = 3600, Value = 60, Increment = 1 };
        var saveButton = new Button { Content = "Save", Theme = (ControlTheme)this.FindResource("DialogButton")!, IsDefault = true };
        var cancelButton = new Button { Content = "Cancel", Theme = (ControlTheme)this.FindResource("DialogButton")!, IsCancel = true };
        var statusText = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12, Foreground = (IBrush)this.FindResource("SecondaryTextBrush")! };

        cancelButton.Click += (_, _) => SwitchTo(View.Home);
        saveButton.Click += async (_, _) =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Save LTC WAV",
                SuggestedFileName = "ltc_timecode.wav",
                DefaultExtension = "wav",
                FileTypeChoices = new[] { new Avalonia.Platform.Storage.FilePickerFileType("WAV Files") { Patterns = new[] { "*.wav" } } },
            });
            if (file is null) return;

            try
            {
                _engine.ExportWav(file.Path.LocalPath, (double)(durationBox.Value ?? 60));
                statusText.Text = $"Saved: {file.Name}\n{durationBox.Value:0}s · {_engine.LtcRateText} · 48 kHz · starts at 01:00:00:00";
            }
            catch (Exception ex)
            {
                statusText.Text = $"Failed to save WAV:\n{ex.Message}";
            }
        };

        ViewHost.Content = new StackPanel
        {
            Spacing = 16,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 12,
                    Children = { new TextBlock { Text = "Duration (seconds):", VerticalAlignment = VerticalAlignment.Center }, durationBox },
                },
                statusText,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { saveButton, cancelButton },
                },
            },
        };
    }

    private void BuildUpdateCheckView()
    {
        _currentView = View.UpdateCheck;
        SetHeader("Check for Updates", showBack: true);
        ViewHost.Content = new UpdateCheckPanel();
    }

    // ── Live refresh while the Home view is open ────────────────────────

    private void RefreshDigits()
    {
        if (_currentView != View.Home || _miniTimecodeLabel is null) return;
        _miniTimecodeLabel.Text = _engine.TimecodeText;
        _miniStatusLabel!.Text = _engine.StatusText;
    }

    private void RefreshFull()
    {
        if (_currentView != View.Home || _playPauseButton is null) return;
        RefreshDigits();

        var iconBrush = (IBrush)this.FindResource("IconBrush")!;
        var primaryBrush = (IBrush)this.FindResource("PrimaryTextBrush")!;
        _playPauseButton.Content = Icons.Create(_engine.IsRunning ? IconKind.Pause : IconKind.Play, primaryBrush);
        _ltcButton!.Content = Icons.Create(IconKind.Record, _engine.IsLtcOn ? (IBrush)this.FindResource("RecRedBrush")! : iconBrush);
        _utcButton!.Content = Icons.Create(IconKind.Clock, _engine.IsUtcOn ? (IBrush)this.FindResource("UtcBlueBrush")! : iconBrush);
        _skipBackButton!.IsEnabled = _engine.CanSkip;
        _skipForwardButton!.IsEnabled = _engine.CanSkip;

        _deviceNameLabel!.Text = _engine.SelectedAudioDeviceName;
        ToolTip.SetTip(_deviceNameLabel, _engine.SelectedAudioDeviceName);
    }

    private void Subscribe()
    {
        Unsubscribe();
        _engine.StateChanged += RefreshFull;
        _engine.TimecodeTicked += RefreshDigits;
    }

    private void Unsubscribe()
    {
        _engine.StateChanged -= RefreshFull;
        _engine.TimecodeTicked -= RefreshDigits;
    }

    // ── Positioning ──────────────────────────────────────────────────────

    private void PositionAboveTray()
    {
        var screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (screen is null) return;

        const int margin = 12;
        var scaling = screen.Scaling;
        var pixelWidth = (int)(Width * scaling);
        var pixelHeight = (int)(Height * scaling);
        var wa = screen.WorkingArea;

        Position = new PixelPoint(wa.Right - pixelWidth - margin, wa.Bottom - pixelHeight - margin);
    }
}
