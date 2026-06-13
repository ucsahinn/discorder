namespace Discorder.Core.Discord;

public sealed record DiscordRestartResult(
    bool Restarted,
    string Message,
    string? Diagnostic = null)
{
    public static DiscordRestartResult NotNeeded() =>
        new(true, "Discord zaten kapalıydı.");

    public static DiscordRestartResult NoExecutablePath() =>
        new(false, "Discord yolu bulunamadı.", "Discord process path could not be inspected.");
}
