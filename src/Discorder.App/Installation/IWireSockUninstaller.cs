namespace Discorder.App.Installation;

public interface IWireSockUninstaller
{
    Task UninstallIfDiscorderInstalledAsync(
        bool installedByDiscorder,
        CancellationToken cancellationToken);
}
