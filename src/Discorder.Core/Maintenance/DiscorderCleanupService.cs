using Discorder.Core.Configuration;
using Discorder.Core.Firewall;

namespace Discorder.Core.Maintenance;

public sealed class DiscorderCleanupService
{
    private readonly AppPaths _paths;
    private readonly IDiscordAccessLock _accessLock;

    public DiscorderCleanupService(
        AppPaths paths,
        IDiscordAccessLock accessLock)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _accessLock = accessLock ?? throw new ArgumentNullException(nameof(accessLock));
    }

    public async Task CleanUninstallAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _accessLock.RemoveAsync(cancellationToken);
        DeleteDiscorderDataDirectory(cancellationToken);
    }

    private void DeleteDiscorderDataDirectory(CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(_paths.DataDirectory);
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
}
