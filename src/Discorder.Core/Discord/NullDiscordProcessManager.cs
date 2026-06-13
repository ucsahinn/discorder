namespace Discorder.Core.Discord;

public sealed class NullDiscordProcessManager : IDiscordProcessManager
{
    public DiscordProcessSnapshot Capture() => new(0, []);

    public Task<DiscordRestartResult> RestartAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken) =>
        Task.FromResult(DiscordRestartResult.NotNeeded());

    public Task<DiscordRestartResult> CloseAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken) =>
        Task.FromResult(DiscordRestartResult.NotNeeded());
}
