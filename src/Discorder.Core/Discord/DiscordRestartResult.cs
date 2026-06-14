namespace Discorder.Core.Discord;

public enum DiscordRestartFailureKind
{
    None,
    UpdaterWindow,
    Unknown
}

public sealed record DiscordRestartResult(
    bool Restarted,
    string Message,
    string? Diagnostic = null,
    DiscordRestartFailureKind FailureKind = DiscordRestartFailureKind.None)
{
    public static DiscordRestartResult NotNeeded() =>
        new(true, "Discord zaten kapalıydı.");

    public static DiscordRestartResult NoExecutablePath() =>
        new(false, "Discord yolu bulunamadı.", "Discord process path could not be inspected.");
}
