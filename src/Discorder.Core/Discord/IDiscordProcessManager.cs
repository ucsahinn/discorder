namespace Discorder.Core.Discord;

public interface IDiscordProcessManager : IDiscordProcessInspector
{
    Task<DiscordRestartResult> RestartAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken);
}
