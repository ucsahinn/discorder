namespace Discorder.Core.Discord;

public sealed class DiscordAppScope
{
    private static readonly string[] InstallationNames =
    [
        "Discord",
        "DiscordPTB",
        "DiscordCanary",
        "DiscordDevelopment"
    ];

    private static readonly string[] ProcessNames =
    [
        "Discord.exe",
        "DiscordPTB.exe",
        "DiscordCanary.exe",
        "DiscordDevelopment.exe"
    ];

    private static readonly string[] BrowserProcessNames =
    [
        "brave.exe",
        "chrome.exe",
        "firefox.exe",
        "msedge.exe",
        "opera.exe",
        "vivaldi.exe"
    ];

    private readonly string _localAppData;

    public DiscordAppScope(
        string? localAppData = null,
        string? programFiles = null,
        string? programFilesX86 = null)
    {
        _localAppData = localAppData ?? Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
    }

    public IReadOnlyList<string> GetAllowedApplications(bool includeBrowserAccess = false)
    {
        var allowed = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var processName in ProcessNames)
        {
            allowed.Add(processName);
        }

        if (!string.IsNullOrWhiteSpace(_localAppData))
        {
            foreach (var installationName in InstallationNames)
            {
                var installationPath = Path.GetFullPath(
                    Path.Combine(_localAppData, installationName));

                if (Directory.Exists(installationPath))
                {
                    allowed.Add(installationPath);
                }
            }
        }

        if (includeBrowserAccess)
        {
            foreach (var browserProcessName in BrowserProcessNames)
            {
                allowed.Add(browserProcessName);
            }
        }

        return allowed.ToArray();
    }
}
