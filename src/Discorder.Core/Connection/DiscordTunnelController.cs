using Discorder.Core.Configuration;
using Discorder.Core.Discord;
using Discorder.Core.Infrastructure;
using Discorder.Core.Provisioning;
using Discorder.Core.WireSock;
using System.Globalization;

namespace Discorder.Core.Connection;

public sealed class DiscordTunnelController : IAsyncDisposable
{
    private readonly AppPaths _paths;
    private readonly DiscordAppScope _discordScope;
    private readonly IWireSockBootstrapper _wireSockBootstrapper;
    private readonly IProfileProvisioner _provisioner;
    private readonly IProcessLauncher _processLauncher;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly TimeSpan _startupGracePeriod;

    private IManagedProcess? _wireSockProcess;
    private TunnelSnapshot _snapshot = new(
        TunnelState.Disconnected,
        "Bağlantı kapalı",
        DateTimeOffset.Now);
    private bool _disposed;
    private bool _intentionalStop;

    public DiscordTunnelController(
        AppPaths paths,
        DiscordAppScope discordScope,
        IWireSockBootstrapper wireSockBootstrapper,
        IProfileProvisioner provisioner,
        IProcessLauncher processLauncher,
        TimeSpan? startupGracePeriod = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _discordScope = discordScope ?? throw new ArgumentNullException(nameof(discordScope));
        _wireSockBootstrapper = wireSockBootstrapper
            ?? throw new ArgumentNullException(nameof(wireSockBootstrapper));
        _provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
        _processLauncher = processLauncher
            ?? throw new ArgumentNullException(nameof(processLauncher));
        _startupGracePeriod = startupGracePeriod ?? TimeSpan.FromSeconds(2);
    }

    public event EventHandler<TunnelSnapshot>? StatusChanged;

    public TunnelSnapshot Snapshot => _snapshot;

    public async Task ToggleAsync(CancellationToken cancellationToken = default)
    {
        if (_snapshot.IsConnected)
        {
            await DisconnectAsync(cancellationToken);
        }
        else
        {
            await ConnectAsync(cancellationToken);
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken);

        try
        {
            if (_snapshot.IsConnected || _snapshot.IsBusy)
            {
                return;
            }

            _intentionalStop = false;
            SetStatus(TunnelState.Preparing, "Discord tüneli hazırlanıyor");

            var progress = new CallbackProgress(
                message => SetStatus(TunnelState.Preparing, message));
            var wireSockExecutable = await _wireSockBootstrapper.EnsureInstalledAsync(
                progress,
                cancellationToken);

            var allowedApplications = _discordScope.GetAllowedApplications();
            var profilePath = await _provisioner.EnsureProfileAsync(
                allowedApplications,
                cancellationToken);

            SetStatus(TunnelState.Connecting, "Discord ağına bağlanılıyor");
            var arguments = await PrepareWireSockArgumentsAsync(
                wireSockExecutable,
                profilePath,
                cancellationToken);
            _wireSockProcess = _processLauncher.Start(
                wireSockExecutable,
                arguments,
                Path.GetDirectoryName(wireSockExecutable)!,
                _paths.TunnelLog);
            _wireSockProcess.Exited += OnWireSockExited;

            await Task.Delay(_startupGracePeriod, cancellationToken);

            if (_wireSockProcess.HasExited)
            {
                var exitCode = _wireSockProcess.ExitCode;
                await DisposeProcessAsync();
                throw new InvalidOperationException(
                    $"WireSock bağlantı kurulmadan kapandı. Çıkış kodu: " +
                    $"{exitCode?.ToString(CultureInfo.InvariantCulture) ?? "bilinmiyor"}.");
            }

            SetStatus(
                TunnelState.Connected,
                "Discord uygulaması ve web erişimi tünelleniyor");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisposeProcessAsync();
            SetStatus(TunnelState.Disconnected, "Bağlantı iptal edildi");
            throw;
        }
        catch (Exception exception)
        {
            await DisposeProcessAsync();
            WriteDiagnostic("Connect", exception.ToString());
            SetStatus(
                TunnelState.Error,
                exception.Message,
                exception.ToString());
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static Task<IReadOnlyList<string>> PrepareWireSockArgumentsAsync(
        string wireSockExecutable,
        string profilePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wireSockExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<string>>([
            "run",
            "-config",
            profilePath,
            "-log-level",
            "error"
        ]);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken);

        try
        {
            if (_wireSockProcess is null)
            {
                SetStatus(TunnelState.Disconnected, "Bağlantı kapalı");
                return;
            }

            _intentionalStop = true;
            SetStatus(TunnelState.Disconnecting, "Bağlantı kapatılıyor");

            if (_wireSockProcess is not null)
            {
                await _wireSockProcess.StopAsync(
                    TimeSpan.FromSeconds(3),
                    cancellationToken);
            }

            await DisposeProcessAsync();

            SetStatus(TunnelState.Disconnected, "Bağlantı kapalı");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(
                TunnelState.Error,
                "Bağlantı kapatma işlemi iptal edildi.");
            throw;
        }
        catch (Exception exception)
        {
            WriteDiagnostic("Disconnect", exception.ToString());
            SetStatus(
                TunnelState.Error,
                "Bağlantı kapatılamadı.",
                exception.ToString());
        }
        finally
        {
            _intentionalStop = false;
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _operationGate.WaitAsync();
        try
        {
            _disposed = true;
            _intentionalStop = true;

            if (_wireSockProcess is not null)
            {
                await _wireSockProcess.StopAsync(
                    TimeSpan.FromSeconds(2),
                    CancellationToken.None);
                await DisposeProcessAsync();
            }
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }

    private async void OnWireSockExited(object? sender, EventArgs e)
    {
        if (_intentionalStop || _disposed)
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
            try
            {
                if (_intentionalStop || _disposed)
                {
                    return;
                }

                var exitCode = _wireSockProcess?.ExitCode;
                await DisposeProcessAsync();
                WriteDiagnostic(
                    "WireSockExited",
                    $"WireSock exit code: " +
                    $"{exitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
                SetStatus(
                    TunnelState.Error,
                    "Tünel beklenmedik şekilde kapandı.",
                    $"WireSock exit code: " +
                    $"{exitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task DisposeProcessAsync()
    {
        if (_wireSockProcess is null)
        {
            return;
        }

        _wireSockProcess.Exited -= OnWireSockExited;
        await _wireSockProcess.DisposeAsync();
        _wireSockProcess = null;
    }

    private void SetStatus(
        TunnelState state,
        string message,
        string? diagnostic = null)
    {
        _snapshot = new TunnelSnapshot(
            state,
            message,
            DateTimeOffset.Now,
            diagnostic);
        StatusChanged?.Invoke(this, _snapshot);
    }

    private void WriteDiagnostic(string operation, string diagnostic)
    {
        try
        {
            _paths.EnsureDirectories();
            File.AppendAllText(
                _paths.ErrorLog,
                $"[{DateTimeOffset.Now:O}] {operation}{Environment.NewLine}" +
                $"{diagnostic}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class CallbackProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value)
        {
            report(value);
        }
    }
}
