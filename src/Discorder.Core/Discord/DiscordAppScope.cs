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

    private static readonly string[][] LocalBrowserDirectories =
    [
        ["BraveSoftware", "Brave-Browser", "Application"],
        ["Google", "Chrome", "Application"],
        ["Microsoft", "Edge", "Application"],
        ["Programs", "Opera"],
        ["Programs", "Opera GX"],
        ["Vivaldi", "Application"]
    ];

    private static readonly string[][] ProgramFilesBrowserDirectories =
    [
        ["BraveSoftware", "Brave-Browser", "Application"],
        ["Google", "Chrome", "Application"],
        ["Microsoft", "Edge", "Application"],
        ["Mozilla Firefox"],
        ["Opera"],
        ["Opera GX"],
        ["Vivaldi", "Application"]
    ];

    private readonly string _localAppData;
    private readonly string _programFiles;
    private readonly string _programFilesX86;

    public DiscordAppScope(
        string? localAppData = null,
        string? programFiles = null,
        string? programFilesX86 = null)
    {
        _localAppData = localAppData ?? Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        _programFiles = programFiles ?? Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        _programFilesX86 = programFilesX86 ?? Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);
    }

    public IReadOnlyList<string> GetAllowedApplications()
    {
        var allowed = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var processName in ProcessNames)
        {
            allowed.Add(processName);
        }

        foreach (var browserProcessName in BrowserProcessNames)
        {
            allowed.Add(browserProcessName);
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

            AddExistingDirectories(
                allowed,
                _localAppData,
                LocalBrowserDirectories);
        }

        AddExistingDirectories(
            allowed,
            _programFiles,
            ProgramFilesBrowserDirectories);
        AddExistingDirectories(
            allowed,
            _programFilesX86,
            ProgramFilesBrowserDirectories);

        return allowed.ToArray();
    }

    private static void AddExistingDirectories(
        SortedSet<string> allowed,
        string root,
        IReadOnlyList<string[]> relativeDirectories)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        foreach (var relativeDirectory in relativeDirectories)
        {
            var installationPath = Path.GetFullPath(
                Path.Combine([root, .. relativeDirectory]));

            if (Directory.Exists(installationPath))
            {
                allowed.Add(installationPath);
            }
        }
    }
}
