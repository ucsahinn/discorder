namespace Discorder.Core.Configuration;

public sealed class AppPaths
{
    public AppPaths(string? localAppData = null)
    {
        var root = localAppData ?? Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("Yerel uygulama veri klasörü kullanılamıyor.");
        }

        DataDirectory = Path.Combine(root, "Discorder");
        ToolsDirectory = Path.Combine(DataDirectory, "tools");
        InstallerDirectory = Path.Combine(DataDirectory, "installers");
        ProfileDirectory = Path.Combine(DataDirectory, "profiles");
        LogDirectory = Path.Combine(DataDirectory, "logs");
        SettingsFile = Path.Combine(DataDirectory, "settings.json");
        WgcfExecutable = Path.Combine(ToolsDirectory, "wgcf.exe");
        WgcfAccount = Path.Combine(ProfileDirectory, "wgcf-account.toml");
        WgcfBaseProfile = Path.Combine(ProfileDirectory, "wgcf-profile.conf");
        DiscordProfile = Path.Combine(ProfileDirectory, "discord.conf");
        TunnelLog = Path.Combine(LogDirectory, "tunnel.log");
        ErrorLog = Path.Combine(LogDirectory, "errors.log");
    }

    public string DataDirectory { get; }

    public string ToolsDirectory { get; }

    public string InstallerDirectory { get; }

    public string ProfileDirectory { get; }

    public string LogDirectory { get; }

    public string SettingsFile { get; }

    public string WgcfExecutable { get; }

    public string WgcfAccount { get; }

    public string WgcfBaseProfile { get; }

    public string DiscordProfile { get; }

    public string TunnelLog { get; }

    public string ErrorLog { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ToolsDirectory);
        Directory.CreateDirectory(InstallerDirectory);
        Directory.CreateDirectory(ProfileDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
