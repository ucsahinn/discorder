using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Discorder.Core.Configuration;
using Discorder.Core.Provisioning;

namespace Discorder.Core.Updates;

public sealed class AppUpdateService
{
    public static readonly Uri DefaultLatestReleaseUri = new(
        "https://api.github.com/repos/ucsahinn/discorder/releases/latest");

    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly AppPaths _paths;
    private readonly IVerifiedDownloader _downloader;
    private readonly Uri _latestReleaseUri;

    public AppUpdateService(
        HttpClient httpClient,
        AppPaths paths,
        IVerifiedDownloader downloader,
        Uri? latestReleaseUri = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _latestReleaseUri = latestReleaseUri ?? DefaultLatestReleaseUri;
    }

    public async Task<AppUpdatePreparation> PrepareLatestUpdateAsync(
        Version currentVersion,
        string applicationDirectory,
        string executableName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

        if (!Directory.Exists(applicationDirectory))
        {
            throw new DirectoryNotFoundException(applicationDirectory);
        }

        var release = await FetchLatestReleaseAsync(cancellationToken);
        var latestVersion = ParseReleaseVersion(release.TagName);
        var normalizedCurrentVersion = NormalizeVersion(currentVersion);
        var normalizedLatestVersion = NormalizeVersion(latestVersion);
        var releaseUrl = release.HtmlUrl ?? string.Empty;

        if (normalizedLatestVersion <= normalizedCurrentVersion)
        {
            return AppUpdatePreparation.UpToDate(
                normalizedCurrentVersion,
                normalizedLatestVersion,
                releaseUrl);
        }

        var versionText = FormatVersion(normalizedLatestVersion);
        var packageFileName = $"Discorder-{versionText}-win-x64.zip";
        var checksumFileName = $"Discorder-{versionText}-win-x64.sha256.txt";
        var packageAsset = FindAsset(release, packageFileName);
        var checksumAsset = FindAsset(release, checksumFileName);
        var packageUri = CreateAssetUri(packageAsset, packageFileName);
        var checksumUri = CreateAssetUri(checksumAsset, checksumFileName);
        var expectedSha256 = await ReadExpectedSha256Async(
            checksumUri,
            checksumFileName,
            cancellationToken);

        var updateDirectory = Path.Combine(
            _paths.DataDirectory,
            "updates",
            versionText);
        Directory.CreateDirectory(updateDirectory);

        var packagePath = Path.Combine(updateDirectory, packageFileName);
        await _downloader.DownloadAsync(
            packageUri,
            packagePath,
            expectedSha256,
            cancellationToken);

        var extractionDirectory = Path.Combine(
            updateDirectory,
            "payload-" + DateTimeOffset.UtcNow.ToString(
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractionDirectory);
        ZipFile.ExtractToDirectory(packagePath, extractionDirectory);

        var extractedExecutable = Path.Combine(extractionDirectory, executableName);
        if (!File.Exists(extractedExecutable))
        {
            throw new InvalidDataException(
                $"{packageFileName} does not contain {executableName}.");
        }

        var scriptPath = await WriteUpdateScriptAsync(
            updateDirectory,
            versionText,
            cancellationToken);

        return AppUpdatePreparation.Prepared(
            normalizedCurrentVersion,
            normalizedLatestVersion,
            releaseUrl,
            packagePath,
            extractionDirectory,
            expectedSha256,
            scriptPath);
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
        using var request = new HttpRequestMessage(HttpMethod.Get, _latestReleaseUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            "application/vnd.github+json"));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{_latestReleaseUri.Host} HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
            stream,
            JsonOptions,
            cancellationToken);

        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
        {
            throw new InvalidDataException(
                "GitHub release metadata could not be read.");
        }

        return release;
    }

    private static Version ParseReleaseVersion(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new InvalidDataException("GitHub release tag is empty.");
        }

        var versionText = tagName.Trim();
        if (versionText.StartsWith('v') || versionText.StartsWith('V'))
        {
            versionText = versionText[1..];
        }

        if (!Version.TryParse(versionText, out var version))
        {
            throw new InvalidDataException(
                $"GitHub release tag is not a valid version: {tagName}");
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
        var asset = release.Assets?.FirstOrDefault(item => string.Equals(
            item.Name,
            assetName,
            StringComparison.OrdinalIgnoreCase));

        if (asset is null)
        {
            throw new InvalidDataException(
                $"GitHub release does not contain asset {assetName}.");
        }

        return asset;
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
                $"{assetName} download URL is invalid.");
        }

        return uri;
    }

    private async Task<string> ReadExpectedSha256Async(
        Uri checksumUri,
        string checksumFileName,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            checksumUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{checksumUri.Host} HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                null,
                response.StatusCode);
        }

        var checksumText = await response.Content.ReadAsStringAsync(
            cancellationToken);
        foreach (var token in checksumText.Split(
                     [' ', '\t', '\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length == 64 && token.All(Uri.IsHexDigit))
            {
                return token.ToUpperInvariant();
            }
        }

        throw new InvalidDataException(
            $"{checksumFileName} does not contain a valid SHA-256.");
    }

    private static async Task<string> WriteUpdateScriptAsync(
        string updateDirectory,
        string versionText,
        CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(
            updateDirectory,
            $"apply-update-{versionText}-{Guid.NewGuid():N}.ps1");
        var script = """
param(
    [Parameter(Mandatory=$true)][int]$ProcessId,
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [Parameter(Mandatory=$true)][string]$TargetDirectory,
    [Parameter(Mandatory=$true)][string]$ExecutableName,
    [Parameter(Mandatory=$true)][string]$LogPath
)

$ErrorActionPreference = 'Stop'

function Write-DiscorderUpdateLog {
    param([string]$Message)
    $line = ('{0:O} {1}' -f [DateTimeOffset]::Now, $Message)
    Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
}

try {
    Write-DiscorderUpdateLog 'Waiting for Discorder to exit.'
    try {
        Wait-Process -Id $ProcessId -Timeout 90 -ErrorAction SilentlyContinue
    } catch {
        Write-DiscorderUpdateLog ('Wait-Process returned: ' + $_.Exception.Message)
    }

    if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
        throw 'Discorder process did not exit before the update timeout.'
    }

    if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
        throw ('Update source not found: ' + $SourceDirectory)
    }

    if (-not (Test-Path -LiteralPath $TargetDirectory -PathType Container)) {
        throw ('Update target not found: ' + $TargetDirectory)
    }

    Write-DiscorderUpdateLog 'Copying update payload.'
    Get-ChildItem -LiteralPath $SourceDirectory -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $TargetDirectory -Recurse -Force
    }

    $executablePath = Join-Path -Path $TargetDirectory -ChildPath $ExecutableName
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw ('Updated executable not found: ' + $executablePath)
    }

    Write-DiscorderUpdateLog 'Starting updated Discorder.'
    Start-Process -FilePath $executablePath -WorkingDirectory $TargetDirectory
    Write-DiscorderUpdateLog 'Update applied successfully.'
    exit 0
} catch {
    Write-DiscorderUpdateLog ('Update failed: ' + $_.Exception.Message)
    exit 1
}
""";

        await File.WriteAllTextAsync(
            scriptPath,
            script,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        return scriptPath;
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("assets")] GitHubReleaseAsset[]? Assets);

    private sealed record GitHubReleaseAsset(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl);
}
