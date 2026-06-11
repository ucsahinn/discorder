using Discorder.Core.Configuration;
using Discorder.Core.Diagnostics;
using Discorder.Core.Discord;
using Discorder.Core.Firewall;
using Discorder.Core.Infrastructure;
using Discorder.Core.Provisioning;
using Discorder.Core.WireSock;
using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;

namespace Discorder.Core.Connection;

public sealed class DiscordTunnelController : IAsyncDisposable
{
    private readonly AppPaths _paths;
    private readonly DiscordAppScope _discordScope;
    private readonly IWireSockBootstrapper _wireSockBootstrapper;
    private readonly IProfileProvisioner _provisioner;
    private readonly IProcessLauncher _processLauncher;
    private readonly IDiscordAccessLock _accessLock;
    private readonly IDiscorderDiagnostics _diagnostics;
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
        TimeSpan? startupGracePeriod = null,
        IDiscordAccessLock? accessLock = null,
        IDiscorderDiagnostics? diagnostics = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _discordScope = discordScope ?? throw new ArgumentNullException(nameof(discordScope));
        _wireSockBootstrapper = wireSockBootstrapper
            ?? throw new ArgumentNullException(nameof(wireSockBootstrapper));
        _provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
        _processLauncher = processLauncher
            ?? throw new ArgumentNullException(nameof(processLauncher));
        _accessLock = accessLock ?? new NullDiscordAccessLock();
        _diagnostics = diagnostics ?? NullDiscorderDiagnostics.Instance;
        _startupGracePeriod = startupGracePeriod ?? TimeSpan.FromSeconds(2);
    }

    public event EventHandler<TunnelSnapshot>? StatusChanged;

    public TunnelSnapshot Snapshot => _snapshot;

    private bool _includeBrowserAccess;

    public bool IncludeBrowserAccess
    {
        get => _includeBrowserAccess;
        set
        {
            if (!TrySetBrowserAccess(value))
            {
                throw new InvalidOperationException(
                    "Discord web kapsamı yalnızca bağlantı kapalıyken değiştirilebilir.");
            }
        }
    }

    public bool TrySetBrowserAccess(bool enabled)
    {
        if (_snapshot.IsConnected || _snapshot.IsBusy)
        {
            _diagnostics.Warning(
                "controller.browserAccess",
                "Bağlantı aktifken Discord web kapsamı değiştirme isteği reddedildi.",
                new Dictionary<string, string?>
                {
                    ["requested"] = enabled.ToString(),
                    ["current"] = _includeBrowserAccess.ToString(),
                    ["state"] = _snapshot.State.ToString()
                });
            return false;
        }

        _includeBrowserAccess = enabled;
        return true;
    }

    public async Task EnsureDisconnectedLockAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken);

        try
        {
            if (_snapshot.IsConnected || _snapshot.IsBusy)
            {
                return;
            }

            _diagnostics.Info("controller.lock", "Discord kilidi etkinleştiriliyor.");
            await _accessLock.EnableAsync(cancellationToken);
            SetStatus(TunnelState.Disconnected, "Bağlantı kapalı, Discord kilitli");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            WriteDiagnostic("EnableAccessLock", exception.ToString());
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
            SetStatus(TunnelState.Preparing, "Discord kilidi kaldırılıyor");
            _diagnostics.Info(
                "controller.connect",
                "Bağlantı başlatıldı.",
                new Dictionary<string, string?>
                {
                    ["browserAccess"] = IncludeBrowserAccess.ToString()
                });
            await _accessLock.DisableAsync(cancellationToken);
            await _accessLock.ApplyTunnelScopeAsync(
                IncludeBrowserAccess,
                cancellationToken);

            SetStatus(TunnelState.Preparing, "Discord tüneli hazırlanıyor");

            var progress = new CallbackProgress(
                message =>
                {
                    _diagnostics.Info("controller.prepare", message);
                    SetStatus(TunnelState.Preparing, message);
                });
            var wireSockExecutable = await _wireSockBootstrapper.EnsureInstalledAsync(
                progress,
                cancellationToken);

            var allowedApplications = _discordScope.GetAllowedApplications(
                IncludeBrowserAccess);
            var profilePath = await _provisioner.EnsureProfileAsync(
                allowedApplications,
                cancellationToken);
            _diagnostics.Info(
                "controller.profile",
                "Discord profili hazırlandı.",
                new Dictionary<string, string?>
                {
                    ["profilePath"] = profilePath,
                    ["allowedApplicationCount"] =
                        allowedApplications.Count.ToString(CultureInfo.InvariantCulture)
                });

            SetStatus(TunnelState.Connecting, "Discord ağına bağlanılıyor");
            var arguments = await PrepareWireSockArgumentsAsync(
                wireSockExecutable,
                profilePath,
                cancellationToken);
            _diagnostics.Info(
                "controller.process",
                "WireSock süreci başlatılıyor.",
                new Dictionary<string, string?>
                {
                    ["executable"] = wireSockExecutable,
                    ["arguments"] = string.Join(" ", arguments)
                });
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
                IncludeBrowserAccess
                    ? "Discord uygulaması ve web erişimi tünelleniyor"
                    : "Discord uygulaması tünelleniyor");
            _diagnostics.WriteHealth(
                "bağlı",
                new Dictionary<string, string?>
                {
                    ["browserAccess"] = IncludeBrowserAccess.ToString(),
                    ["profilePath"] = profilePath,
                    ["tunnelLog"] = _paths.TunnelLog
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisposeProcessAsync();
            await TryClearTunnelScopeAsync("ConnectCanceled");
            await TryEnableAccessLockAsync("ConnectCanceled");
            SetStatus(TunnelState.Disconnected, "Bağlantı iptal edildi");
            _diagnostics.Warning("controller.connect", "Bağlantı kullanıcı tarafından iptal edildi.");
            throw;
        }
        catch (Exception exception)
        {
            await DisposeProcessAsync();
            await TryClearTunnelScopeAsync("ConnectFailure");
            await TryEnableAccessLockAsync("ConnectFailure");
            WriteDiagnostic("Connect", exception.ToString());
            var userFacingMessage = CreateUserFacingConnectError(exception);
            _diagnostics.WriteHealth(
                "hata",
                new Dictionary<string, string?>
                {
                    ["operation"] = "connect",
                    ["message"] = userFacingMessage,
                    ["diagnostic"] = exception.Message
                });
            SetStatus(
                TunnelState.Error,
                userFacingMessage,
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
                _diagnostics.Info("controller.disconnect", "Aktif WireSock süreci yok; kilit etkinleştiriliyor.");
                await TryClearTunnelScopeAsync("DisconnectWithoutProcess");
                await _accessLock.EnableAsync(cancellationToken);
                SetStatus(TunnelState.Disconnected, "Bağlantı kapalı, Discord kilitli");
                return;
            }

            _intentionalStop = true;
            SetStatus(TunnelState.Disconnecting, "Bağlantı kapatılıyor");
            _diagnostics.Info("controller.disconnect", "Bağlantı kapatılıyor.");

            if (_wireSockProcess is not null)
            {
                await _wireSockProcess.StopAsync(
                    TimeSpan.FromSeconds(3),
                    cancellationToken);
            }

            await DisposeProcessAsync();
            await TryClearTunnelScopeAsync("Disconnect");
            await _accessLock.EnableAsync(cancellationToken);

            SetStatus(TunnelState.Disconnected, "Bağlantı kapalı, Discord kilitli");
            _diagnostics.WriteHealth("kapalı", new Dictionary<string, string?>
            {
                ["accessLock"] = "enabled"
            });
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
            _diagnostics.WriteHealth(
                "hata",
                new Dictionary<string, string?>
                {
                    ["operation"] = "disconnect",
                    ["message"] = exception.Message
                });
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

            await TryClearTunnelScopeAsync("Dispose");
            await TryEnableAccessLockAsync("Dispose");
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
                await TryClearTunnelScopeAsync("WireSockExited");
                await TryEnableAccessLockAsync("WireSockExited");
                WriteDiagnostic(
                    "WireSockExited",
                    $"WireSock exit code: " +
                    $"{exitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
                _diagnostics.WriteHealth(
                    "hata",
                    new Dictionary<string, string?>
                    {
                        ["operation"] = "WireSockExited",
                        ["exitCode"] = exitCode?.ToString(CultureInfo.InvariantCulture)
                            ?? "unknown"
                    });
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

    private async Task TryEnableAccessLockAsync(string operation)
    {
        try
        {
            await _accessLock.EnableAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            WriteDiagnostic(operation + "AccessLock", exception.ToString());
        }
    }

    private async Task TryClearTunnelScopeAsync(string operation)
    {
        try
        {
            await _accessLock.ClearTunnelScopeAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            WriteDiagnostic(operation + "TunnelScope", exception.ToString());
        }
    }

    private static string CreateUserFacingConnectError(Exception exception)
    {
        if (exception is TaskCanceledException
            && exception.InnerException is TimeoutException)
        {
            return "Gerekli dosya indirilemedi. Bağlantı zaman aşımına uğradı; internet erişimini kontrol edip tekrar bağlanın.";
        }

        if (exception is HttpRequestException httpException)
        {
            if (httpException.InnerException is SocketException socketException
                && socketException.SocketErrorCode is SocketError.HostNotFound
                    or SocketError.NoData)
            {
                return "Gerekli GitHub dosyası indirilemedi. DNS veya internet bağlantısını kontrol edip tekrar bağlanın.";
            }

            return "Gerekli dosya indirilemedi. İnternet bağlantısını kontrol edip tekrar bağlanın.";
        }

        if (exception is InvalidOperationException
            && exception.Message.Contains(
                "Discord VPN kilidi",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Discord kilidi güncellenemedi. Discorder'ı yönetici olarak açıp tekrar deneyin; hosts dosyasını koruyan güvenlik yazılımı varsa izin verin.";
        }

        return exception.Message;
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
        _diagnostics.Info(
            "controller.status",
            message,
            new Dictionary<string, string?>
            {
                ["state"] = state.ToString()
            });
        StatusChanged?.Invoke(this, _snapshot);
    }

    private void WriteDiagnostic(string operation, string diagnostic)
    {
        _diagnostics.Failure(
            "controller." + operation,
            "Denetleyici tanılaması yazıldı.",
            details: new Dictionary<string, string?>
            {
                ["diagnostic"] = diagnostic
            });

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
