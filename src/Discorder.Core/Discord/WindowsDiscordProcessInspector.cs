using System.Diagnostics;

namespace Discorder.Core.Discord;

public sealed class WindowsDiscordProcessInspector : IDiscordProcessInspector
{
    private static readonly string[] ProcessNames =
    [
        "Discord",
        "DiscordPTB",
        "DiscordCanary",
        "DiscordDevelopment"
    ];

    public DiscordProcessSnapshot Capture()
    {
        var runningCount = 0;
        var executablePaths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var processName in ProcessNames)
        {
            foreach (var process in GetProcessesByName(processName))
            {
                using (process)
                {
                    runningCount++;
                    var executablePath = TryGetExecutablePath(process);
                    if (!string.IsNullOrWhiteSpace(executablePath))
                    {
                        executablePaths.Add(executablePath);
                    }
                }
            }
        }

        return new DiscordProcessSnapshot(
            runningCount,
            executablePaths.ToArray());
    }

    private static Process[] GetProcessesByName(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or NotSupportedException
                or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
