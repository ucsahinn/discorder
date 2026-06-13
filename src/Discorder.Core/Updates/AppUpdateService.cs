using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Discorder.Core.Configuration;
using Discorder.Core.Provisioning;
using Discorder.Core.Security;

namespace Discorder.Core.Updates;

public sealed class AppUpdateService
{
    public static readonly Uri DefaultLatestReleaseUri = new(
        "https://api.github.com/repos/ucsahinn/discorder/releases/latest");

    private const string RepositoryOwner = "ucsahinn";
    private const string RepositoryName = "discorder";
    private const string GitHubHost = "github.com";
    private const string UpdaterExecutableName = "Discorder.Updater.exe";
    private const int MetadataMaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web);
    private static readonly TimeSpan MetadataRetryDelay = TimeSpan.FromSeconds(2);

    private readonly HttpClient _httpClient;
    private readonly AppPaths _paths;
    private readonly IVerifiedDownloader _downloader;
    private readonly Uri _latestReleaseUri;
    private readonly bool _requireUpdateAuthenticode;

    public AppUpdateService(
        HttpClient httpClient,
        AppPaths paths,
        IVerifiedDownloader downloader,
        Uri? latestReleaseUri = null,
        bool requireUpdateAuthenticode = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _latestReleaseUri = latestReleaseUri ?? DefaultLatestReleaseUri;
        _requireUpdateAuthenticode = requireUpdateAuthenticode;
    }

    public async Task<AppUpdateCheckResult> CheckLatestUpdateAsync(
        Version currentVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        var release = await FetchLatestReleaseAsync(cancellationToken);
        var latestVersion = ParseReleaseVersion(release.TagName);
        var normalizedCurrentVersion = NormalizeVersion(currentVersion);
        var normalizedLatestVersion = NormalizeVersion(latestVersion);
        var releaseUrl = release.HtmlUrl ?? string.Empty;

        if (normalizedLatestVersion <= normalizedCurrentVersion)
        {
            return AppUpdateCheckResult.UpToDate(
                normalizedCurrentVersion,
                normalizedLatestVersion,
                releaseUrl);
        }

        var versionText = FormatVersion(normalizedLatestVersion);
        var packageFileName = $"Discorder-{versionText}-win-x64.zip";
        var checksumFileName = $"Discorder-{versionText}-win-x64.sha256.txt";
        var packageAsset = FindAsset(release, packageFileName);
        var checksumAsset = FindAsset(release, checksumFileName);

        ValidateAsset(packageAsset, packageFileName, release.TagName, isPackage: true);
        ValidateAsset(checksumAsset, checksumFileName, release.TagName, isPackage: false);

        var packageUri = CreateAssetUri(packageAsset, packageFileName);
        var checksumUri = CreateAssetUri(checksumAsset, checksumFileName);
        var packageDigestSha256 = ParseSha256Digest(packageAsset, packageFileName);
        var expectedSha256 = await ReadExpectedSha256Async(
            checksumUri,
            checksumFileName,
            cancellationToken);

        if (!string.Equals(
                expectedSha256,
                packageDigestSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "GitHub asset özeti ile SHA-256 dosyası eşleşmiyor.");
        }

        return AppUpdateCheckResult.UpdateAvailable(
            normalizedCurrentVersion,
            normalizedLatestVersion,
            releaseUrl,
            packageFileName,
            packageUri,
            checksumFileName,
            checksumUri,
            expectedSha256,
            packageDigestSha256,
            packageAsset.Size!.Value);
    }

    public async Task<AppUpdatePreparation> PrepareCheckedUpdateAsync(
        AppUpdateCheckResult check,
        string applicationDirectory,
        string executableName,
        CancellationToken cancellationToken,
        IProgress<AppUpdateProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

        if (check.Status == AppUpdateCheckStatus.UpToDate)
        {
            return AppUpdatePreparation.UpToDate(
                check.CurrentVersion,
                check.LatestVersion,
                check.ReleaseUrl);
        }

        if (!Directory.Exists(applicationDirectory))
        {
            throw new DirectoryNotFoundException(applicationDirectory);
        }

        var packageFileName = Require(check.PackageFileName, nameof(check.PackageFileName));
        var packageUri = check.PackageUri
            ?? throw new InvalidOperationException("Güncelleme indirme adresi yok.");
        var expectedSha256 = Require(check.ExpectedSha256, nameof(check.ExpectedSha256));
        var packageDigestSha256 = Require(
            check.PackageDigestSha256,
            nameof(check.PackageDigestSha256));
        if (!string.Equals(
                expectedSha256,
                packageDigestSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Denetlenen güncelleme özeti tutarlı değil.");
        }

        if (check.PackageSizeBytes is null or <= 0)
        {
            throw new InvalidDataException(
                "Denetlenen güncelleme boyutu okunamadı.");
        }

        var versionText = FormatVersion(check.LatestVersion);
        progress?.Report(new AppUpdateProgress(
            10,
            $"v{versionText} hazırlanıyor",
            "Paket bilgileri doğrulandı."));
        var expectedSignerThumbprint = _requireUpdateAuthenticode
            ? AuthenticodeSignatureVerifier.GetRequiredSignerThumbprint(
                Path.Combine(applicationDirectory, executableName))
            : null;
        var updateDirectory = ProtectedUpdateStaging.CreateVersionDirectory(
            _paths.UpdateStagingDirectory,
            versionText,
            _paths.ProtectUpdateStaging);

        var packagePath = Path.Combine(updateDirectory, packageFileName);
        var lastReportedDownloadBucket = -1;
        var downloadProgress = new DirectProgress<DownloadProgress>(download =>
        {
            if (!string.IsNullOrWhiteSpace(download.Message))
            {
                var statusPercent = download.Percent is null
                    ? 20
                    : 20 + (download.Percent.Value * 0.45);
                progress?.Report(new AppUpdateProgress(
                    Math.Clamp(statusPercent, 20, 65),
                    download.IsRetry
                        ? "Bağlantı tekrar deneniyor"
                        : $"v{versionText} indiriliyor",
                    FormatDownloadStatusDetail(download)));
                return;
            }

            var percent = download.Percent is null
                ? 30
                : 20 + (download.Percent.Value * 0.45);
            percent = Math.Clamp(percent, 20, 65);
            var bucket = (int)Math.Floor(percent);
            if (bucket <= lastReportedDownloadBucket
                && download.Percent is not >= 100)
            {
                return;
            }

            lastReportedDownloadBucket = bucket;
            progress?.Report(new AppUpdateProgress(
                percent,
                $"v{versionText} indiriliyor",
                FormatDownloadDetail(download)));
        });
        await _downloader.DownloadAsync(
            packageUri,
            packagePath,
            expectedSha256,
            cancellationToken,
            check.PackageSizeBytes.Value,
            downloadProgress);

        var downloadedPackage = new FileInfo(packagePath);
        if (!downloadedPackage.Exists || downloadedPackage.Length != check.PackageSizeBytes.Value)
        {
            throw new InvalidDataException(
                "Güncelleme paketi beklenen boyutta değil.");
        }

        if (!string.Equals(
                UpdatePackageValidator.ComputeSha256(packagePath),
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Güncelleme paketi indirme sonrasında doğrulanamadı.");
        }

        progress?.Report(new AppUpdateProgress(
            70,
            "Paket doğrulanıyor",
            "SHA-256 ve GitHub digest eşleşti."));
        UpdatePackageValidator.ValidateArchive(
            packagePath,
            executableName,
            versionText);

        progress?.Report(new AppUpdateProgress(
            82,
            "Dosyalar hazırlanıyor",
            "Paket güvenli staging alanına açılıyor."));
        var extractionDirectory = Path.Combine(
            updateDirectory,
            "payload-" + DateTimeOffset.UtcNow.ToString(
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        UpdatePackageValidator.ExtractToDirectory(
            packagePath,
            extractionDirectory,
            executableName,
            versionText,
            expectedSignerThumbprint,
            expectedSha256);

        var applicatorPath = CopyApplicator(
            applicationDirectory,
            updateDirectory);
        progress?.Report(new AppUpdateProgress(
            94,
            "Yükleme yardımcısı hazırlanıyor",
            "Discorder birazdan kapanıp yeni sürümle açılacak."));

        return AppUpdatePreparation.Prepared(
            check.CurrentVersion,
            check.LatestVersion,
            check.ReleaseUrl,
            packagePath,
            extractionDirectory,
            expectedSha256,
            expectedSignerThumbprint,
            applicatorPath);
    }

    public async Task<AppUpdatePreparation> PrepareLatestUpdateAsync(
        Version currentVersion,
        string applicationDirectory,
        string executableName,
        CancellationToken cancellationToken)
    {
        var check = await CheckLatestUpdateAsync(
            currentVersion,
            cancellationToken);

        return await PrepareCheckedUpdateAsync(
            check,
            applicationDirectory,
            executableName,
            cancellationToken);
    }

    public static string FormatVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (version.Build >= 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{version.Major}.{version.Minor}.{version.Build}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{version.Major}.{version.Minor}");
    }

    private async Task<GitHubRelease> FetchLatestReleaseAsync(
        CancellationToken cancellationToken)
    {
        return await ExecuteUpdateRequestWithRetryAsync(
            CreateLatestReleaseRequest,
            async (response, token) =>
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"{_latestReleaseUri.Host} HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                        null,
                        response.StatusCode);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(
                    token);
                var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                    stream,
                    JsonOptions,
                    token);

                if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                {
                    throw new InvalidDataException(
                        "GitHub release bilgisi okunamadı.");
                }

                return release;
            },
            cancellationToken);
    }

    private HttpRequestMessage CreateLatestReleaseRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _latestReleaseUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            "application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private async Task<T> ExecuteUpdateRequestWithRetryAsync<T>(
        Func<HttpRequestMessage> createRequest,
        Func<HttpResponseMessage, CancellationToken, Task<T>> readResponse,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MetadataMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = createRequest();

            try
            {
                var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                using var _ = response;
                if (!response.IsSuccessStatusCode
                    && attempt < MetadataMaxAttempts
                    && IsTransientStatusCode(response.StatusCode))
                {
                    lastException = new HttpRequestException(
                        $"{request.RequestUri?.Host ?? "update"} HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                        null,
                        response.StatusCode);
                }
                else
                {
                    return await readResponse(response, cancellationToken);
                }
            }
            catch (Exception exception) when (
                attempt < MetadataMaxAttempts
                && IsTransientUpdateFailure(exception, cancellationToken))
            {
                lastException = exception;
            }

            await Task.Delay(GetMetadataRetryDelay(attempt), cancellationToken);
        }

        throw new InvalidOperationException(
            "Güncelleme bilgisi alınamadı.",
            lastException);
    }

    private static bool IsTransientUpdateFailure(
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
            IOException => true,
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

    private static TimeSpan GetMetadataRetryDelay(int failedAttempt)
    {
        return TimeSpan.FromMilliseconds(
            MetadataRetryDelay.TotalMilliseconds * failedAttempt);
    }

    private static Version ParseReleaseVersion(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new InvalidDataException("GitHub release etiketi boş.");
        }

        var versionText = tagName.Trim();
        if (versionText.StartsWith('v') || versionText.StartsWith('V'))
        {
            versionText = versionText[1..];
        }

        if (!Version.TryParse(versionText, out var version))
        {
            throw new InvalidDataException(
                $"GitHub release etiketi geçerli sürüm değil: {tagName}");
        }

        return version;
    }

    private static Version NormalizeVersion(Version version)
    {
        return new Version(
            version.Major,
            version.Minor,
            version.Build < 0 ? 0 : version.Build,
            version.Revision < 0 ? 0 : version.Revision);
    }

    private static GitHubReleaseAsset FindAsset(
        GitHubRelease release,
        string assetName)
    {
        var assets = release.Assets?
            .Where(item => string.Equals(
                item.Name,
                assetName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];

        return assets.Length switch
        {
            1 => assets[0],
            0 => throw new InvalidDataException(
                $"GitHub release {assetName} dosyasını içermiyor."),
            _ => throw new InvalidDataException(
                $"GitHub release {assetName} dosyasını birden fazla içeriyor.")
        };
    }

    private static void ValidateAsset(
        GitHubReleaseAsset asset,
        string assetName,
        string? releaseTag,
        bool isPackage)
    {
        if (!string.Equals(
                asset.State,
                "uploaded",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{assetName} GitHub tarafında hazır değil.");
        }

        if (asset.Size is null or <= 0)
        {
            throw new InvalidDataException(
                $"{assetName} boyutu okunamadı.");
        }

        var maxSize = isPackage
            ? UpdatePackageValidator.MaxPackageBytes
            : 1024L * 1024L;
        if (asset.Size > maxSize)
        {
            throw new InvalidDataException(
                $"{assetName} beklenenden büyük.");
        }

        if (isPackage)
        {
            _ = ParseSha256Digest(asset, assetName);
        }

        var uri = CreateAssetUri(asset, assetName);
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, GitHubHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{assetName} GitHub indirme adresi güvenli değil.");
        }

        var expectedPath = string.Join(
            '/',
            RepositoryOwner,
            RepositoryName,
            "releases",
            "download",
            releaseTag,
            assetName);
        var actualPath = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (!string.Equals(
                actualPath,
                expectedPath,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{assetName} beklenen release yolundan indirilmiyor.");
        }
    }

    private static Uri CreateAssetUri(
        GitHubReleaseAsset asset,
        string assetName)
    {
        if (!Uri.TryCreate(
                asset.BrowserDownloadUrl,
                UriKind.Absolute,
                out var uri))
        {
            throw new InvalidDataException(
                $"{assetName} indirme adresi geçerli değil.");
        }

        return uri;
    }

    private async Task<string> ReadExpectedSha256Async(
        Uri checksumUri,
        string checksumFileName,
        CancellationToken cancellationToken)
    {
        return await ExecuteUpdateRequestWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, checksumUri),
            async (response, token) =>
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"{checksumUri.Host} HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                        null,
                        response.StatusCode);
                }

                if (response.Content.Headers.ContentLength is > 8192)
                {
                    throw new InvalidDataException(
                        $"{checksumFileName} beklenenden büyük.");
                }

                var checksumText = await ReadLimitedTextAsync(
                    response.Content,
                    checksumFileName,
                    maxBytes: 8192,
                    token);

                foreach (var tokenText in checksumText.Split(
                             [' ', '\t', '\r', '\n'],
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    if (tokenText.Length == 64 && tokenText.All(Uri.IsHexDigit))
                    {
                        return tokenText.ToUpperInvariant();
                    }
                }

                throw new InvalidDataException(
                    $"{checksumFileName} geçerli SHA-256 içermiyor.");
            },
            cancellationToken);
    }

    private static async Task<string> ReadLimitedTextAsync(
        HttpContent content,
        string sourceName,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        var totalBytes = 0;
        while (true)
        {
            var bytesRead = await stream.ReadAsync(chunk, cancellationToken);
            if (bytesRead == 0)
            {
                return Encoding.UTF8.GetString(buffer.ToArray());
            }

            totalBytes += bytesRead;
            if (totalBytes > maxBytes)
            {
                throw new InvalidDataException(
                    $"{sourceName} beklenenden büyük.");
            }

            buffer.Write(chunk, 0, bytesRead);
        }
    }

    private static string ParseSha256Digest(
        GitHubReleaseAsset asset,
        string assetName)
    {
        const string prefix = "sha256:";
        if (string.IsNullOrWhiteSpace(asset.Digest)
            || !asset.Digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{assetName} GitHub SHA-256 digest bilgisi içermiyor.");
        }

        var digest = asset.Digest[prefix.Length..];
        if (digest.Length != 64 || !digest.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                $"{assetName} GitHub SHA-256 digest bilgisi geçerli değil.");
        }

        return digest.ToUpperInvariant();
    }

    private static string CopyApplicator(
        string applicationDirectory,
        string updateDirectory)
    {
        var sourceDirectory = Path.GetFullPath(applicationDirectory);
        var applicatorDirectory = Path.Combine(
            updateDirectory,
            "applicator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(applicatorDirectory);

        var updaterFiles = EnumerateApplicatorFiles(sourceDirectory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var source in updaterFiles)
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, source);
            var destination = Path.Combine(applicatorDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(
                source,
                destination,
                overwrite: true);
        }

        var applicatorPath = Path.Combine(
            applicatorDirectory,
            UpdaterExecutableName);
        if (!File.Exists(applicatorPath))
        {
            throw new FileNotFoundException(
                "Güncelleme yardımcısı bulunamadı.",
                applicatorPath);
        }

        return applicatorPath;
    }

    private static IEnumerable<string> EnumerateApplicatorFiles(string sourceDirectory)
    {
        foreach (var path in Directory.EnumerateFiles(
                     sourceDirectory,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            yield return path;
        }

        foreach (var directoryName in new[] { "runtimes" })
        {
            var directory = Path.Combine(sourceDirectory, directoryName);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(
                         directory,
                         "*",
                         SearchOption.AllDirectories))
            {
                yield return path;
            }
        }
    }

    private static string Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Güncelleme bilgisi eksik: {name}");
        }

        return value;
    }

    private static string FormatDownloadDetail(DownloadProgress progress)
    {
        if (progress.TotalBytes is null or <= 0)
        {
            return $"{FormatBytes(progress.BytesReceived)} indirildi.";
        }

        return $"{FormatBytes(progress.BytesReceived)} / {FormatBytes(progress.TotalBytes.Value)} indirildi.";
    }

    private static string FormatDownloadStatusDetail(DownloadProgress progress)
    {
        var message = progress.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return FormatDownloadDetail(progress);
        }

        if (progress.Attempt is > 0
            && progress.MaxAttempts is > 1)
        {
            return $"{message} Deneme {progress.Attempt}/{progress.MaxAttempts}.";
        }

        return message;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{value:0} {units[unitIndex]}")
            : string.Create(CultureInfo.InvariantCulture, $"{value:0.0} {units[unitIndex]}");
    }

    private sealed class DirectProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public DirectProgress(Action<T> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Report(T value)
        {
            _handler(value);
        }
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("assets")] GitHubReleaseAsset[]? Assets);

    private sealed record GitHubReleaseAsset(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("size")] long? Size,
        [property: JsonPropertyName("digest")] string? Digest);
}
