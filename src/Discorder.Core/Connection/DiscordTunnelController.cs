using Discorder.Core.Configuration;
using Discorder.Core.Diagnostics;
using Discorder.Core.Discord;
using Discorder.Core.Firewall;
using Discorder.Core.Infrastructure;
using Discorder.Core.Provisioning;
using Discorder.Core.WireSock;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    private readonly IDiscordProcessManager _discordProcessManager;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly TimeSpan _startupGracePeriod;

    private IManagedProcess? _wireSockProcess;
    private TunnelSnapshot _snapshot = new(
        TunnelState.Disconnected,
        "Discorder Bağlı Değil",
        DateTimeOffset.Now);
    private bool _disposed;
    private bool _intentionalStop;
    private bool _accessLockConfirmed;
    private DiscordProcessSnapshot _lastDiscordProcessSnapshot = new(0, []);
    private string? _lastProfilePath;
    private string? _lastRoutingProfileSha256;
    private string? _lastNextAction;
    private string? _lastDiscordRestartStatus;
    private IReadOnlyDictionary<string, string?> _lastRoutingSummary =
        new Dictionary<string, string?>();

    public DiscordTunnelController(
        AppPaths paths,
        DiscordAppScope discordScope,
        IWireSockBootstrapper wireSockBootstrapper,
        IProfileProvisioner provisioner,
        IProcessLauncher processLauncher,
        TimeSpan? startupGracePeriod = null,
        IDiscordAccessLock? accessLock = null,
        IDiscorderDiagnostics? diagnostics = null,
        IDiscordProcessManager? discordProcessManager = null)
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
        _discordProcessManager = discordProcessManager
            ?? new NullDiscordProcessManager();
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

        if (_includeBrowserAccess != enabled)
        {
            _lastProfilePath = null;
            _lastRoutingProfileSha256 = null;
            _lastRoutingSummary = new Dictionary<string, string?>
            {
                ["profileState"] = "pending-next-connect",
                ["browserMode"] = enabled ? "pending-included" : "pending-excluded",
                ["browserProcessScope"] = enabled
                    ? "will-resolve-on-next-connect"
                    : "excluded"
            };
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

            _diagnostics.Info("controller.lock", "Discord bağlantı koruması etkinleştiriliyor.");
            await StopOwnedOrphanWireSockProcessAsync(cancellationToken);
            await _accessLock.EnableAsync(cancellationToken);
            _accessLockConfirmed = true;
            SetStatus(TunnelState.Disconnected, "Discorder Bağlı Değil");
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

    public IReadOnlyDictionary<string, string?> CreateDiagnosticDetails()
    {
        var liveDiscordProcesses = _discordProcessManager.Capture();
        var wireSockRunning = _wireSockProcess is not null
            && !_wireSockProcess.HasExited;

        return new Dictionary<string, string?>
        {
            ["browserAccess"] = IncludeBrowserAccess.ToString(),
            ["state"] = _snapshot.State.ToString(),
            ["message"] = _snapshot.Message,
            ["diagnostic"] = _snapshot.Diagnostic,
            ["wireSockRunning"] = wireSockRunning.ToString(),
            ["discordProcessCount"] =
                liveDiscordProcesses.RunningProcessCount.ToString(CultureInfo.InvariantCulture),
            ["discordExecutablePathCount"] =
                liveDiscordProcesses.KnownExecutablePathCount.ToString(CultureInfo.InvariantCulture),
            ["lastDiscordProcessCount"] =
                _lastDiscordProcessSnapshot.RunningProcessCount.ToString(CultureInfo.InvariantCulture),
            ["lastDiscordExecutablePathCount"] =
                _lastDiscordProcessSnapshot.KnownExecutablePathCount.ToString(CultureInfo.InvariantCulture),
            ["nextAction"] = _lastNextAction,
            ["discordRestartStatus"] = _lastDiscordRestartStatus,
            ["profilePath"] = _lastProfilePath,
            ["routingProfileSha256"] = _lastRoutingProfileSha256,
            ["profileHashScope"] = "sanitized-routing-lines",
            ["tunnelLog"] = _paths.TunnelLog,
            ["routingSummaryKind"] = "last-applied-or-pending-profile",
            ["routingScope"] = CreateRoutingScopeDescription(IncludeBrowserAccess),
            ["routingSummary"] = FormatRoutingSummary(_lastRoutingSummary)
        };
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

            var connectStopwatch = Stopwatch.StartNew();
            void LogConnectPhase(string phase)
            {
                _diagnostics.Info(
                    "controller.connect.phase",
                    phase,
                    new Dictionary<string, string?>
                    {
                        ["elapsedMs"] = connectStopwatch.ElapsedMilliseconds
                            .ToString(CultureInfo.InvariantCulture),
                        ["browserAccess"] = IncludeBrowserAccess.ToString(),
                        ["state"] = _snapshot.State.ToString()
                    });
            }

            _intentionalStop = false;
            SetStatus(TunnelState.Preparing, "Discord bağlantısı hazırlanıyor");
            _diagnostics.Info(
                "controller.connect",
                "Bağlantı başlatıldı.",
                new Dictionary<string, string?>
                {
                    ["browserAccess"] = IncludeBrowserAccess.ToString()
            });
            await _accessLock.DisableAsync(cancellationToken);
            _accessLockConfirmed = false;
            await _accessLock.ApplyTunnelScopeAsync(
                IncludeBrowserAccess,
                cancellationToken);
            LogConnectPhase("access-lock-ready");

            SetStatus(TunnelState.Preparing, "Discord bağlantısı açılıyor");

            var progress = new CallbackProgress(
                message =>
                {
                    _diagnostics.Info("controller.prepare", message);
                    SetStatus(TunnelState.Preparing, message);
                });
            var wireSockExecutable = await _wireSockBootstrapper.EnsureInstalledAsync(
                progress,
                cancellationToken);
            LogConnectPhase("wiresock-ready");

            var allowedApplications = _discordScope.GetAllowedApplications(
                IncludeBrowserAccess);
            _lastRoutingSummary = CreateAllowedApplicationDiagnostics(
                allowedApplications,
                IncludeBrowserAccess);
            var profilePath = await _provisioner.EnsureProfileAsync(
                allowedApplications,
                progress,
                cancellationToken);
            _lastProfilePath = profilePath;
            var routingProfileHash = await ComputeRoutingProfileSha256Async(
                profilePath,
                cancellationToken);
            _lastRoutingProfileSha256 = routingProfileHash;
            LogConnectPhase("profile-ready");
            _diagnostics.Info(
                "controller.profile",
                "Discord profili hazırlandı.",
                new Dictionary<string, string?>
                {
                    ["profilePath"] = profilePath,
                    ["routingProfileSha256"] = routingProfileHash,
                    ["profileHashScope"] = "sanitized-routing-lines",
                    ["routingScope"] = CreateRoutingScopeDescription(IncludeBrowserAccess)
                }.Concat(_lastRoutingSummary)
                    .ToDictionary(pair => pair.Key, pair => pair.Value));

            await StartWireSockProcessAsync(
                wireSockExecutable,
                profilePath,
                cancellationToken);
            LogConnectPhase("wiresock-process-started");

            DiscordProcessSnapshot discordProcesses = _discordProcessManager.Capture();
            _lastDiscordProcessSnapshot = discordProcesses;
            TunnelState finalState;
            string nextAction;
            string connectionMessage;
            string restartStatus;

            SetStatus(
                TunnelState.Verifying,
                discordProcesses.HasRunningProcesses
                    ? "Discord yenileniyor"
                    : "Discord açılıyor");
            var restart = await _discordProcessManager.RestartAsync(
                discordProcesses,
                TimeSpan.FromSeconds(4),
                cancellationToken);
            LogConnectPhase("discord-restart-attempted");
            restartStatus = restart.Message;
            _lastDiscordRestartStatus = restartStatus;
            discordProcesses = _discordProcessManager.Capture();
            _lastDiscordProcessSnapshot = discordProcesses;

            if (!restart.Restarted
                && restart.FailureKind is DiscordRestartFailureKind.UpdaterWindow)
            {
                restart = await TryRecoverDiscordUpdaterAsync(
                    wireSockExecutable,
                    profilePath,
                    cancellationToken);
                LogConnectPhase("discord-updater-recovery-attempted");
                restartStatus = restart.Message;
                _lastDiscordRestartStatus = restartStatus;
                discordProcesses = _discordProcessManager.Capture();
                _lastDiscordProcessSnapshot = discordProcesses;
            }

            if (restart.Restarted)
            {
                finalState = TunnelState.Connected;
                if (restart.Message.Contains("şimdi açın", StringComparison.OrdinalIgnoreCase)
                    || restart.Message.Contains("kapalıydı", StringComparison.OrdinalIgnoreCase))
                {
                    nextAction = "Discord'u şimdi açın.";
                    connectionMessage = "Bağlantı hazır. Discord'u şimdi açın";
                }
                else if (restart.Message.Contains("açıldı", StringComparison.OrdinalIgnoreCase))
                {
                    nextAction = "Discord açıldı. Bağlantı hazır.";
                    connectionMessage = "Discord açıldı. Bağlantı hazır";
                }
                else
                {
                    nextAction = "Discord yenilendi. Bağlantı hazır.";
                    connectionMessage = "Discord yenilendi. Bağlantı hazır";
                }
            }
            else
            {
                finalState = TunnelState.DiscordRestartRequired;
                nextAction = restart.Message;
                connectionMessage = restart.Message;
                _diagnostics.Warning(
                    "controller.discordRestart",
                    "Discord otomatik açılamadı.",
                    new Dictionary<string, string?>
                    {
                        ["message"] = restart.Message,
                        ["diagnostic"] = restart.Diagnostic
                    });
            }

            _lastNextAction = nextAction;
            connectStopwatch.Stop();

            SetStatus(
                finalState,
                connectionMessage,
                nextAction);
            _diagnostics.Info(
                "controller.verify",
                "WireSock süreci açık; Discord kullanımı için son durum belirlendi.",
                new Dictionary<string, string?>
                {
                    ["browserAccess"] = IncludeBrowserAccess.ToString(),
                    ["routingScope"] = CreateRoutingScopeDescription(IncludeBrowserAccess),
                    ["routingSummary"] = FormatRoutingSummary(_lastRoutingSummary),
                    ["discordRestartStatus"] = restartStatus,
                    ["discordProcessCount"] =
                        discordProcesses.RunningProcessCount.ToString(CultureInfo.InvariantCulture),
                    ["discordExecutablePathCount"] =
                        discordProcesses.KnownExecutablePathCount.ToString(CultureInfo.InvariantCulture),
                    ["nextAction"] = nextAction,
                    ["connectElapsedMs"] = connectStopwatch.ElapsedMilliseconds
                        .ToString(CultureInfo.InvariantCulture),
                    ["wireSockRunning"] = "True"
                });
            _diagnostics.WriteHealth(
                finalState is TunnelState.Connected
                    ? "bağlantı hazır"
                    : "discord yeniden başlatılmalı",
                new Dictionary<string, string?>
                {
                    ["browserAccess"] = IncludeBrowserAccess.ToString(),
                    ["routingScope"] = CreateRoutingScopeDescription(IncludeBrowserAccess),
                    ["routingSummary"] = FormatRoutingSummary(_lastRoutingSummary),
                    ["discordRestartStatus"] = restartStatus,
                    ["discordProcessCount"] =
                        discordProcesses.RunningProcessCount.ToString(CultureInfo.InvariantCulture),
                    ["discordExecutablePathCount"] =
                        discordProcesses.KnownExecutablePathCount.ToString(CultureInfo.InvariantCulture),
                    ["nextAction"] = nextAction,
                    ["profilePath"] = profilePath,
                    ["routingProfileSha256"] = routingProfileHash,
                    ["profileHashScope"] = "sanitized-routing-lines",
                    ["connectElapsedMs"] = connectStopwatch.ElapsedMilliseconds
                        .ToString(CultureInfo.InvariantCulture),
                    ["tunnelLog"] = _paths.TunnelLog,
                    ["wireSockRunning"] = "True"
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

    private async Task StartWireSockProcessAsync(
        string wireSockExecutable,
        string profilePath,
        CancellationToken cancellationToken)
    {
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
        WriteWireSockProcessMarker(profilePath);
        _wireSockProcess.Exited += OnWireSockExited;

        SetStatus(TunnelState.Verifying, "Bağlantı doğrulanıyor");
        await Task.Delay(_startupGracePeriod, cancellationToken);

        if (_wireSockProcess.HasExited)
        {
            var exitCode = _wireSockProcess.ExitCode;
            await DisposeProcessAsync();
            throw new InvalidOperationException(
                $"WireSock bağlantı kurulmadan kapandı. Çıkış kodu: " +
                $"{exitCode?.ToString(CultureInfo.InvariantCulture) ?? "bilinmiyor"}.");
        }
    }

    private async Task<DiscordRestartResult> TryRecoverDiscordUpdaterAsync(
        string wireSockExecutable,
        string profilePath,
        CancellationToken cancellationToken)
    {
        _diagnostics.Warning(
            "controller.discordUpdaterRecovery",
            "Discord güncelleme ekranında kaldı; bağlantı geçici olarak yenileniyor.");
        SetStatus(
            TunnelState.Verifying,
            "Discord güncellemesi tamamlanıyor",
            "Discord güncelleme ekranında kaldı; bağlantı kısa süre yenileniyor.");

        await DisposeProcessAsync();
        await TryClearTunnelScopeAsync("DiscordUpdaterRecovery");

        var directSnapshot = _discordProcessManager.Capture();
        _lastDiscordProcessSnapshot = directSnapshot;
        var directRestart = await _discordProcessManager.RestartAsync(
            directSnapshot,
            TimeSpan.FromSeconds(4),
            cancellationToken);
        _diagnostics.Info(
            "controller.discordUpdaterRecovery",
            "Discord güncelleme recovery denemesi tamamlandı.",
            new Dictionary<string, string?>
            {
                ["message"] = directRestart.Message,
                ["diagnostic"] = directRestart.Diagnostic,
                ["restarted"] = directRestart.Restarted.ToString()
            });

        await _accessLock.ApplyTunnelScopeAsync(
            IncludeBrowserAccess,
            cancellationToken);
        await StartWireSockProcessAsync(
            wireSockExecutable,
            profilePath,
            cancellationToken);

        if (!directRestart.Restarted)
        {
            return new DiscordRestartResult(
                false,
                "Discord güncelleme bağlantısı tamamlanamadı. Discord'u kapatıp tekrar deneyin.",
                directRestart.Diagnostic,
                directRestart.FailureKind);
        }

        _diagnostics.Info(
            "controller.discordUpdaterRecovery",
            "Discord güncellemesi tamamlandı; bağlantı yeniden açılıyor.",
            new Dictionary<string, string?>
            {
                ["directMessage"] = directRestart.Message,
                ["recoveryMode"] = "DirectRestartThenTunnelResume",
                ["tunnelRestart"] = "Skipped"
            });

        SetStatus(TunnelState.Verifying, "Discord bağlantısı doğrulanıyor");
        var resumedSnapshot = _discordProcessManager.Capture();
        _lastDiscordProcessSnapshot = resumedSnapshot;
        var verifyReady = await _discordProcessManager.VerifyReadyAsync(
            resumedSnapshot,
            cancellationToken);
        _diagnostics.Info(
            "controller.discordUpdaterRecovery",
            "Discord bağlantısı yeniden başlatmadan doğrulandı.",
            new Dictionary<string, string?>
            {
                ["message"] = verifyReady.Message,
                ["diagnostic"] = verifyReady.Diagnostic,
                ["ready"] = verifyReady.Restarted.ToString()
            });

        if (verifyReady.Restarted)
        {
            return verifyReady;
        }

        return new DiscordRestartResult(
            false,
            verifyReady.Message,
            verifyReady.Diagnostic,
            verifyReady.FailureKind);
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

    private static async Task<string> ComputeRoutingProfileSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        var safeLines = new List<string>();
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("AllowedApps", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("AllowedIPs", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Endpoint", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("DNS", StringComparison.OrdinalIgnoreCase))
            {
                safeLines.Add(trimmed);
            }
        }

        var projection = string.Join("\n", safeLines.Order(StringComparer.Ordinal));
        var bytes = Encoding.UTF8.GetBytes(projection);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private void WriteWireSockProcessMarker(string profilePath)
    {
        if (_wireSockProcess is null)
        {
            return;
        }

        try
        {
            _paths.EnsureDirectories();
            var marker = new WireSockProcessMarker(
                _wireSockProcess.ProcessId,
                _wireSockProcess.StartTime,
                profilePath);
            File.WriteAllText(
                _paths.WireSockProcessMarker,
                JsonSerializer.Serialize(marker));
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            _diagnostics.Warning(
                "controller.wiresock.marker",
                "WireSock süreç işareti yazılamadı.",
                new Dictionary<string, string?>
                {
                    ["diagnostic"] = exception.Message
                });
        }
    }

    private async Task StopOwnedOrphanWireSockProcessAsync(
        CancellationToken cancellationToken)
    {
        if (_wireSockProcess is not null
            || !File.Exists(_paths.WireSockProcessMarker))
        {
            return;
        }

        WireSockProcessMarker? marker;
        try
        {
            marker = JsonSerializer.Deserialize<WireSockProcessMarker>(
                await File.ReadAllTextAsync(
                    _paths.WireSockProcessMarker,
                    cancellationToken));
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            _diagnostics.Warning(
                "controller.wiresock.orphan",
                "Eski WireSock süreç işareti okunamadı.",
                new Dictionary<string, string?>
                {
                    ["diagnostic"] = exception.Message
                });
            return;
        }

        if (marker is null || marker.ProcessId <= 0)
        {
            DeleteWireSockProcessMarker();
            return;
        }

        var deleteMarker = false;
        try
        {
            using var process = Process.GetProcessById(marker.ProcessId);
            process.Refresh();
            if (!string.Equals(
                    process.ProcessName,
                    "wiresock-client",
                    StringComparison.OrdinalIgnoreCase)
                || !IsSameProcessStart(process, marker.StartTime))
            {
                deleteMarker = true;
                return;
            }

            if (!IsOwnedWireSockProfilePath(marker.ProfilePath))
            {
                _diagnostics.Warning(
                    "controller.wiresock.orphan",
                    "Eski WireSock süreç işareti beklenen Discorder profiline ait değil.",
                    new Dictionary<string, string?>
                    {
                        ["processId"] = marker.ProcessId.ToString(CultureInfo.InvariantCulture),
                        ["profilePath"] = marker.ProfilePath
                    });
                deleteMarker = true;
                return;
            }

            _diagnostics.Warning(
                "controller.wiresock.orphan",
                "Önceki oturumdan kalan Discorder WireSock süreci kapatılıyor.",
                new Dictionary<string, string?>
                {
                    ["processId"] = marker.ProcessId.ToString(CultureInfo.InvariantCulture),
                    ["profilePath"] = marker.ProfilePath
            });
            process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
            await process.WaitForExitAsync(linked.Token);
            deleteMarker = true;
        }
        catch (ArgumentException)
        {
            deleteMarker = true;
        }
        catch (InvalidOperationException)
        {
            deleteMarker = true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _diagnostics.Warning(
                "controller.wiresock.orphan",
                "Eski WireSock süreci kapatma zaman aşımına uğradı; sonraki açılışta tekrar denenecek.");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            _diagnostics.Warning(
                "controller.wiresock.orphan",
                "Eski WireSock süreci otomatik kapatılamadı; sonraki açılışta tekrar denenecek.",
                new Dictionary<string, string?>
                {
                    ["diagnostic"] = exception.Message
                });
        }
        finally
        {
            if (deleteMarker)
            {
                DeleteWireSockProcessMarker();
            }
        }
    }

    private bool IsOwnedWireSockProfilePath(string? profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(profilePath),
                Path.GetFullPath(_paths.DiscordProfile),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or IOException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsSameProcessStart(
        Process process,
        DateTimeOffset? expectedStartTime)
    {
        if (expectedStartTime is null)
        {
            return false;
        }

        try
        {
            var actual = new DateTimeOffset(process.StartTime);
            return Math.Abs((actual - expectedStartTime.Value).TotalSeconds) < 2;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or NotSupportedException
                or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private void DeleteWireSockProcessMarker()
    {
        try
        {
            if (File.Exists(_paths.WireSockProcessMarker))
            {
                File.Delete(_paths.WireSockProcessMarker);
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            _diagnostics.Warning(
                "controller.wiresock.marker",
                "WireSock süreç işareti silinemedi.",
                new Dictionary<string, string?>
                {
                    ["diagnostic"] = exception.Message
                });
        }
    }

    private static Dictionary<string, string?> CreateAllowedApplicationDiagnostics(
        IReadOnlyList<string> allowedApplications,
        bool includeBrowserAccess)
    {
        var pathCount = allowedApplications.Count(Path.IsPathRooted);
        var bareProcessCount = allowedApplications.Count(app => !Path.IsPathRooted(app));
        var browserPathCount = allowedApplications.Count(IsBrowserExecutable);
        var discordPathCount = allowedApplications.Count(IsDiscordExecutable);
        var broadBrowserNameCount = allowedApplications.Count(app =>
            !Path.IsPathRooted(app) && IsBrowserExecutable(app));

        return new Dictionary<string, string?>
        {
            ["allowedApplicationCount"] =
                allowedApplications.Count.ToString(CultureInfo.InvariantCulture),
            ["allowedApplicationPathCount"] =
                pathCount.ToString(CultureInfo.InvariantCulture),
            ["allowedApplicationBareNameCount"] =
                bareProcessCount.ToString(CultureInfo.InvariantCulture),
            ["allowedDiscordApplicationCount"] =
                discordPathCount.ToString(CultureInfo.InvariantCulture),
            ["allowedBrowserApplicationCount"] =
                browserPathCount.ToString(CultureInfo.InvariantCulture),
            ["allowedBareBrowserNameCount"] =
                broadBrowserNameCount.ToString(CultureInfo.InvariantCulture),
            ["browserMode"] = includeBrowserAccess ? "included" : "excluded",
            ["browserProcessScope"] = includeBrowserAccess
                ? "known-browser-executable-paths"
                : "excluded"
        };
    }

    private static string CreateRoutingScopeDescription(bool includeBrowserAccess)
    {
        return includeBrowserAccess
            ? "Discord uygulaması ve bulunan desteklenen tarayıcı executable path'leri kapsama alınır."
            : "Yalnızca Discord uygulaması kapsama alınır; desteklenen tarayıcılar profile eklenmez.";
    }

    private static string FormatRoutingSummary(
        IReadOnlyDictionary<string, string?> summary)
    {
        if (summary.Count == 0)
        {
            return "henüz profil hazırlanmadı";
        }

        return string.Join(
            "; ",
            summary
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private static bool IsBrowserExecutable(string value)
    {
        var name = Path.GetFileName(value);
        return name.Equals("brave.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("chromium.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("opera.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("vivaldi.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDiscordExecutable(string value)
    {
        var name = Path.GetFileName(value);
        return name.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("DiscordPTB.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("DiscordCanary.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("DiscordDevelopment.exe", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record WireSockProcessMarker(
        int ProcessId,
        DateTimeOffset? StartTime,
        string ProfilePath);

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken);

        try
        {
            if (_wireSockProcess is null)
            {
                _diagnostics.Info("controller.disconnect", "Aktif WireSock süreci yok; bağlantı koruması etkinleştiriliyor.");
                await TryClearTunnelScopeAsync("DisconnectWithoutProcess");
                await _accessLock.EnableAsync(cancellationToken);
                _accessLockConfirmed = true;
                await CloseDiscordAfterDisconnectAsync(cancellationToken);
                SetStatus(TunnelState.Disconnected, "Discorder Bağlı Değil");
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
            _accessLockConfirmed = true;
            await CloseDiscordAfterDisconnectAsync(cancellationToken);

            SetStatus(TunnelState.Disconnected, "Discorder Bağlı Değil");
            _lastNextAction = null;
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

    private async Task CloseDiscordAfterDisconnectAsync(CancellationToken cancellationToken)
    {
        var discordProcesses = _discordProcessManager.Capture();
        _lastDiscordProcessSnapshot = discordProcesses;
        if (!discordProcesses.HasRunningProcesses)
        {
            _lastDiscordRestartStatus = "Discord zaten kapalıydı.";
            return;
        }

        var close = await _discordProcessManager.CloseAsync(
            discordProcesses,
            TimeSpan.FromSeconds(3),
            cancellationToken);
        _lastDiscordRestartStatus = close.Message;
        if (!close.Restarted)
        {
            _diagnostics.Warning(
                "controller.discordClose",
                "Discord otomatik kapatılamadı.",
                new Dictionary<string, string?>
                {
                    ["message"] = close.Message,
                    ["diagnostic"] = close.Diagnostic
                });
        }
        else
        {
            _diagnostics.Info(
                "controller.discordClose",
                close.Message);
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
            var requiresTunnelCleanup = _wireSockProcess is not null
                || _snapshot.State is not TunnelState.Disconnected;
            var requiresAccessLockRefresh = requiresTunnelCleanup
                || !_accessLockConfirmed;
            _disposed = true;
            _intentionalStop = true;

            if (_wireSockProcess is not null)
            {
                await _wireSockProcess.StopAsync(
                    TimeSpan.FromSeconds(2),
                    CancellationToken.None);
                await DisposeProcessAsync();
            }

            if (requiresTunnelCleanup)
            {
                await TryClearTunnelScopeAsync("Dispose");
            }

            if (requiresAccessLockRefresh)
            {
                await TryEnableAccessLockAsync("Dispose");
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
        var process = _wireSockProcess;
        await process.DisposeAsync();
        _wireSockProcess = null;
        if (process.ExitConfirmed)
        {
            DeleteWireSockProcessMarker();
        }
        else
        {
            _diagnostics.Warning(
                "controller.wiresock.marker",
                "WireSock kapanışı doğrulanamadı; süreç işareti sonraki açılış için korunuyor.");
        }
    }

    private async Task TryEnableAccessLockAsync(string operation)
    {
        try
        {
            await _accessLock.EnableAsync(CancellationToken.None);
            _accessLockConfirmed = true;
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
        if (exception is TimeoutException)
        {
            return "Gerekli dosya indirilemedi. Bağlantı zaman aşımına uğradı; internet erişimini kontrol edip tekrar bağlanın.";
        }

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
            return "Discord bağlantı koruması güncellenemedi. Discorder'ı yönetici olarak açıp tekrar deneyin; hosts dosyasını koruyan güvenlik yazılımı varsa izin verin.";
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
        var redactedDiagnostic = DiscorderDiagnostics.RedactForLog(diagnostic)
            ?? string.Empty;
        _diagnostics.Failure(
            "controller." + operation,
            "Denetleyici tanılaması yazıldı.",
            details: new Dictionary<string, string?>
            {
                ["diagnostic"] = redactedDiagnostic
            });

        try
        {
            _paths.EnsureDirectories();
            File.AppendAllText(
                _paths.ErrorLog,
                $"[{DateTimeOffset.Now:O}] {operation}{Environment.NewLine}" +
                $"{redactedDiagnostic}{Environment.NewLine}{Environment.NewLine}");
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
