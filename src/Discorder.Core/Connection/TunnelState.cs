namespace Discorder.Core.Connection;

public enum TunnelState
{
    Disconnected,
    Preparing,
    Connecting,
    Verifying,
    Connected,
    Disconnecting,
    Error
}
