using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Techibbie.TimecodeGenerator.App.Services;

namespace Techibbie.TimecodeGenerator.App.Views;

/// <summary>Checks GitHub for a newer release and offers to download/apply it — the flyout's "Check for Updates" view.</summary>
public partial class UpdateCheckPanel : UserControl
{
    private readonly UpdateService _service = new();

    public UpdateCheckPanel()
    {
        InitializeComponent();
        VersionLabel.Text = $"Version {AppInfo.AppVersion}";
        _ = RunCheckAsync();
    }

    private async Task RunCheckAsync()
    {
        ShowStatus("Checking for updates…");

        var result = await _service.CheckAsync();

        if (!result.UpdateAvailable)
        {
            ShowStatus("You're up to date.", withRetry: true);
            return;
        }

        if (string.IsNullOrEmpty(result.AssetUrl))
        {
            ShowPrompt($"Version {result.Tag} is available. No installer was found for your platform.",
                "Open Releases Page",
                () => OpenUrl(result.HtmlUrl ?? $"https://github.com/{AppInfo.GitHubRepo}/releases/latest"));
            return;
        }

        ShowPrompt($"Version {result.Tag} is available.", "Update Now", async () =>
        {
            try
            {
                var suffix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
                var tempPath = Path.Combine(Path.GetTempPath(), $"TcgUpdate_{Guid.NewGuid():N}{suffix}");
                await _service.DownloadAsync(result.AssetUrl, tempPath);
                UpdateService.ApplyUpdateAndRestart(tempPath);
            }
            catch (Exception ex)
            {
                ShowStatus($"Download failed: {ex.Message}", withRetry: true);
            }
        });
    }

    private void ShowStatus(string message, bool withRetry = false)
    {
        StatusArea.Children.Clear();
        StatusArea.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (IBrush)this.FindResource("SecondaryTextBrush")!,
        });

        if (withRetry)
        {
            var retry = new Button
            {
                Content = "Check Again",
                Theme = (ControlTheme)this.FindResource("DialogButton")!,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            retry.Click += async (_, _) => await RunCheckAsync();
            StatusArea.Children.Add(retry);
        }
    }

    private void ShowPrompt(string message, string actionLabel, Action onAction)
    {
        var actionButton = new Button
        {
            Content = actionLabel,
            Theme = (ControlTheme)this.FindResource("KofiButton")!,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        var laterButton = new Button
        {
            Content = "Later",
            Theme = (ControlTheme)this.FindResource("DialogButton")!,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        actionButton.Click += (_, _) => onAction();
        laterButton.Click += (_, _) => ShowStatus("Update available — dismissed.", withRetry: true);

        StatusArea.Children.Clear();
        StatusArea.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (IBrush)this.FindResource("SecondaryTextBrush")!,
        });
        StatusArea.Children.Add(actionButton);
        StatusArea.Children.Add(laterButton);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                System.Diagnostics.Process.Start("open", url);
            }
            else
            {
                System.Diagnostics.Process.Start("xdg-open", url);
            }
        }
        catch
        {
            // Opening a browser is best-effort; a failure here shouldn't crash the app.
        }
    }
}
