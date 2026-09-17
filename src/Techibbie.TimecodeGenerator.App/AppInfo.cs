namespace Techibbie.TimecodeGenerator.App;

/// <summary>
/// Build-time constants. AppVersion is patched by the release CI workflow (a plain text
/// replace of the "dev" literal below), mirroring how the Python build injected APP_VERSION.
/// </summary>
public static class AppInfo
{
    public const string AppVersion = "dev";
    public const string GitHubRepo = "libbierose/Techibbie.TimecodeGenerator";
    public const string KofiUrl = "https://ko-fi.com/G2G5IPEXX";
}
