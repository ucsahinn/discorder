namespace Discorder.Core.Connection;

public enum TunnelState
{
    Disconnected,
    Preparing,
    Connecting,
    Verifying,
    DiscordRestartRequired,
    Connected,
    Disconnecting,
    Error
}
