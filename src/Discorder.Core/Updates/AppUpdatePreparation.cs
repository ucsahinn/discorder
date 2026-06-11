namespace Discorder.Core.Updates;

public enum AppUpdatePreparationStatus
{
    UpToDate,
    Prepared
}

public sealed record AppUpdatePreparation(
    AppUpdatePreparationStatus Status,
    Version CurrentVersion,
    Version LatestVersion,
    string ReleaseUrl,
    string? PackagePath,
    string? PayloadDirectory,
    string? ExpectedSha256,
    string? ScriptPath)
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
            ScriptPath: null);
    }

    public static AppUpdatePreparation Prepared(
        Version currentVersion,
        Version latestVersion,
        string releaseUrl,
        string packagePath,
        string payloadDirectory,
        string expectedSha256,
        string scriptPath)
    {
        return new AppUpdatePreparation(
            AppUpdatePreparationStatus.Prepared,
            currentVersion,
            latestVersion,
            releaseUrl,
            packagePath,
            payloadDirectory,
            expectedSha256,
            scriptPath);
    }
}
