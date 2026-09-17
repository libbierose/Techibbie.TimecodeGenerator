using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace Techibbie.TimecodeGenerator.App.Services;

public sealed record UpdateCheckResult(bool UpdateAvailable, string? Tag, string? HtmlUrl, string? AssetUrl);

/// <summary>
/// Checks the GitHub "latest release" API for a newer version, downloads the matching
/// platform asset, and replaces the running executable on relaunch. Mirrors the Python
/// app's updater: Windows can't overwrite its own running .exe, so a detached PowerShell
/// helper waits for this process to exit before swapping the file in; Linux/macOS can
/// safely replace an open file's inode directly.
/// </summary>
public sealed class UpdateService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
        DefaultRequestHeaders = { { "User-Agent", "Techibbie-TimecodeGenerator-UpdateCheck" } },
    };

    public async Task<UpdateCheckResult> CheckAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{AppInfo.GitHubRepo}/releases/latest";
            using var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(false, null, null, null);
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString()?.TrimStart('v') : null;
            var htmlUrl = root.TryGetProperty("html_url", out var urlEl)
                ? urlEl.GetString()
                : $"https://github.com/{AppInfo.GitHubRepo}/releases/latest";

            var assetSuffix = GetAssetSuffix();
            string? assetUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name != null && name.EndsWith(assetSuffix, StringComparison.Ordinal))
                    {
                        assetUrl = asset.TryGetProperty("browser_download_url", out var a) ? a.GetString() : null;
                        break;
                    }
                }
            }

            var isNewer = tag != null && tag != AppInfo.AppVersion && AppInfo.AppVersion != "dev";
            return new UpdateCheckResult(isNewer, tag, htmlUrl, assetUrl);
        }
        catch
        {
            return new UpdateCheckResult(false, null, null, null);
        }
    }

    private static string GetAssetSuffix()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "-Windows.exe";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "-macOS";
        return "-Linux";
    }

    public async Task DownloadAsync(string url, string destPath, IProgress<int>? progress = null)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var dest = File.Create(destPath);

        var buffer = new byte[64 * 1024];
        long downloaded = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read));
            downloaded += read;
            if (total > 0) progress?.Report((int)(downloaded * 100 / total));
        }
        progress?.Report(100);
    }

    /// <summary>Replace the running executable with <paramref name="newExePath"/> and relaunch, then exit this process.</summary>
    public static void ApplyUpdateAndRestart(string newExePath)
    {
        var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Could not determine the running executable path.");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var pid = Environment.ProcessId;
            var scriptPath = Path.Combine(Path.GetTempPath(), $"TcgSwap_{Guid.NewGuid():N}.ps1");
            var script = $$"""
                while (Get-Process -Id {{pid}} -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 200 }
                try {
                    if (Test-Path -LiteralPath '{{currentExe}}') { [System.IO.File]::Delete('{{currentExe}}') }
                    $bytes = [System.IO.File]::ReadAllBytes('{{newExePath}}')
                    [System.IO.File]::WriteAllBytes('{{currentExe}}', $bytes)
                    Remove-Item -LiteralPath '{{newExePath}}' -Force -ErrorAction SilentlyContinue
                    $deadline = (Get-Date).AddSeconds(30)
                    while ((Get-Date) -lt $deadline) {
                        try { $fs = [System.IO.File]::Open('{{currentExe}}', 'Open', 'Read', 'None'); $fs.Close(); break }
                        catch { Start-Sleep -Milliseconds 300 }
                    }
                    Start-Process -FilePath '{{currentExe}}'
                } catch { }
                Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
                """;
            File.WriteAllText(scriptPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NonInteractive -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        else
        {
            File.Copy(newExePath, currentExe, overwrite: true);
            File.Delete(newExePath);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("chmod", $"+x \"{currentExe}\"");
            }
            Process.Start(currentExe);
        }

        Environment.Exit(0);
    }
}
