using Discorder.Core.Security;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Discorder.Core.Discord;

public sealed class WindowsDiscordProcessInspector : IDiscordProcessManager
{
    private const string ExpectedPublisher = "Discord Inc.";
    private const int CsidlDesktop = 0;
    private const int ShowNormal = 1;
    private const int SwcDesktop = 8;
    private const int SwfoNeedDispatch = 1;
    private const int RestoreWindow = 9;

    private static readonly Guid ShellWindowsClsid = new(
        "9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly TimeSpan LaunchVerificationTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan LaunchVerificationInterval = TimeSpan.FromMilliseconds(300);
    private static readonly string[] ProcessNames =
    [
        "Discord",
        "DiscordPTB",
        "DiscordCanary",
        "DiscordDevelopment"
    ];

    private static readonly InstallationSpec[] KnownInstallations =
    [
        new("Discord", "Discord.exe", "Discord"),
        new("DiscordPTB", "DiscordPTB.exe", "DiscordPTB"),
        new("DiscordCanary", "DiscordCanary.exe", "DiscordCanary"),
        new("DiscordDevelopment", "DiscordDevelopment.exe", "DiscordDevelopment")
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

    internal static IReadOnlyList<string> CreateLaunchPlanForTesting(
        IEnumerable<string> executablePaths)
    {
        return executablePaths
            .Select(path => Path.GetFullPath(path))
            .Where(path => TryCreateLaunchSpecFromTrustedExecutablePath(
                path,
                out _))
            .Select(path =>
            {
                TryCreateLaunchSpecFromTrustedExecutablePath(path, out var launchSpec);
                return launchSpec.CommandKey;
            })
            .ToArray();
    }

    public async Task<DiscordRestartResult> RestartAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.HasRunningProcesses)
        {
            var installedLaunchSpecs = ResolveInstalledLaunchSpecs(snapshot.ExecutablePaths);
            if (installedLaunchSpecs.Length == 0)
            {
                return new DiscordRestartResult(
                    false,
                    "Discord bulunamadı. Discord'u açıp tekrar deneyin.",
                    "No trusted Discord installation was found.");
            }

            return await LaunchDiscordAsync(
                installedLaunchSpecs,
                "Discord açıldı.",
                cancellationToken);
        }

        var targets = ResolveRestartTargets(snapshot);
        if (targets.Failures.Count > 0)
        {
            DisposeTargets(targets.Targets);
            return new DiscordRestartResult(
                false,
                "Discord otomatik yenilenemedi.",
                string.Join("; ", targets.Failures));
        }

        if (targets.Targets.Count == 0)
        {
            return new DiscordRestartResult(
                false,
                "Discord yolu doğrulanamadı.",
                "No trusted Discord process target was available.");
        }

        try
        {
            var launchSpecs = ResolveLaunchSpecs(
                targets.Targets.Select(target => target.ExecutablePath));
            if (launchSpecs.Length == 0)
            {
                return new DiscordRestartResult(
                    false,
                    "Discord yolu doğrulanamadı.",
                    "No trusted Discord launch target was available after process validation.");
            }

            var failures = new List<string>();
            foreach (var target in targets.Targets)
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

            return await LaunchDiscordAsync(
                launchSpecs,
                launchSpecs.Length > 1
                    ? "Discord uygulamaları yenilendi."
                    : "Discord yenilendi.",
                cancellationToken);
        }
        finally
        {
            DisposeTargets(targets.Targets);
        }
    }

    public async Task<DiscordRestartResult> VerifyReadyAsync(
        DiscordProcessSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var effectiveSnapshot = snapshot.HasRunningProcesses
            ? snapshot
            : Capture();
        if (!effectiveSnapshot.HasRunningProcesses)
        {
            return new DiscordRestartResult(
                false,
                "Discord kapanmış. Discord'u açıp tekrar deneyin.",
                "No trusted Discord process was running after tunnel resume.",
                DiscordRestartFailureKind.Unknown);
        }

        var launchSpecs = ResolveLaunchSpecs(effectiveSnapshot.ExecutablePaths);
        if (launchSpecs.Length == 0)
        {
            return new DiscordRestartResult(
                false,
                "Discord yolu doğrulanamadı.",
                "No trusted Discord executable path was available after tunnel resume.",
                DiscordRestartFailureKind.Unknown);
        }

        var windowResult = await WaitForVisibleDiscordWindowAsync(
            launchSpecs,
            cancellationToken);
        if (windowResult is VisibleDiscordWindowResult.MainWindow)
        {
            return new DiscordRestartResult(true, "Discord güncellendi.");
        }

        if (windowResult is VisibleDiscordWindowResult.UpdaterWindow)
        {
            return new DiscordRestartResult(
                false,
                "Discord güncellendi ama ana pencere hazır olmadı. Discord'u kapatıp tekrar deneyin.",
                "Only a trusted Discord updater window was detected after tunnel resume.",
                DiscordRestartFailureKind.UpdaterWindow);
        }

        return new DiscordRestartResult(
            false,
            "Discord açıldı ama pencere görünmedi. Görev çubuğundan Discord'u açın.",
            "No trusted visible Discord main window was detected after tunnel resume.",
            DiscordRestartFailureKind.Unknown);
    }

    public async Task<DiscordRestartResult> CloseAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.HasRunningProcesses)
        {
            return DiscordRestartResult.NotNeeded();
        }

        var targets = ResolveRestartTargets(snapshot);
        if (targets.Failures.Count > 0)
        {
            DisposeTargets(targets.Targets);
            return new DiscordRestartResult(
                false,
                "Discord kapatılamadı.",
                string.Join("; ", targets.Failures));
        }

        if (targets.Targets.Count == 0)
        {
            return new DiscordRestartResult(
                false,
                "Discord kapatılamadı.",
                "No trusted Discord process target was available.");
        }

        try
        {
            foreach (var target in targets.Targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await StopDiscordProcessAsync(
                    target.Process,
                    gracefulTimeout,
                    cancellationToken);
            }

            return new DiscordRestartResult(true, "Discord kapatıldı.");
        }
        finally
        {
            DisposeTargets(targets.Targets);
        }
    }

    private static RestartTargets ResolveRestartTargets(DiscordProcessSnapshot snapshot)
    {
        var effectiveSnapshot = snapshot.ProcessIds.Count > 0
            ? snapshot
            : new WindowsDiscordProcessInspector().Capture();
        var processIds = effectiveSnapshot.ProcessIds
            .Distinct()
            .ToArray();
        var expectedExecutablePaths = effectiveSnapshot.ExecutablePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = new List<RestartTarget>();
        var failures = new List<string>();

        foreach (var processId in processIds)
        {
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

        return new RestartTargets(targets, failures);
    }

    private static async Task<DiscordRestartResult> LaunchDiscordAsync(
        IReadOnlyList<LaunchSpec> launchSpecs,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();

        foreach (var launchSpec in launchSpecs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ShellExecuteUnelevated(launchSpec);
            }
            catch (Exception exception)
                when (exception is InvalidOperationException
                    or TargetInvocationException
                    or System.ComponentModel.Win32Exception
                    or COMException)
            {
                failures.Add($"{launchSpec.FileName}: {exception.Message}");
            }
        }

        if (failures.Count > 0)
        {
            return new DiscordRestartResult(
                false,
                "Discord açılamadı.",
                string.Join("; ", failures));
        }

        var windowResult = await WaitForVisibleDiscordWindowAsync(
            launchSpecs,
            cancellationToken);
        if (windowResult is VisibleDiscordWindowResult.MainWindow)
        {
            return new DiscordRestartResult(true, successMessage);
        }

        if (windowResult is VisibleDiscordWindowResult.UpdaterWindow)
        {
            return new DiscordRestartResult(
                false,
                "Discord güncelleme ekranında kaldı. Bağlantı kısa süre yenileniyor.",
                "Only a trusted Discord updater window was detected; no trusted main window appeared.",
                DiscordRestartFailureKind.UpdaterWindow);
        }

        return new DiscordRestartResult(
            false,
            "Discord açıldı ama pencere görünmedi. Görev çubuğundan Discord'u açın.",
            "Discord process was launched, but no visible trusted Discord main window was detected.",
            DiscordRestartFailureKind.Unknown);
    }

    private static void ShellExecuteUnelevated(LaunchSpec launchSpec)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Discord otomatik açma Windows gerektirir.");
        }

        var shellWindowsType = Type.GetTypeFromCLSID(ShellWindowsClsid)
            ?? throw new InvalidOperationException("Windows Explorer başlatıcısı bulunamadı.");
        var shellWindows = Activator.CreateInstance(shellWindowsType)
            ?? throw new InvalidOperationException("Windows Shell başlatıcısı açılamadı.");
        object? desktopBrowser = null;
        object? desktopDocument = null;
        object? shellApplication = null;

        try
        {
            desktopBrowser = GetDesktopShellBrowser(shellWindowsType, shellWindows);
            desktopDocument = desktopBrowser
                .GetType()
                .InvokeMember(
                    "Document",
                    BindingFlags.GetProperty,
                    binder: null,
                    target: desktopBrowser,
                    args: null,
                    culture: CultureInfo.InvariantCulture);
            if (desktopDocument is null)
            {
                throw new InvalidOperationException("Windows Explorer masaüstü görünümü bulunamadı.");
            }

            shellApplication = desktopDocument
                .GetType()
                .InvokeMember(
                    "Application",
                    BindingFlags.GetProperty,
                    binder: null,
                    target: desktopDocument,
                    args: null,
                    culture: CultureInfo.InvariantCulture);
            if (shellApplication is null)
            {
                throw new InvalidOperationException("Windows Explorer başlatıcısı bulunamadı.");
            }

            shellApplication
                .GetType()
                .InvokeMember(
                    "ShellExecute",
                    BindingFlags.InvokeMethod,
                    binder: null,
                    target: shellApplication,
                    args:
                    [
                        launchSpec.FileName,
                        launchSpec.Arguments,
                        launchSpec.WorkingDirectory,
                        "open",
                        ShowNormal
                    ],
                    culture: CultureInfo.InvariantCulture);
        }
        finally
        {
            ReleaseComObject(shellApplication);
            ReleaseComObject(desktopDocument);
            ReleaseComObject(desktopBrowser);
            ReleaseComObject(shellWindows);
        }
    }

    private static object GetDesktopShellBrowser(Type shellWindowsType, object shellWindows)
    {
        var args = new object[]
        {
            CsidlDesktop,
            Type.Missing,
            SwcDesktop,
            0,
            SwfoNeedDispatch
        };
        var modifier = new ParameterModifier(5);
        modifier[0] = true;
        modifier[1] = true;
        modifier[3] = true;

        return shellWindowsType.InvokeMember(
                "FindWindowSW",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shellWindows,
                args,
                modifiers: [modifier],
                culture: CultureInfo.InvariantCulture,
                namedParameters: null)
            ?? throw new InvalidOperationException("Windows Explorer masaüstü başlatıcısı bulunamadı.");
    }

    private static void ReleaseComObject(object? instance)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (instance is not null && Marshal.IsComObject(instance))
        {
            try
            {
                Marshal.FinalReleaseComObject(instance);
            }
            catch (ArgumentException)
            {
            }
        }
    }

    private static async Task<VisibleDiscordWindowResult> WaitForVisibleDiscordWindowAsync(
        IReadOnlyList<LaunchSpec> launchSpecs,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + LaunchVerificationTimeout;
        var updaterWindowSeen = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var processName in launchSpecs
                         .Select(launchSpec => launchSpec.ProcessName)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var process in GetProcessesByName(processName))
                {
                    using (process)
                    {
                        var windowResult = GetVisibleDiscordWindowResult(
                            process,
                            launchSpecs);
                        if (windowResult is VisibleDiscordWindowResult.MainWindow)
                        {
                            return VisibleDiscordWindowResult.MainWindow;
                        }

                        if (windowResult is VisibleDiscordWindowResult.UpdaterWindow)
                        {
                            updaterWindowSeen = true;
                        }
                    }
                }
            }

            await Task.Delay(LaunchVerificationInterval, cancellationToken);
        }

        return updaterWindowSeen
            ? VisibleDiscordWindowResult.UpdaterWindow
            : VisibleDiscordWindowResult.None;
    }

    private static VisibleDiscordWindowResult GetVisibleDiscordWindowResult(
        Process process,
        IReadOnlyList<LaunchSpec> launchSpecs)
    {
        try
        {
            if (process.HasExited)
            {
                return VisibleDiscordWindowResult.None;
            }

            var executablePath = TryGetExecutablePath(process);
            if (string.IsNullOrWhiteSpace(executablePath)
                || !launchSpecs.Any(spec => spec.MatchesExecutablePath(executablePath))
                || !IsTrustedDiscordExecutable(executablePath))
            {
                return VisibleDiscordWindowResult.None;
            }

            process.Refresh();
            var handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero || !IsWindowVisible(handle))
            {
                return VisibleDiscordWindowResult.None;
            }

            if (IsIconic(handle))
            {
                ShowWindow(handle, RestoreWindow);
                return VisibleDiscordWindowResult.None;
            }

            var title = process.MainWindowTitle;
            if (title.Contains("Updater", StringComparison.OrdinalIgnoreCase)
                || title.Contains("Update", StringComparison.OrdinalIgnoreCase))
            {
                return VisibleDiscordWindowResult.UpdaterWindow;
            }

            return VisibleDiscordWindowResult.MainWindow;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            return VisibleDiscordWindowResult.None;
        }
    }

    private static LaunchSpec[] ResolveLaunchSpecs(
        IEnumerable<string> executablePaths)
    {
        var launchSpecs = new List<LaunchSpec>();
        foreach (var executablePath in executablePaths)
        {
            if (TryCreateLaunchSpecFromExecutablePath(executablePath, out var launchSpec))
            {
                launchSpecs.Add(launchSpec);
            }
        }

        return launchSpecs
            .DistinctBy(launchSpec => launchSpec.CommandKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static LaunchSpec[] ResolveInstalledLaunchSpecs(
        IReadOnlyList<string> executablePaths)
    {
        var launchSpecs = ResolveLaunchSpecs(executablePaths).ToList();
        if (launchSpecs.Count > 0)
        {
            return launchSpecs
                .DistinctBy(launchSpec => launchSpec.CommandKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return [];
        }

        foreach (var spec in KnownInstallations)
        {
            var root = Path.Combine(localAppData, spec.DirectoryName);
            var executablePath = FindInstalledDiscordExecutable(root, spec.ExecutableName);
            if (executablePath is null
                || !IsTrustedDiscordExecutable(executablePath, spec.ExecutableName))
            {
                continue;
            }

            var appDirectory = Path.GetDirectoryName(executablePath);
            if (string.IsNullOrWhiteSpace(appDirectory))
            {
                continue;
            }

            launchSpecs.Add(new LaunchSpec(
                executablePath,
                string.Empty,
                appDirectory,
                spec.ProcessName,
                appDirectory,
                [executablePath]));
            break;
        }

        return launchSpecs
            .DistinctBy(launchSpec => launchSpec.CommandKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FindInstalledDiscordExecutable(
        string installRoot,
        string executableName)
    {
        try
        {
            if (!Directory.Exists(installRoot))
            {
                return null;
            }

            return Directory
                .EnumerateDirectories(installRoot, "app-*", SearchOption.TopDirectoryOnly)
                .Select(directory => Path.Combine(directory, executableName))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return null;
        }
    }

    private static bool TryCreateLaunchSpecFromExecutablePath(
        string? executablePath,
        out LaunchSpec launchSpec)
    {
        launchSpec = default!;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(executablePath);
            if (!IsTrustedDiscordExecutable(fullPath))
            {
                return false;
            }

            return TryCreateLaunchSpecFromTrustedExecutablePath(
                fullPath,
                out launchSpec);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryCreateLaunchSpecFromTrustedExecutablePath(
        string fullPath,
        out LaunchSpec launchSpec)
    {
        launchSpec = default!;

        try
        {
            var processName = Path.GetFileNameWithoutExtension(fullPath);
            var appDirectory = Directory.GetParent(fullPath);
            var installRoot = appDirectory?.Parent;
            if (appDirectory is not null
                && installRoot is not null
                && appDirectory.Name.StartsWith("app-", StringComparison.OrdinalIgnoreCase))
            {
                launchSpec = new LaunchSpec(
                    fullPath,
                    string.Empty,
                    appDirectory.FullName,
                    processName,
                    appDirectory.FullName,
                    [fullPath]);
                return true;
            }

            launchSpec = new LaunchSpec(
                fullPath,
                string.Empty,
                Path.GetDirectoryName(fullPath)!,
                processName,
                Path.GetDirectoryName(fullPath)!,
                [fullPath]);
            return true;
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
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
                return false;
            }

            if (expectedExecutablePaths.Count > 0
                && !expectedExecutablePaths.Contains(executablePath))
            {
                validationError = "Discord yolu değişti.";
                return false;
            }

            if (!IsTrustedDiscordExecutable(executablePath))
            {
                validationError = "Discord imzası doğrulanamadı.";
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

    private static bool IsTrustedDiscordExecutable(
        string? path,
        string? expectedFileName = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath)
                || HasReparsePointInPath(fullPath)
                || (expectedFileName is not null
                    && !Path.GetFileName(fullPath).Equals(
                        expectedFileName,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var publisher = AuthenticodeSignatureVerifier.TryGetSignerPublisher(fullPath);
            return string.Equals(
                publisher,
                ExpectedPublisher,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or IOException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool HasReparsePointInPath(string path)
    {
        var current = File.Exists(path)
            ? new FileInfo(path).FullName
            : new DirectoryInfo(path).FullName;

        while (!string.IsNullOrWhiteSpace(current))
        {
            try
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }
            catch (Exception exception)
                when (exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or NotSupportedException)
            {
                return true;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return false;
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

    private static void DisposeTargets(IEnumerable<RestartTarget> targets)
    {
        foreach (var target in targets)
        {
            target.Process.Dispose();
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private sealed record InstallationSpec(
        string DirectoryName,
        string ExecutableName,
        string ProcessName);

    private sealed record LaunchSpec(
        string FileName,
        string Arguments,
        string WorkingDirectory,
        string ProcessName,
        string TrustedRoot,
        IReadOnlyList<string> ExpectedExecutablePaths)
    {
        public string CommandKey => $"{FileName}\n{Arguments}";

        public bool MatchesExecutablePath(string executablePath)
        {
            var fullPath = Path.GetFullPath(executablePath);
            return ExpectedExecutablePaths.Any(path => string.Equals(
                    Path.GetFullPath(path),
                    fullPath,
                    StringComparison.OrdinalIgnoreCase))
                || fullPath.StartsWith(
                    Path.GetFullPath(TrustedRoot) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record RestartTargets(
        IReadOnlyList<RestartTarget> Targets,
        IReadOnlyList<string> Failures);

    private sealed record RestartTarget(Process Process, string ExecutablePath);

    private enum VisibleDiscordWindowResult
    {
        None,
        MainWindow,
        UpdaterWindow
    }
}
