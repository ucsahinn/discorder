namespace Discorder.Core.Firewall;

public interface IDiscordAccessLock
{
    Task EnableAsync(CancellationToken cancellationToken);

    Task DisableAsync(CancellationToken cancellationToken);

    Task RemoveAsync(CancellationToken cancellationToken);
}
