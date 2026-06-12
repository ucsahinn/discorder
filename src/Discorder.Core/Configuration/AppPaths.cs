namespace Discorder.Core.Configuration;

public sealed class AppPaths
{
    public AppPaths(string? localAppData = null)
    {
        var root = localAppData ?? Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var sharedRoot = localAppData ?? Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);

        if (string.IsNullOrWhiteSpace(root)
            || string.IsNullOrWhiteSpace(sharedRoot))
        {
            throw new InvalidOperationException("Yerel uygulama veri klasörü kullanılamıyor.");
        }

        DataDirectory = Path.Combine(root, "Discorder");
        SharedDataDirectory = Path.Combine(sharedRoot, "Discorder");
        UpdateStagingDirectory = Path.Combine(SharedDataDirectory, "updates");
        ProtectUpdateStaging = localAppData is null;
        ToolsDirectory = Path.Combine(DataDirectory, "tools");
        InstallerDirectory = Path.Combine(DataDirectory, "installers");
        ProfileDirectory = Path.Combine(DataDirectory, "profiles");
        LogDirectory = Path.Combine(DataDirectory, "logs");
        DiagnosticBundleDirectory = Path.Combine(DataDirectory, "diagnostic-bundles");
        SettingsFile = Path.Combine(DataDirectory, "settings.json");
        WireSockInstallMarker = Path.Combine(DataDirectory, "wiresock-installed-by-discorder.marker");
        WgcfExecutable = Path.Combine(ToolsDirectory, "wgcf.exe");
        WgcfAccount = Path.Combine(ProfileDirectory, "wgcf-account.toml");
        WgcfBaseProfile = Path.Combine(ProfileDirectory, "wgcf-profile.conf");
        DiscordProfile = Path.Combine(ProfileDirectory, "discord.conf");
        TunnelLog = Path.Combine(LogDirectory, "tunnel.log");
        ErrorLog = Path.Combine(LogDirectory, "errors.log");
        EventLog = Path.Combine(LogDirectory, "events.jsonl");
        HealthReport = Path.Combine(LogDirectory, "health.json");
        DiagnosticSummary = Path.Combine(LogDirectory, "diagnostics.md");
    }

    public string DataDirectory { get; }

    public string SharedDataDirectory { get; }

    public string UpdateStagingDirectory { get; }

    public bool ProtectUpdateStaging { get; }

    public string ToolsDirectory { get; }

    public string InstallerDirectory { get; }

    public string ProfileDirectory { get; }

    public string LogDirectory { get; }

    public string DiagnosticBundleDirectory { get; }

    public string SettingsFile { get; }

    public string WireSockInstallMarker { get; }

    public string WgcfExecutable { get; }

    public string WgcfAccount { get; }

    public string WgcfBaseProfile { get; }

    public string DiscordProfile { get; }

    public string TunnelLog { get; }

    public string ErrorLog { get; }

    public string EventLog { get; }

    public string HealthReport { get; }

    public string DiagnosticSummary { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ToolsDirectory);
        Directory.CreateDirectory(InstallerDirectory);
        Directory.CreateDirectory(ProfileDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(DiagnosticBundleDirectory);
    }
}
