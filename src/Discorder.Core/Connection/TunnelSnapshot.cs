namespace Discorder.Core.Connection;

public sealed record TunnelSnapshot(
    TunnelState State,
    string Message,
    DateTimeOffset ChangedAt,
    string? Diagnostic = null)
{
    public bool IsBusy => State is TunnelState.Preparing
        or TunnelState.Connecting
        or TunnelState.Disconnecting;

    public bool IsConnected => State is TunnelState.Connected;
}
