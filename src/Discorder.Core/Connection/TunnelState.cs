namespace Discorder.Core.Connection;

public enum TunnelState
{
    Disconnected,
    Preparing,
    Connecting,
    Connected,
    Disconnecting,
    Error
}
