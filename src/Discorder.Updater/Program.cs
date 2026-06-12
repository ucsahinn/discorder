using System.Diagnostics;
using System.Globalization;
using Discorder.Core.Updates;

return await DiscorderUpdater.RunAsync(args);

internal static class DiscorderUpdater
{
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(90);

    public static async Task<int> RunAsync(string[] args)
    {
        UpdateOptions options;
        try
        {
            options = UpdateOptions.Parse(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        var log = new UpdateLog(options.LogPath);
        try
        {
            log.Write("Update helper started.");
            await WaitForDiscorderToExitAsync(options.ProcessId, log);

            var packageHash = UpdatePackageValidator.ComputeSha256(options.PackagePath);
            if (!string.Equals(
                    packageHash,
                    options.ExpectedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Update package hash changed before apply.");
            }

            var updateRoot = Path.GetDirectoryName(options.PackagePath)
                ?? Path.GetTempPath();
            var applyPayload = Path.Combine(
                updateRoot,
                "apply-payload-" + Guid.NewGuid().ToString("N"));
            UpdatePackageValidator.ExtractToDirectory(
                options.PackagePath,
                applyPayload,
                options.ExecutableName,
                options.ExpectedVersion,
                options.ExpectedSignerThumbprint,
                options.ExpectedSha256);
            var newManifest = UpdatePackageValidator.ValidatePayload(
                applyPayload,
                options.ExecutableName,
                options.ExpectedVersion,
                options.ExpectedSignerThumbprint);

            ApplyPayload(
                applyPayload,
                options.TargetDirectory,
                newManifest,
                options.ExecutableName,
                options.ExpectedVersion,
                options.ExpectedSignerThumbprint,
                log);

            var executablePath = Path.Combine(
                options.TargetDirectory,
                options.ExecutableName);
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException(
                    "Updated executable not found.",
                    executablePath);
            }

            log.Write("Starting updated Discorder.");
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = options.TargetDirectory,
                UseShellExecute = false
            });

            log.Write("Update completed.");
            return 0;
        }
        catch (Exception exception)
        {
            log.Write("Update failed: " + exception.Message);
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task WaitForDiscorderToExitAsync(
        int processId,
        UpdateLog log)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return;
            }

            log.Write("Waiting for Discorder to exit.");
            using var timeout = new CancellationTokenSource(ProcessExitTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    "Discorder did not exit before the update timeout.");
            }
        }
        catch (ArgumentException)
        {
            log.Write("Discorder process was already closed.");
        }
    }

    private static void ApplyPayload(
        string payloadDirectory,
        string targetDirectory,
        UpdateManifest newManifest,
        string executableName,
        string expectedVersion,
        string? expectedSignerThumbprint,
        UpdateLog log)
    {
        var targetRoot = Path.GetFullPath(targetDirectory);
        if (!Directory.Exists(targetRoot))
        {
            throw new DirectoryNotFoundException(targetRoot);
        }

        var backupRoot = Path.Combine(
            Path.GetDirectoryName(payloadDirectory) ?? Path.GetTempPath(),
            "backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupRoot);

        var backups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var copiedWithoutBackup = new List<string>();
        var filesToInstall = newManifest.Files
            .Select(file => UpdatePackageValidator.NormalizeRelativePath(file.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        filesToInstall.Add(UpdatePackageValidator.ManifestFileName);

        var staleFiles = ReadOldManifest(targetRoot, log)?
            .Files
            .Select(file => UpdatePackageValidator.NormalizeRelativePath(file.Path))
            .Where(path => !filesToInstall.Contains(path))
            .ToArray() ?? [];

        try
        {
            foreach (var relativePath in filesToInstall.Concat(staleFiles)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                BackupTargetFile(targetRoot, backupRoot, relativePath, backups);
            }

            foreach (var file in newManifest.Files)
            {
                var relativePath = UpdatePackageValidator.NormalizeRelativePath(file.Path);
                var source = UpdatePackageValidator.GetSafePath(
                    payloadDirectory,
                    relativePath);
                var destination = UpdatePackageValidator.GetSafePath(
                    targetRoot,
                    relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (!backups.ContainsKey(destination))
                {
                    copiedWithoutBackup.Add(destination);
                }

                File.Copy(source, destination, overwrite: true);
            }

            CopyManifest(
                targetRoot,
                newManifest,
                backups,
                copiedWithoutBackup);

            UpdatePackageValidator.ValidatePayload(
                targetRoot,
                executableName,
                expectedVersion,
                expectedSignerThumbprint,
                newManifest);
            log.Write("Payload applied.");
        }
        catch
        {
            log.Write("Restoring previous files.");
            RestoreBackup(backups, copiedWithoutBackup, log);
            throw;
        }
    }

    private static void CopyManifest(
        string targetRoot,
        UpdateManifest manifest,
        Dictionary<string, string> backups,
        List<string> copiedWithoutBackup)
    {
        var destination = UpdatePackageValidator.GetSafePath(
            targetRoot,
            UpdatePackageValidator.ManifestFileName);
        if (!backups.ContainsKey(destination))
        {
            copiedWithoutBackup.Add(destination);
        }

        UpdatePackageValidator.WriteManifest(targetRoot, manifest);
    }

    private static UpdateManifest? ReadOldManifest(
        string targetRoot,
        UpdateLog log)
    {
        try
        {
            return UpdatePackageValidator.TryReadManifest(targetRoot);
        }
        catch (Exception exception)
        {
            log.Write("Existing manifest ignored: " + exception.Message);
            return null;
        }
    }

    private static void BackupTargetFile(
        string targetRoot,
        string backupRoot,
        string relativePath,
        IDictionary<string, string> backups)
    {
        var source = UpdatePackageValidator.GetSafePath(targetRoot, relativePath);
        if (!File.Exists(source))
        {
            return;
        }

        var backup = UpdatePackageValidator.GetSafePath(backupRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Move(source, backup, overwrite: true);
        backups[source] = backup;
    }

    private static void RestoreBackup(
        IReadOnlyDictionary<string, string> backups,
        IEnumerable<string> copiedWithoutBackup,
        UpdateLog log)
    {
        foreach (var path in copiedWithoutBackup)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                log.Write("Could not remove partial file: " + exception.Message);
            }
        }

        foreach (var item in backups)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.Key)!);
                File.Move(item.Value, item.Key, overwrite: true);
            }
            catch (Exception exception)
            {
                log.Write("Could not restore backup: " + exception.Message);
            }
        }
    }
}

internal sealed record UpdateOptions(
    int ProcessId,
    string PackagePath,
    string ExpectedSha256,
    string ExpectedVersion,
    string? ExpectedSignerThumbprint,
    string TargetDirectory,
    string ExecutableName,
    string LogPath)
{
    public static UpdateOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal)
                || index + 1 >= args.Length)
            {
                throw new ArgumentException("Update helper arguments are invalid.");
            }

            values[args[index][2..]] = args[index + 1];
        }

        return new UpdateOptions(
            int.Parse(
                Require(values, "process-id"),
                CultureInfo.InvariantCulture),
            Path.GetFullPath(Require(values, "package")),
            Require(values, "expected-sha256"),
            Require(values, "expected-version"),
            Optional(values, "expected-signer-thumbprint"),
            Path.GetFullPath(Require(values, "target-directory")),
            Require(values, "executable-name"),
            Path.GetFullPath(Require(values, "log")));
    }

    private static string Require(
        Dictionary<string, string> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"Missing update helper argument: --{name}");
        }

        return value;
    }

    private static string? Optional(
        Dictionary<string, string> values,
        string name)
    {
        return values.TryGetValue(name, out var value)
            && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
    }
}

internal sealed class UpdateLog
{
    private readonly string _path;

    public UpdateLog(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Write(string message)
    {
        var line = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:O} {message}");
        File.AppendAllText(_path, line + Environment.NewLine);
    }
}
