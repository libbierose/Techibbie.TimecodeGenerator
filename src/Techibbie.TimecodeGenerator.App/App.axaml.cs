using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Techibbie.TimecodeGenerator.App.Services;
using Techibbie.TimecodeGenerator.App.Views;

namespace Techibbie.TimecodeGenerator.App;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private TrayFlyoutWindow? _trayFlyout;
    private TimecodeEngine? _engine;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // No main window at all — the tray flyout is the entire app UI, so the
            // app only quits via the tray menu's Exit item.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _engine = new TimecodeEngine();
            _trayFlyout = new TrayFlyoutWindow(_engine);
            SetUpTrayIcon(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetUpTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var iconStream = AssetLoader.Open(new Uri("avares://Techibbie.TimecodeGenerator/Assets/Icons/app-icon.png"));

        var menu = new NativeMenu();

        var versionItem = new NativeMenuItem($"Techibbie Timecode Generator {AppInfo.AppVersion}") { IsEnabled = false };
        menu.Add(versionItem);
        menu.Add(new NativeMenuItemSeparator());

        var kofiItem = new NativeMenuItem("Support on Ko-fi");
        kofiItem.Click += (_, _) => OpenUrl(AppInfo.KofiUrl);
        menu.Add(kofiItem);

        var githubItem = new NativeMenuItem("View on GitHub");
        githubItem.Click += (_, _) => OpenUrl($"https://github.com/{AppInfo.GitHubRepo}");
        menu.Add(githubItem);

        var updateItem = new NativeMenuItem("Check for Updates…");
        updateItem.Click += (_, _) => _trayFlyout!.OpenUpdateCheck();
        menu.Add(updateItem);

        menu.Add(new NativeMenuItemSeparator());

        var settingsItem = new NativeMenuItem("Settings…");
        settingsItem.Click += (_, _) => _trayFlyout!.OpenSettings();
        menu.Add(settingsItem);

        menu.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            _engine?.Shutdown();
            desktop.Shutdown();
        };
        menu.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "Techibbie Timecode Generator",
            Menu = menu,
        };
        _trayIcon.Clicked += (_, _) => _trayFlyout!.ToggleVisibility();

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private static void OpenUrl(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start("xdg-open", url);
            }
        }
        catch
        {
            // Opening a browser is best-effort; a failure here shouldn't crash the app.
        }
    }
}
