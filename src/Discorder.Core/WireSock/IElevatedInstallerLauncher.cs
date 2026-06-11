namespace Discorder.Core.WireSock;

public interface IElevatedInstallerLauncher
{
    Task<int> InstallAsync(
        string installerPath,
        CancellationToken cancellationToken);
}
