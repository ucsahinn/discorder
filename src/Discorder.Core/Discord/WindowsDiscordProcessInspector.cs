using System.Diagnostics;

namespace Discorder.Core.Discord;

public sealed class WindowsDiscordProcessInspector : IDiscordProcessManager
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
        var processIds = new SortedSet<int>();

        foreach (var processName in ProcessNames)
        {
            foreach (var process in GetProcessesByName(processName))
            {
                using (process)
                {
                    runningCount++;
                    processIds.Add(process.Id);
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
            executablePaths.ToArray(),
            processIds.ToArray());
    }

    public async Task<DiscordRestartResult> RestartAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.HasRunningProcesses)
        {
            return DiscordRestartResult.NotNeeded();
        }

        var effectiveSnapshot = snapshot.ProcessIds.Count > 0
            ? snapshot
            : Capture();
        var processIds = effectiveSnapshot.ProcessIds
            .Distinct()
            .ToArray();
        if (processIds.Length == 0)
        {
            return new DiscordRestartResult(
                true,
                "Discord kapatıldı. Discord'u şimdi açın.");
        }

        var expectedExecutablePaths = effectiveSnapshot.ExecutablePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = new List<RestartTarget>();
        var failures = new List<string>();

        try
        {
            foreach (var processId in processIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Process process;
                try
                {
                    process = Process.GetProcessById(processId);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!TryCreateRestartTarget(
                        process,
                        expectedExecutablePaths,
                        out var target,
                        out var validationError))
                {
                    process.Dispose();
                    if (!string.IsNullOrWhiteSpace(validationError))
                    {
                        failures.Add($"{processId}: {validationError}");
                    }

                    continue;
                }

                targets.Add(target);
            }

            if (failures.Count > 0)
            {
                return new DiscordRestartResult(
                    false,
                    "Discord otomatik yenilenemedi.",
                    string.Join("; ", failures));
            }

            if (targets.Count == 0)
            {
                return new DiscordRestartResult(
                    true,
                    "Discord kapatıldı. Discord'u şimdi açın.");
            }

            var launchPaths = targets
                .Select(target => target.ExecutablePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await StopDiscordProcessAsync(
                        target.Process,
                        gracefulTimeout,
                        cancellationToken);
                }
                catch (Exception exception)
                    when (exception is InvalidOperationException
                        or System.ComponentModel.Win32Exception)
                {
                    failures.Add($"{target.Process.Id}: {exception.Message}");
                }
            }

            if (failures.Count > 0)
            {
                return new DiscordRestartResult(
                    false,
                    "Discord otomatik yenilenemedi.",
                    string.Join("; ", failures));
            }

            foreach (var launchPath in launchPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var started = Process.Start(new ProcessStartInfo
                    {
                        FileName = launchPath,
                        WorkingDirectory = Path.GetDirectoryName(launchPath)!,
                        UseShellExecute = true
                    });
                }
                catch (Exception exception)
                    when (exception is InvalidOperationException
                        or System.ComponentModel.Win32Exception)
                {
                    failures.Add($"{launchPath}: {exception.Message}");
                }
            }

            if (failures.Count > 0)
            {
                return new DiscordRestartResult(
                    false,
                    "Discord kapatıldı ancak yeniden açılamadı.",
                    string.Join("; ", failures));
            }

            return new DiscordRestartResult(
                true,
                launchPaths.Length > 1
                    ? "Discord uygulamaları yenilendi."
                    : "Discord yenilendi.");
        }
        finally
        {
            foreach (var target in targets)
            {
                target.Process.Dispose();
            }
        }
    }

    private static async Task StopDiscordProcessAsync(
        Process process,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken)
    {
        if (process.HasExited)
        {
            return;
        }

        var closeRequested = process.CloseMainWindow();
        if (closeRequested)
        {
            using var timeout = new CancellationTokenSource(gracefulTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken);
        }
    }

    private static bool TryCreateRestartTarget(
        Process process,
        HashSet<string> expectedExecutablePaths,
        out RestartTarget target,
        out string? validationError)
    {
        target = default!;
        validationError = null;

        try
        {
            if (process.HasExited)
            {
                return false;
            }

            if (!IsKnownDiscordProcess(process))
            {
                validationError = "Discord süreci değil.";
                return false;
            }

            var executablePath = TryGetExecutablePath(process);
            if (string.IsNullOrWhiteSpace(executablePath)
                || !File.Exists(executablePath))
            {
                validationError = "Discord yolu doğrulanamadı.";
                return false;
            }

            if (expectedExecutablePaths.Count > 0
                && !expectedExecutablePaths.Contains(executablePath))
            {
                validationError = "Discord yolu değişti.";
                return false;
            }

            target = new RestartTarget(process, executablePath);
            return true;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            validationError = exception.Message;
            return false;
        }
    }

    private static bool IsKnownDiscordProcess(Process process)
    {
        try
        {
            return ProcessNames.Any(processName => string.Equals(
                processName,
                process.ProcessName,
                StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
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

    private sealed record RestartTarget(Process Process, string ExecutablePath);
}
