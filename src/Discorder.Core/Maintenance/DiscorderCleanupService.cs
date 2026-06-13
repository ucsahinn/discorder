using Discorder.Core.Configuration;
using Discorder.Core.Diagnostics;
using Discorder.Core.Firewall;

namespace Discorder.Core.Maintenance;

public sealed class DiscorderCleanupService
{
    private readonly AppPaths _paths;
    private readonly IDiscordAccessLock _accessLock;
    private readonly IDiscorderDiagnostics _diagnostics;

    public DiscorderCleanupService(
        AppPaths paths,
        IDiscordAccessLock accessLock,
        IDiscorderDiagnostics? diagnostics = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _accessLock = accessLock ?? throw new ArgumentNullException(nameof(accessLock));
        _diagnostics = diagnostics ?? NullDiscorderDiagnostics.Instance;
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
        await _accessLock.RemoveAsync(cancellationToken);
        DeleteDiscorderDataDirectory(cancellationToken);
        DeleteDiscorderSharedDataDirectory(cancellationToken);
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

    private void DeleteDiscorderDataDirectory(CancellationToken cancellationToken)
    {
        DeleteDiscorderDirectory(_paths.DataDirectory, cancellationToken);
    }

    private void DeleteDiscorderSharedDataDirectory(CancellationToken cancellationToken)
    {
        if (string.Equals(
                Path.GetFullPath(_paths.SharedDataDirectory),
                Path.GetFullPath(_paths.DataDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DeleteDiscorderDirectory(_paths.SharedDataDirectory, cancellationToken);
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
            throw new IOException(
                "Discorder veri klasoru temizlenemedi: " +
                directory.FullName);
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
}
