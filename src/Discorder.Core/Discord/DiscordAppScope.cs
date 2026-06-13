namespace Discorder.Core.Discord;

public sealed class DiscordAppScope
{
    private static readonly (string InstallationName, string ProcessName)[] DiscordInstallations =
    [
        ("Discord", "Discord.exe"),
        ("DiscordPTB", "DiscordPTB.exe"),
        ("DiscordCanary", "DiscordCanary.exe"),
        ("DiscordDevelopment", "DiscordDevelopment.exe")
    ];

    private static readonly BrowserDefinition[] BrowserDefinitions =
    [
        new(
            "brave.exe",
            [
                @"BraveSoftware\Brave-Browser\Application\brave.exe"
            ]),
        new(
            "chrome.exe",
            [
                @"Google\Chrome\Application\chrome.exe"
            ]),
        new(
            "firefox.exe",
            [
                @"Mozilla Firefox\firefox.exe"
            ]),
        new(
            "msedge.exe",
            [
                @"Microsoft\Edge\Application\msedge.exe"
            ]),
        new(
            "opera.exe",
            [
                @"Opera\opera.exe",
                @"Programs\Opera\opera.exe",
                @"Programs\Opera GX\opera.exe"
            ]),
        new(
            "vivaldi.exe",
            [
                @"Vivaldi\Application\vivaldi.exe"
            ])
    ];

    private readonly string _localAppData;
    private readonly string? _programFiles;
    private readonly string? _programFilesX86;

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

    public IReadOnlyList<string> GetAllowedApplications(bool includeBrowserAccess = false)
    {
        var allowed = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (installationName, processName) in DiscordInstallations)
        {
            allowed.Add(processName);
            if (!string.IsNullOrWhiteSpace(_localAppData))
            {
                var installationPath = Path.GetFullPath(
                    Path.Combine(_localAppData, installationName));

                AddDiscordInstallation(
                    allowed,
                    installationPath,
                    processName);
            }
        }

        if (includeBrowserAccess)
        {
            foreach (var browser in BrowserDefinitions)
            {
                allowed.Add(browser.ProcessName);
                AddKnownBrowserPaths(allowed, browser);
            }
        }

        return allowed.ToArray();
    }

    private static bool AddDiscordInstallation(
        SortedSet<string> allowed,
        string installationPath,
        string processName)
    {
        if (!IsSafeExistingDirectory(installationPath))
        {
            return false;
        }

        allowed.Add(installationPath);
        var executableFound = AddFileIfExists(
            allowed,
            Path.Combine(installationPath, processName));

        foreach (var applicationDirectory in EnumerateSafeDirectories(
                     installationPath,
                     "app-*"))
        {
            executableFound |= AddFileIfExists(
                allowed,
                Path.Combine(applicationDirectory, processName));
        }

        return executableFound;
    }

    private int AddKnownBrowserPaths(
        SortedSet<string> allowed,
        BrowserDefinition browser)
    {
        var count = 0;
        foreach (var root in EnumerateProgramRoots())
        {
            foreach (var relativePath in browser.RelativeExecutablePaths)
            {
                if (AddFileIfExists(allowed, Path.Combine(root, relativePath)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private IEnumerable<string> EnumerateProgramRoots()
    {
        if (!string.IsNullOrWhiteSpace(_localAppData))
        {
            yield return _localAppData;
        }

        if (!string.IsNullOrWhiteSpace(_programFiles))
        {
            yield return _programFiles;
        }

        if (!string.IsNullOrWhiteSpace(_programFilesX86)
            && !string.Equals(_programFilesX86, _programFiles, StringComparison.OrdinalIgnoreCase))
        {
            yield return _programFilesX86;
        }
    }

    private static string[] EnumerateSafeDirectories(
        string root,
        string searchPattern)
    {
        try
        {
            return Directory
                .EnumerateDirectories(root, searchPattern, SearchOption.TopDirectoryOnly)
                .Where(IsSafeExistingDirectory)
                .ToArray();
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool AddFileIfExists(SortedSet<string> allowed, string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                return false;
            }

            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            allowed.Add(fullPath);
            return true;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsSafeExistingDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return false;
        }
    }

    private sealed record BrowserDefinition(
        string ProcessName,
        IReadOnlyList<string> RelativeExecutablePaths);
}
