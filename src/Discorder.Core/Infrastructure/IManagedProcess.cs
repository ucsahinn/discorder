namespace Discorder.Core.Infrastructure;

public interface IManagedProcess : IAsyncDisposable
{
    event EventHandler? Exited;

    bool HasExited { get; }

    int? ExitCode { get; }

    Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
