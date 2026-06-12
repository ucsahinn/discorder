namespace Discorder.Core.Updates;

public enum AppUpdateCheckStatus
{
    UpToDate,
    UpdateAvailable
}

public enum AppUpdatePreparationStatus
{
    UpToDate,
    Prepared
}

public sealed record AppUpdateCheckResult(
    AppUpdateCheckStatus Status,
    Version CurrentVersion,
    Version LatestVersion,
    string ReleaseUrl,
    string? PackageFileName,
    Uri? PackageUri,
    string? ChecksumFileName,
    Uri? ChecksumUri,
    string? ExpectedSha256,
    string? PackageDigestSha256,
    long? PackageSizeBytes)
{
    public static AppUpdateCheckResult UpToDate(
        Version currentVersion,
        Version latestVersion,
        string releaseUrl)
    {
        return new AppUpdateCheckResult(
            AppUpdateCheckStatus.UpToDate,
            currentVersion,
            latestVersion,
            releaseUrl,
            PackageFileName: null,
            PackageUri: null,
            ChecksumFileName: null,
            ChecksumUri: null,
            ExpectedSha256: null,
            PackageDigestSha256: null,
            PackageSizeBytes: null);
    }

    public static AppUpdateCheckResult UpdateAvailable(
        Version currentVersion,
        Version latestVersion,
        string releaseUrl,
        string packageFileName,
        Uri packageUri,
        string checksumFileName,
        Uri checksumUri,
        string expectedSha256,
        string packageDigestSha256,
        long packageSizeBytes)
    {
        return new AppUpdateCheckResult(
            AppUpdateCheckStatus.UpdateAvailable,
            currentVersion,
            latestVersion,
            releaseUrl,
            packageFileName,
            packageUri,
            checksumFileName,
            checksumUri,
            expectedSha256,
            packageDigestSha256,
            packageSizeBytes);
    }
}

public sealed record AppUpdatePreparation(
    AppUpdatePreparationStatus Status,
    Version CurrentVersion,
    Version LatestVersion,
    string ReleaseUrl,
    string? PackagePath,
    string? PayloadDirectory,
    string? ExpectedSha256,
    string? ExpectedSignerThumbprint,
    string? ApplicatorPath)
{
    public static AppUpdatePreparation UpToDate(
        Version currentVersion,
        Version latestVersion,
        string releaseUrl)
    {
        return new AppUpdatePreparation(
            AppUpdatePreparationStatus.UpToDate,
            currentVersion,
            latestVersion,
            releaseUrl,
            PackagePath: null,
            PayloadDirectory: null,
            ExpectedSha256: null,
            ExpectedSignerThumbprint: null,
            ApplicatorPath: null);
    }

    public static AppUpdatePreparation Prepared(
        Version currentVersion,
        Version latestVersion,
        string releaseUrl,
        string packagePath,
        string payloadDirectory,
        string expectedSha256,
        string? expectedSignerThumbprint,
        string applicatorPath)
    {
        return new AppUpdatePreparation(
            AppUpdatePreparationStatus.Prepared,
            currentVersion,
            latestVersion,
            releaseUrl,
            packagePath,
            payloadDirectory,
            expectedSha256,
            expectedSignerThumbprint,
            applicatorPath);
    }
}
