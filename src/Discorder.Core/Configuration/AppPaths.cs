using Discorder.Core.Updates;

namespace Discorder.Core.Configuration;

public sealed class AppPaths
{
    public AppPaths(string? localAppData = null, string? commonAppData = null)
    {
        var root = localAppData ?? Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var sharedRoot = commonAppData
            ?? localAppData
            ?? Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);

        if (string.IsNullOrWhiteSpace(root)
            || string.IsNullOrWhiteSpace(sharedRoot))
        {
            throw new InvalidOperationException("Yerel uygulama veri klasörü kullanılamıyor.");
        }

        DataDirectory = Path.Combine(root, "Discorder");
        SharedDataDirectory = Path.Combine(sharedRoot, "Discorder");
        UpdateStagingDirectory = Path.Combine(SharedDataDirectory, "updates");
        WireSockInstallerStagingDirectory = Path.Combine(
            SharedDataDirectory,
            "installers");
        ProtectUpdateStaging = localAppData is null;
        ProtectSharedData = localAppData is null && commonAppData is null;
        ToolsDirectory = Path.Combine(SharedDataDirectory, "tools");
        InstallerDirectory = Path.Combine(SharedDataDirectory, "installers");
        ProfileDirectory = Path.Combine(SharedDataDirectory, "profiles");
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

    public string WireSockInstallerStagingDirectory { get; }

    public bool ProtectUpdateStaging { get; }

    public bool ProtectSharedData { get; }

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
        PrepareDirectory(DataDirectory, restrictAccess: false);
        PrepareDirectory(LogDirectory, restrictAccess: false);
        PrepareDirectory(DiagnosticBundleDirectory, restrictAccess: false);
        PrepareDirectory(SharedDataDirectory, ProtectSharedData);
        PrepareDirectory(ToolsDirectory, ProtectSharedData);
        PrepareDirectory(InstallerDirectory, ProtectSharedData);
        PrepareDirectory(ProfileDirectory, ProtectSharedData);
        PrepareDirectory(WireSockInstallerStagingDirectory, ProtectSharedData);
    }

    private static void PrepareDirectory(string directory, bool restrictAccess)
    {
        var fullPath = Path.GetFullPath(directory);
        RejectReparsePointsInExistingAncestors(fullPath);
        Directory.CreateDirectory(fullPath);
        RejectReparsePoint(fullPath);

        if (restrictAccess)
        {
            ProtectedUpdateStaging.RestrictToAdministrators(fullPath);
        }
    }

    private static void RejectReparsePointsInExistingAncestors(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                "Discorder veri yolu çözümlenemedi.");
        }

        var current = root;
        foreach (var part in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (Directory.Exists(current))
            {
                RejectReparsePoint(current);
            }
        }
    }

    private static void RejectReparsePoint(string directory)
    {
        var info = new DirectoryInfo(directory);
        if (info.Exists
            && info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException(
                "Discorder veri klasörü junction veya symlink olamaz.");
        }
    }
}
