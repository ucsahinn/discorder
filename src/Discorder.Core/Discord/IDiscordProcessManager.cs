namespace Discorder.Core.Discord;

public interface IDiscordProcessManager : IDiscordProcessInspector
{
    Task<DiscordRestartResult> RestartAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken);

    Task<DiscordRestartResult> CloseAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken);
}
