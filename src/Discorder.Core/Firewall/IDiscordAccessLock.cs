namespace Discorder.Core.Firewall;

public interface IDiscordAccessLock
{
    Task EnableAsync(CancellationToken cancellationToken);

    Task DisableAsync(CancellationToken cancellationToken);

    Task ApplyTunnelScopeAsync(
        bool includeBrowserAccess,
        CancellationToken cancellationToken);

    Task ClearTunnelScopeAsync(CancellationToken cancellationToken);

    Task RemoveAsync(CancellationToken cancellationToken);
}
