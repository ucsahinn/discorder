using Discorder.Core.Security;

namespace Discorder.Core.Provisioning;

public sealed class VerifiedDownloader : IVerifiedDownloader
{
    private readonly HttpClient _httpClient;

    public VerifiedDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);

        if (await FileHashVerifier.MatchesSha256Async(
                destination,
                expectedSha256,
                cancellationToken))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporaryPath = destination + ".download";

        try
        {
            using var response = await _httpClient.GetAsync(
                source,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var sourceStream = await response.Content.ReadAsStreamAsync(
                             cancellationToken))
            await using (var destinationStream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await sourceStream.CopyToAsync(destinationStream, cancellationToken);
            }

            if (!await FileHashVerifier.MatchesSha256Async(
                    temporaryPath,
                    expectedSha256,
                    cancellationToken))
            {
                throw new InvalidDataException(
                    $"{source.Host} için SHA-256 doğrulaması başarısız oldu.");
            }

            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
