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

    private readonly string _localAppData;

    public DiscordAppScope(string? localAppData = null)
    {
        _localAppData = localAppData ?? Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
    }

    public IReadOnlyList<string> GetAllowedApplications()
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

        return allowed.ToArray();
    }
}
