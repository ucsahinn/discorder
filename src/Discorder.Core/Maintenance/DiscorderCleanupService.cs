using Discorder.Core.Configuration;
using Discorder.Core.Diagnostics;
using Discorder.Core.Firewall;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Discorder.Core.Maintenance;

public sealed class DiscorderCleanupService
{
    private readonly AppPaths _paths;
    private readonly IDiscordAccessLock _accessLock;
    private readonly IDiscorderDiagnostics _diagnostics;
    private readonly bool _allowNonDefaultDataRoots;

    public DiscorderCleanupService(
        AppPaths paths,
        IDiscordAccessLock accessLock,
        IDiscorderDiagnostics? diagnostics = null,
        bool allowNonDefaultDataRoots = false)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _accessLock = accessLock ?? throw new ArgumentNullException(nameof(accessLock));
        _diagnostics = diagnostics ?? NullDiscorderDiagnostics.Instance;
        _allowNonDefaultDataRoots = allowNonDefaultDataRoots;
    }

    public async Task CleanUninstallAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _diagnostics.Warning(
            "cleanup.cleanUninstall",
            "Uygulama kaldırma başladı.",
            new Dictionary<string, string?>
            {
                ["dataDirectory"] = _paths.DataDirectory
            });

        var targets = GetCleanupTargets();
        foreach (var target in targets)
        {
            ValidateCleanupTarget(target);
        }

        await _accessLock.RemoveAsync(cancellationToken);

        var diagnosticsStopped = false;
        try
        {
            StopPersistentDiagnostics();
            diagnosticsStopped = true;
            foreach (var target in targets)
            {
                DeleteDiscorderDirectory(target, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (diagnosticsStopped)
            {
                ResumePersistentDiagnostics();
            }

            _diagnostics.Failure(
                "cleanup.cleanUninstall",
                "Uygulama kaldirma tamamlanamadi.",
                exception);
            throw;
        }
    }

    public async Task RepairAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _diagnostics.Info(
            "cleanup.repair",
            "Onarım başladı; ayarlar ve tanılama logları korunacak.");
        await _accessLock.EnableAsync(cancellationToken);

        DeleteGeneratedDirectory(_paths.ProfileDirectory, cancellationToken);
        DeleteGeneratedDirectory(_paths.ToolsDirectory, cancellationToken);
        DeleteGeneratedDirectory(_paths.InstallerDirectory, cancellationToken);

        _paths.EnsureDirectories();
        _diagnostics.WriteHealth(
            "onarım tamamlandı",
            new Dictionary<string, string?>
            {
                ["profiles"] = "yeniden üretilecek",
                ["tools"] = "yeniden indirilecek",
                ["installers"] = "temizlendi",
                ["logs"] = "korundu"
            });
    }

    private void StopPersistentDiagnostics()
    {
        if (_diagnostics is DiscorderDiagnostics diagnostics)
        {
            diagnostics.StopPersistentWrites();
        }
    }

    private void ResumePersistentDiagnostics()
    {
        if (_diagnostics is DiscorderDiagnostics diagnostics)
        {
            diagnostics.ResumePersistentWrites();
        }
    }

    private string[] GetCleanupTargets()
    {
        return new[]
        {
            _paths.DataDirectory,
            _paths.SharedDataDirectory
        }
            .Select(NormalizeDirectoryPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ValidateCleanupTarget(string path)
    {
        if (!_allowNonDefaultDataRoots
            && !IsDefaultDiscorderDataRoot(path))
        {
            throw new InvalidOperationException(
                "Discorder veri klasoru beklenen AppData konumunda degil.");
        }

        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            return;
        }

        if (!string.Equals(directory.Name, "Discorder", StringComparison.Ordinal)
            || directory.Parent is null)
        {
            throw new InvalidOperationException(
                "Discorder veri klasoru beklenen konumda degil.");
        }

        RejectReparsePoints(directory);
    }

    private static bool IsDefaultDiscorderDataRoot(string path)
    {
        var localRoot = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var sharedRoot = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);

        if (string.IsNullOrWhiteSpace(localRoot)
            || string.IsNullOrWhiteSpace(sharedRoot))
        {
            return false;
        }

        var expected = new[]
        {
            Path.Combine(localRoot, "Discorder"),
            Path.Combine(sharedRoot, "Discorder")
        }
            .Select(NormalizeDirectoryPath);

        return expected.Contains(path, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void DeleteDiscorderDirectory(
        string path,
        CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            return;
        }

        if (!string.Equals(directory.Name, "Discorder", StringComparison.Ordinal)
            || directory.Parent is null)
        {
            throw new InvalidOperationException(
                "Discorder veri klasoru beklenen konumda degil.");
        }

        RejectReparsePoints(directory);

        for (var attempt = 1; attempt <= 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                directory.Refresh();
                if (!directory.Exists)
                {
                    return;
                }

                RejectReparsePoints(directory);
                ClearReadOnlyAttributes(directory);
                RejectReparsePoints(directory);
                directory.Delete(recursive: true);
                return;
            }
            catch (IOException) when (attempt < 8)
            {
                Thread.Sleep(120 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < 8)
            {
                Thread.Sleep(120 * attempt);
            }
        }

        directory.Refresh();
        if (directory.Exists)
        {
            throw new IOException(
                "Discorder veri klasoru temizlenemedi: " +
                directory.FullName);
        }
    }

    private static void RejectReparsePoints(DirectoryInfo root)
    {
        var directories = new Stack<DirectoryInfo>();
        directories.Push(root);

        while (directories.Count > 0)
        {
            var directory = directories.Pop();
            directory.Refresh();
            if (!directory.Exists)
            {
                continue;
            }

            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException(
                    "Discorder veri klasoru junction veya symlink olamaz: " +
                    directory.FullName);
            }

            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException(
                        "Discorder veri klasoru junction veya symlink iceremez: " +
                        entry.FullName);
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    directories.Push(childDirectory);
                }
            }
        }
    }

    private static void ClearReadOnlyAttributes(DirectoryInfo root)
    {
        var directories = new Stack<DirectoryInfo>();
        directories.Push(root);

        while (directories.Count > 0)
        {
            var directory = directories.Pop();
            directory.Refresh();
            if (!directory.Exists)
            {
                continue;
            }

            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException(
                    "Discorder veri klasoru junction veya symlink olamaz: " +
                    directory.FullName);
            }

            if (directory.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                directory.Attributes &= ~FileAttributes.ReadOnly;
            }

            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                entry.Refresh();
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException(
                        "Discorder veri klasoru junction veya symlink iceremez: " +
                        entry.FullName);
                }

                if (entry is FileInfo file)
                {
                    ClearReadOnlyAttribute(file);
                    continue;
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    directories.Push(childDirectory);
                }
            }
        }
    }

    private static void ClearReadOnlyAttribute(FileInfo file)
    {
        if (!file.Attributes.HasFlag(FileAttributes.ReadOnly))
        {
            return;
        }

        if (HasMultipleHardLinks(file))
        {
            throw new InvalidOperationException(
                "Discorder veri klasorunde hard link iceren dosya temizlenemez: " +
                file.FullName);
        }

        file.Attributes &= ~FileAttributes.ReadOnly;
    }

    private static bool HasMultipleHardLinks(FileInfo file)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var handle = File.OpenHandle(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandle(handle, out var information))
            {
                throw new IOException(
                    "Dosya baglanti bilgisi okunamadi.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }

            return information.NumberOfLinks > 1;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                "Discorder veri klasorundeki dosya baglanti bilgisi dogrulanamadi: " +
                file.FullName,
                exception);
        }
    }

    private void DeleteGeneratedDirectory(
        string path,
        CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            return;
        }

        var allowedRoots = new[]
        {
            _paths.DataDirectory,
            _paths.SharedDataDirectory
        }
            .Select(root => Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var target = Path.GetFullPath(directory.FullName)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!allowedRoots.Any(root => target.StartsWith(
                root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Onarim klasoru Discorder veri kokunun disinda.");
        }

        DeleteDirectoryWithRetry(
            directory,
            "Discorder onarim klasoru temizlenemedi: ",
            cancellationToken);
    }

    private static void DeleteDirectoryWithRetry(
        DirectoryInfo directory,
        string failurePrefix,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                directory.Refresh();
                if (!directory.Exists)
                {
                    return;
                }

                directory.Delete(recursive: true);
                return;
            }
            catch (IOException) when (attempt < 8)
            {
                Thread.Sleep(120 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < 8)
            {
                Thread.Sleep(120 * attempt);
            }
        }

        directory.Refresh();
        if (directory.Exists)
        {
            throw new IOException(failurePrefix + directory.FullName);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out ByHandleFileInformation lpFileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
