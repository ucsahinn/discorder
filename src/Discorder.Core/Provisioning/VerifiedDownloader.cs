using Discorder.Core.Security;
using System.Net;

namespace Discorder.Core.Provisioning;

public sealed class VerifiedDownloader : IVerifiedDownloader
{
    private const int DefaultMaxAttempts = 3;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    private readonly HttpClient _httpClient;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;

    public VerifiedDownloader(
        HttpClient httpClient,
        int maxAttempts = DefaultMaxAttempts,
        TimeSpan? retryDelay = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttempts),
                "En az bir indirme denemesi yapılmalıdır.");
        }

        _maxAttempts = maxAttempts;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
    }

    public async Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken,
        long? maxBytes = null)
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
            for (var attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                try
                {
                    await DownloadOnceAsync(
                        source,
                        temporaryPath,
                        destination,
                        expectedSha256,
                        maxBytes,
                        cancellationToken);
                    return;
                }
                catch (Exception exception) when (
                    attempt < _maxAttempts
                    && IsTransientDownloadFailure(exception, cancellationToken))
                {
                    TryDelete(temporaryPath);
                    await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                }
            }
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private async Task DownloadOnceAsync(
        Uri source,
        string temporaryPath,
        string destination,
        string expectedSha256,
        long? maxBytes,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            source,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{source.Host} HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                null,
                response.StatusCode);
        }

        if (maxBytes is not null
            && response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength.Value > maxBytes.Value)
        {
            throw new InvalidDataException(
                $"{source.Host} dosyası beklenenden büyük.");
        }

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
            await CopyToAsync(
                sourceStream,
                destinationStream,
                maxBytes,
                cancellationToken);
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

    private static async Task CopyToAsync(
        Stream source,
        Stream destination,
        long? maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        var totalBytes = 0L;
        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                return;
            }

            totalBytes += bytesRead;
            if (maxBytes is not null && totalBytes > maxBytes.Value)
            {
                throw new InvalidDataException(
                    "İndirilen dosya beklenenden büyük.");
            }

            await destination.WriteAsync(
                buffer.AsMemory(0, bytesRead),
                cancellationToken);
        }
    }

    private static bool IsTransientDownloadFailure(
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception switch
        {
            TaskCanceledException => true,
            TimeoutException => true,
            HttpRequestException httpException
                => IsTransientStatusCode(httpException.StatusCode),
            _ => false
        };
    }

    private static bool IsTransientStatusCode(HttpStatusCode? statusCode)
    {
        if (statusCode is null
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        return (int)statusCode.Value >= 500;
    }

    private TimeSpan GetRetryDelay(int failedAttempt)
    {
        if (_retryDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromMilliseconds(_retryDelay.TotalMilliseconds * failedAttempt);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
