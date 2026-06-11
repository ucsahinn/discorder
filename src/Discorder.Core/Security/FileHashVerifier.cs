using System.Security.Cryptography;

namespace Discorder.Core.Security;

public static class FileHashVerifier
{
    public static async Task<bool> MatchesSha256Async(
        string path,
        string expectedHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);

        if (!File.Exists(path))
        {
            return false;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).Equals(
            expectedHash,
            StringComparison.OrdinalIgnoreCase);
    }
}
