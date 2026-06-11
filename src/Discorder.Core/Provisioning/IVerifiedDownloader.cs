namespace Discorder.Core.Provisioning;

public interface IVerifiedDownloader
{
    Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken);
}
