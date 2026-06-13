using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Discorder.App.Installation;
using Discorder.Core.Configuration;
using Discorder.Core.Connection;
using Discorder.Core.Diagnostics;
using Discorder.Core.Maintenance;
using Discorder.Core.Updates;
using Discorder.Core.WireSock;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;

namespace Discorder.App;

public partial class MainWindow : Window, IDisposable
{
    private static readonly TimeSpan LiveDnsRefreshInterval = TimeSpan.FromSeconds(15);

    private static readonly Uri RepositoryUri = new(
        "https://github.com/ucsahinn/discorder");
    private static readonly Uri BackgroundVideoUri = new(
        "https://d8j0ntlcm91z4.cloudfront.net/user_38xzZboKViGWJOttwIXH07lWA1P/hf_20260606_154941_df1a96e1-a06f-450c-bd02-d863414cc1a0.mp4");
    private static readonly string LocalBackgroundVideoPath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "background.mp4");

    private readonly DiscordTunnelController _controller;
    private readonly AppPaths _paths;
    private readonly IWireSockBootstrapper _wireSockBootstrapper;
    private readonly AppSettingsStore _settingsStore;
    private readonly DiscorderCleanupService _cleanupService;
    private readonly IStartupLaunchService _startupLaunchService;
    private readonly IWireSockUninstaller _wireSockUninstaller;
    private readonly AppUpdateService _updateService;
    private readonly IDiscorderDiagnostics _diagnostics;
    private bool _isApplyingSettings;
    private bool _isRunInBackgroundEnabled;
    private bool _isToggleOperationRunning;
    private bool _isUpdateOperationRunning;
    private bool _isUpdateProgressPinned;
    private string? _lastLoggedUpdateProgressKey;
    private int _lastLoggedUpdateProgressPercent = -1;
    private AppUpdateCheckResult? _pendingUpdate;
    private bool _backgroundVideoRemoteFallbackTried;
    private bool _isBackgroundVideoStarted;
    private DateTimeOffset _lastDnsRefreshUtc = DateTimeOffset.MinValue;
    private (string Summary, string Detail) _cachedDnsStatus = ("Okunuyor", "Makinenin aldığı DNS");
    private Forms.NotifyIcon? _trayIcon;
    private bool _hasShownTrayNotice;
    private CancellationTokenSource? _operationCancellation;
    private bool _allowClose;
    private bool _isClosing;
    private bool _disposed;

    public MainWindow(
        DiscordTunnelController controller,
        AppPaths paths,
        IWireSockBootstrapper wireSockBootstrapper,
        AppSettingsStore settingsStore,
        DiscorderCleanupService cleanupService,
        IStartupLaunchService startupLaunchService,
        IWireSockUninstaller wireSockUninstaller,
        AppUpdateService updateService,
        IDiscorderDiagnostics? diagnostics = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _wireSockBootstrapper = wireSockBootstrapper
            ?? throw new ArgumentNullException(nameof(wireSockBootstrapper));
        _settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        _cleanupService = cleanupService
            ?? throw new ArgumentNullException(nameof(cleanupService));
        _startupLaunchService = startupLaunchService
            ?? throw new ArgumentNullException(nameof(startupLaunchService));
        _wireSockUninstaller = wireSockUninstaller
            ?? throw new ArgumentNullException(nameof(wireSockUninstaller));
        _updateService = updateService
            ?? throw new ArgumentNullException(nameof(updateService));
        _diagnostics = diagnostics ?? NullDiscorderDiagnostics.Instance;

        InitializeComponent();
        ApplyBrowserAccessSetting(_settingsStore.IsBrowserAccessEnabled());
        ApplyRunInBackgroundSetting(
            _settingsStore.IsRunInBackgroundOnCloseEnabled());
        ApplyStartupSetting(SynchronizeStartupLaunchSetting(
            _settingsStore.IsStartWithWindowsEnabled()));
        _controller.StatusChanged += OnStatusChanged;
        ApplySnapshot(_controller.Snapshot);
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isToggleOperationRunning || _controller.Snapshot.IsBusy)
        {
            return;
        }

        _isToggleOperationRunning = true;
        ToggleButton.IsEnabled = false;

        try
        {
            if (!_controller.Snapshot.IsConnected
                && !_wireSockBootstrapper.IsSetupConsentAccepted)
            {
                var consentWindow = new WireSockConsentWindow(_wireSockBootstrapper)
                {
                    Owner = this
                };

                if (consentWindow.ShowDialog() != true)
                {
                    return;
                }

                _wireSockBootstrapper.AcceptSetupConsent();
            }

            _operationCancellation?.Dispose();
            _operationCancellation = new CancellationTokenSource();

            if (_controller.Snapshot.IsConnected)
            {
                await _controller.DisconnectAsync(CancellationToken.None);
            }
            else
            {
                await _controller.ConnectAsync(_operationCancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isToggleOperationRunning = false;
            ApplySnapshot(_controller.Snapshot);
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _controller.EnsureDisconnectedLockAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            StopBackgroundVideo();
            return;
        }

        UpdateBackgroundVideoForSnapshot(_controller.Snapshot);
    }

    private void OnStatusChanged(object? sender, TunnelSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ApplySnapshot(snapshot));
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(TunnelSnapshot snapshot)
    {
        if (!_isUpdateOperationRunning && snapshot.IsBusy)
        {
            _isUpdateProgressPinned = false;
        }

        ToggleButton.IsEnabled = !_isToggleOperationRunning && !snapshot.IsBusy;
        RepairButton.IsEnabled = !snapshot.IsBusy;
        ApplyUpdateControls(snapshot.IsBusy);
        StatusMessage.Text = snapshot.Message;
        StatusDetail.Text = GetDetail(snapshot.State);
        ApplyBrowserAccessView(
            _controller.IncludeBrowserAccess,
            snapshot.IsBusy || snapshot.IsConnected);
        RefreshLiveStatusCards(snapshot);
        UpdateBackgroundVideoForSnapshot(snapshot);

        var templateLabel = ToggleButton.Template.FindName(
            "ToggleButtonLabel",
            ToggleButton) as System.Windows.Controls.TextBlock;
        var powerIcon = ToggleButton.Template.FindName(
            "PowerIcon",
            ToggleButton) as System.Windows.Controls.TextBlock;
        var stateRing = ToggleButton.Template.FindName(
            "StateRing",
            ToggleButton) as System.Windows.Shapes.Ellipse;
        var coreGlow = ToggleButton.Template.FindName(
            "CoreGlow",
            ToggleButton) as System.Windows.Shapes.Ellipse;
        var outerRing = ToggleButton.Template.FindName(
            "OuterRing",
            ToggleButton) as System.Windows.Shapes.Ellipse;
        var visual = GetStateVisual(snapshot.State);

        if (templateLabel is not null)
        {
            templateLabel.Text = visual.ButtonText;
        }

        StatusLabel.Text = visual.BadgeText;
        StatusDot.Fill = visual.StatusBrush;
        StatusBadge.BorderBrush = visual.StatusBrush;
        StatusBadge.Background = visual.BadgeBackground;
        ToggleButton.BorderBrush = visual.StatusBrush;

        if (outerRing is not null)
        {
            outerRing.Effect = CreateGlowEffect(34, 0.9, visual.GlowColor);
        }

        if (powerIcon is not null)
        {
            powerIcon.Foreground = visual.PowerBrush;
            powerIcon.Effect = CreateGlowEffect(18, 0.86, visual.GlowColor);
        }

        if (stateRing is not null)
        {
            stateRing.Stroke = visual.StatusBrush;
        }

        if (coreGlow is not null)
        {
            coreGlow.Fill = visual.CoreFill;
            coreGlow.Stroke = visual.PowerBrush;
        }

        if (!_isUpdateProgressPinned)
        {
            ApplyProgress(snapshot, visual.StatusBrush);
        }
    }

    private void RefreshLiveStatusCards(TunnelSnapshot snapshot)
    {
        var dns = GetCachedDnsStatus();
        DnsSummary.Text = dns.Summary;
        DnsDetail.Text = dns.Detail;

        ConnectionSummary.Text = GetConnectionSummary(snapshot.State);
        ConnectionDetail.Text = GetConnectionDetail(snapshot);

        ScopeSummary.Text = _controller.IncludeBrowserAccess
            ? "Uygulama + tarayıcı"
            : "Sadece uygulama";
        ScopeDetail.Text = _controller.IncludeBrowserAccess
            ? "Discord web bağlantıya dahil"
            : "Tarayıcı modu kapalı";
    }

    private (string Summary, string Detail) GetCachedDnsStatus()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastDnsRefreshUtc < LiveDnsRefreshInterval)
        {
            return _cachedDnsStatus;
        }

        _cachedDnsStatus = GetDnsStatus();
        _lastDnsRefreshUtc = now;
        return _cachedDnsStatus;
    }

    private static (string Summary, string Detail) GetDnsStatus()
    {
        try
        {
            var addresses = NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(IsActiveAdapter)
                .SelectMany(GetDnsAddresses)
                .Where(IsDisplayableDnsAddress)
                .Select(address => address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToArray();

            return addresses.Length switch
            {
                0 => ("Algılanmadı", "Windows DNS adresi yok"),
                1 => (addresses[0], "Makinenin aldığı DNS"),
                _ => (
                    string.Join(", ", addresses.Take(2)),
                    $"{addresses.Length} DNS adresi aktif")
            };
        }
        catch (NetworkInformationException)
        {
            return ("Okunamadı", "DNS bilgisi alınamadı");
        }
        catch (UnauthorizedAccessException)
        {
            return ("Okunamadı", "DNS bilgisi alınamadı");
        }
    }

    private static bool IsActiveAdapter(NetworkInterface adapter)
    {
        return adapter.OperationalStatus == OperationalStatus.Up
            && adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback
            && adapter.NetworkInterfaceType is not NetworkInterfaceType.Tunnel;
    }

    private static IEnumerable<IPAddress> GetDnsAddresses(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().DnsAddresses;
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    private static bool IsDisplayableDnsAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !address.IsIPv6LinkLocal
                && !address.IsIPv6Multicast
                && !address.IsIPv6SiteLocal;
        }

        return address.AddressFamily == AddressFamily.InterNetwork;
    }

    private static string GetConnectionSummary(TunnelState state)
    {
        return state switch
        {
            TunnelState.Connected => "Bağlı",
            TunnelState.Preparing => "Hazırlanıyor",
            TunnelState.Connecting => "Bağlanıyor",
            TunnelState.Disconnecting => "Kapanıyor",
            TunnelState.Error => "Kontrol gerekiyor",
            _ => "Bağlı değil"
        };
    }

    private static string GetConnectionDetail(TunnelSnapshot snapshot)
    {
        var time = snapshot.ChangedAt
            .ToLocalTime()
            .ToString("HH:mm", CultureInfo.CurrentCulture);

        return snapshot.State switch
        {
            TunnelState.Connected => $"Tünel aktif, son durum {time}",
            TunnelState.Preparing or TunnelState.Connecting
                => $"Kurulum ve profil kontrolü, {time}",
            TunnelState.Disconnecting => $"Bağlantı kapatılıyor, {time}",
            TunnelState.Error => $"Tanılama gerekebilir, {time}",
            _ => $"Son durum {time}"
        };
    }

    private static DropShadowEffect CreateGlowEffect(
        double blurRadius,
        double opacity,
        MediaColor color)
    {
        return new DropShadowEffect
        {
            BlurRadius = blurRadius,
            Direction = 270,
            Opacity = opacity,
            ShadowDepth = 0,
            Color = color
        };
    }

    private static StateVisual GetStateVisual(TunnelState state)
    {
        return state switch
        {
            TunnelState.Connected => new StateVisual(
                "BAĞLI",
                "BAĞLANTIYI KES",
                MediaColor.FromRgb(86, 240, 123),
                MediaColor.FromRgb(128, 255, 167),
                MediaColor.FromRgb(26, 80, 48),
                MediaColor.FromRgb(86, 240, 123)),
            TunnelState.Preparing or TunnelState.Connecting => new StateVisual(
                "BAĞLANIYOR",
                "BAĞLANIYOR",
                MediaColor.FromRgb(249, 214, 107),
                MediaColor.FromRgb(115, 232, 255),
                MediaColor.FromRgb(82, 62, 22),
                MediaColor.FromRgb(249, 214, 107)),
            TunnelState.Disconnecting => new StateVisual(
                "KAPANIYOR",
                "KAPANIYOR",
                MediaColor.FromRgb(255, 163, 92),
                MediaColor.FromRgb(115, 232, 255),
                MediaColor.FromRgb(86, 43, 22),
                MediaColor.FromRgb(255, 163, 92)),
            TunnelState.Error => new StateVisual(
                "HATA",
                "TEKRAR DENE",
                MediaColor.FromRgb(255, 107, 122),
                MediaColor.FromRgb(255, 176, 187),
                MediaColor.FromRgb(86, 24, 34),
                MediaColor.FromRgb(255, 68, 92)),
            _ => new StateVisual(
                "KAPALI",
                "BAĞLAN",
                MediaColor.FromRgb(255, 78, 106),
                MediaColor.FromRgb(255, 118, 135),
                MediaColor.FromRgb(90, 28, 42),
                MediaColor.FromRgb(255, 78, 106))
        };
    }

    private sealed class StateVisual
    {
        public StateVisual(
            string badgeText,
            string buttonText,
            MediaColor statusColor,
            MediaColor powerColor,
            MediaColor coreColor,
            MediaColor glowColor)
        {
            BadgeText = badgeText;
            ButtonText = buttonText;
            StatusBrush = new SolidColorBrush(statusColor);
            PowerBrush = new SolidColorBrush(powerColor);
            GlowColor = glowColor;
            CoreFill = CreateCoreFill(coreColor, powerColor);
            BadgeBackground = new SolidColorBrush(MediaColor.FromArgb(
                126,
                coreColor.R,
                coreColor.G,
                coreColor.B));
        }

        public string BadgeText { get; }

        public string ButtonText { get; }

        public SolidColorBrush StatusBrush { get; }

        public SolidColorBrush PowerBrush { get; }

        public MediaColor GlowColor { get; }

        public MediaBrush CoreFill { get; }

        public SolidColorBrush BadgeBackground { get; }

        private static RadialGradientBrush CreateCoreFill(
            MediaColor coreColor,
            MediaColor powerColor)
        {
            return new RadialGradientBrush
            {
                Center = new System.Windows.Point(0.46, 0.38),
                GradientOrigin = new System.Windows.Point(0.34, 0.25),
                RadiusX = 0.82,
                RadiusY = 0.82,
                GradientStops =
                [
                    new GradientStop(MediaColor.FromArgb(
                        62,
                        powerColor.R,
                        powerColor.G,
                        powerColor.B), 0),
                    new GradientStop(MediaColor.FromArgb(
                        39,
                        coreColor.R,
                        coreColor.G,
                        coreColor.B), 0.48),
                    new GradientStop(MediaColor.FromArgb(
                        20,
                        4,
                        7,
                        16), 1)
                ]
            };
        }
    }

    private void ApplyProgress(
        TunnelSnapshot snapshot,
        System.Windows.Media.Brush stateBrush)
    {
        var progress = GetProgress(snapshot);

        StageProgressBar.Value = progress.Value;
        StageProgressBar.Foreground = stateBrush;
        StageProgressLabel.Text = progress.Label;
        StageProgressPercent.Text = $"{progress.Value:0}%";

        ApplyStageStep(StageStepLock, progress.ActiveStep >= 1, stateBrush);
        ApplyStageStep(StageStepSetup, progress.ActiveStep >= 2, stateBrush);
        ApplyStageStep(StageStepProfile, progress.ActiveStep >= 3, stateBrush);
        ApplyStageStep(StageStepTunnel, progress.ActiveStep >= 4, stateBrush);
    }

    private static (double Value, string Label, int ActiveStep) GetProgress(
        TunnelSnapshot snapshot)
    {
        var message = snapshot.Message;
        return snapshot.State switch
        {
            TunnelState.Connected => (100, "Bağlandı", 4),
            TunnelState.Connecting => (82, "Bağlantı açılıyor", 4),
            TunnelState.Disconnecting => (62, "Bağlantı kapatılıyor", 4),
            TunnelState.Error => (100, "Müdahale gerekiyor", 4),
            TunnelState.Preparing when message.Contains(
                    "Cloudflare",
                    StringComparison.OrdinalIgnoreCase)
                || message.Contains("WARP", StringComparison.OrdinalIgnoreCase)
                || message.Contains("wgcf", StringComparison.OrdinalIgnoreCase)
                || message.Contains("profil", StringComparison.OrdinalIgnoreCase)
                => (68, "Profil hazırlanıyor", 3),
            TunnelState.Preparing when message.Contains(
                    "Discord bağlantısı",
                    StringComparison.OrdinalIgnoreCase)
                => (22, "Discord bağlantısı hazırlanıyor", 1),
            TunnelState.Preparing when message.Contains(
                    "WireSock",
                    StringComparison.OrdinalIgnoreCase)
                || message.Contains("kurul", StringComparison.OrdinalIgnoreCase)
                || message.Contains("kurucu", StringComparison.OrdinalIgnoreCase)
                || message.Contains("doğrulan", StringComparison.OrdinalIgnoreCase)
                => (48, "WireSock doğrulanıyor", 2),
            TunnelState.Preparing => (38, "Hazırlık çalışıyor", 2),
            _ => (8, "Discorder Bağlı Değil", 0)
        };
    }

    private static void ApplyStageStep(
        System.Windows.Controls.TextBlock textBlock,
        bool isActive,
        System.Windows.Media.Brush activeBrush)
    {
        var inactiveBrush = (System.Windows.Media.Brush)
            System.Windows.Application.Current.Resources["SecondaryTextBrush"];
        textBlock.Foreground = isActive
            ? activeBrush
            : inactiveBrush;
        textBlock.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
        textBlock.Opacity = isActive ? 1 : 0.58;
    }

    private string GetDetail(TunnelState state)
    {
        return state switch
        {
            TunnelState.Connected => _controller.IncludeBrowserAccess
                ? "Tarayıcı modunda Discord uygulaması ve desteklenen tarayıcılar kapsamda."
                : "Çalışma modunda yalnızca Discord uygulaması kapsamda.",
            TunnelState.Preparing => "Kurulum, dijital imza ve bağlantı profili doğrulanıyor.",
            TunnelState.Connecting => "WireSock VPN Client süreci başlatılıyor.",
            TunnelState.Disconnecting => "Bağlantı güvenli biçimde sonlandırılıyor.",
            TunnelState.Error => "Tanılama klasörünü açarak ayrıntıları inceleyin.",
            _ => "Discorder bağlı değilken Discord düz bağlantıya çıkmaz."
        };
    }

    private void BrowserAccessToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        var enabled = BrowserAccessToggle.IsChecked == true;
        if (!_controller.TrySetBrowserAccess(enabled))
        {
            ApplyBrowserAccessView(
                _controller.IncludeBrowserAccess,
                locked: true);
            return;
        }

        _settingsStore.SetBrowserAccessEnabled(enabled);
        _diagnostics.Info(
            "ui.browserAccess",
            enabled
                ? "Discord web kapsamı açıldı."
                : "Discord web kapsamı kapatıldı.");
        ApplyBrowserAccessView(enabled, locked: false);
    }

    private void ApplyBrowserAccessSetting(bool enabled)
    {
        if (!_controller.TrySetBrowserAccess(enabled))
        {
            enabled = _controller.IncludeBrowserAccess;
        }

        ApplyBrowserAccessView(enabled, _controller.Snapshot.IsBusy
            || _controller.Snapshot.IsConnected);
    }

    private void ApplyBrowserAccessView(bool enabled, bool locked)
    {
        _isApplyingSettings = true;
        try
        {
            BrowserAccessToggle.IsChecked = enabled;
            BrowserAccessToggle.IsEnabled = !locked;
            BrowserAccessStatus.Text = enabled
                ? locked
                    ? "Tarayıcı modu bu oturumda açık. Değiştirmek için önce bağlantıyı kes."
                    : "Tarayıcı modu açık. Discord web erişimi de bağlantıya dahil."
                : locked
                    ? "Bu oturumda değişmez. Değiştirmek için önce bağlantıyı kes."
                    : "Kapalıyken yalnızca Discord uygulaması bağlantıya dahil.";
        }
        finally
        {
            _isApplyingSettings = false;
        }

        RefreshLiveStatusCards(_controller.Snapshot);
    }

    private void RunInBackgroundToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        var enabled = RunInBackgroundToggle.IsChecked == true;
        _settingsStore.SetRunInBackgroundOnCloseEnabled(enabled);
        _diagnostics.Info(
            "ui.runInBackground",
            enabled
                ? "Pencere kapanınca arka planda kalma açıldı."
                : "Pencere kapanınca arka planda kalma kapatıldı.");
        ApplyRunInBackgroundSetting(enabled);
    }

    private void ApplyRunInBackgroundSetting(bool enabled)
    {
        _isApplyingSettings = true;
        try
        {
            _isRunInBackgroundEnabled = enabled;
            RunInBackgroundToggle.IsChecked = enabled;
            RunInBackgroundStatus.Text = enabled
                ? "Pencere kapanınca bildirim alanında kalır."
                : "Pencere kapanınca bildirim alanında kalmaz.";

            if (enabled)
            {
                EnsureTrayIcon();
            }
            else if (IsVisible)
            {
                DisposeTrayIcon();
            }
        }
        finally
        {
            _isApplyingSettings = false;
        }

        RefreshLiveStatusCards(_controller.Snapshot);
    }

    private void StartupToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        var enabled = StartupToggle.IsChecked == true;
        try
        {
            _startupLaunchService.SetEnabled(enabled);
            _settingsStore.SetStartWithWindowsEnabled(enabled);
            _diagnostics.Info(
                "ui.startup",
                enabled
                    ? "Windows açılışında çalıştırma açıldı."
                    : "Windows açılışında çalıştırma kapatıldı.");
            ApplyStartupSetting(enabled);
        }
        catch (Exception exception)
        {
            var currentState = TryGetStartupLaunchState();
            _settingsStore.SetStartWithWindowsEnabled(currentState);
            ApplyStartupSetting(currentState);

            MessageBox.Show(
                "Windows açılışında çalıştırma ayarı kaydedilemedi.\n\n" + exception.Message,
                "Discorder",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _diagnostics.Failure(
                "ui.startup",
                "Windows başlangıç ayarı kaydedilemedi.",
                exception);
        }
    }

    private void ApplyStartupSetting(bool enabled)
    {
        _isApplyingSettings = true;
        try
        {
            StartupToggle.IsChecked = enabled;
            StartupStatus.Text = enabled
                ? "Windows açılışında çalışır."
                : "Windows açılışında çalıştır";
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private bool SynchronizeStartupLaunchSetting(bool desiredState)
    {
        try
        {
            _startupLaunchService.SetEnabled(desiredState);
            return desiredState;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            var currentState = TryGetStartupLaunchState();
            _settingsStore.SetStartWithWindowsEnabled(currentState);
            return currentState;
        }
    }

    private bool TryGetStartupLaunchState()
    {
        try
        {
            return _startupLaunchService.IsEnabled();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return false;
        }
    }

    private async void CleanUninstall_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            "Discorder bağlantıyı kapatacak, yerel ayarları ve uygulama verilerini kaldıracak. " +
            "WireSock Discorder tarafından kurulduysa Windows'tan kaldırılacak.\n\nDevam edilsin mi?",
            "Discorder uygulamayı kaldır",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        IsEnabled = false;
        _operationCancellation?.Cancel();
        StopBackgroundVideo();
        StatusMessage.Text = "Kaldırma çalışıyor";
        StatusDetail.Text = "Bağlantı kapatılıyor, yerel Discorder verileri kaldırılıyor.";
        SetMaintenanceProgress(24, "Bağlantı kapatılıyor");
        _diagnostics.Warning("ui.cleanUninstall", "Kullanıcı temiz kaldırmayı başlattı.");

        try
        {
            _startupLaunchService.SetEnabled(false);
            var removeWireSock = _settingsStore.IsWireSockInstalledByDiscorder()
                || File.Exists(_paths.WireSockInstallMarker);
            _settingsStore.SetStartWithWindowsEnabled(false);
            _settingsStore.SetRunInBackgroundOnCloseEnabled(false);
            await _controller.DisposeAsync();
            SetMaintenanceProgress(48, removeWireSock
                ? "WireSock kaldırılıyor"
                : "WireSock korunuyor");
            await _wireSockUninstaller.UninstallIfDiscorderInstalledAsync(
                removeWireSock,
                CancellationToken.None);
            _settingsStore.SetWireSockInstalledByDiscorder(installed: false);
            SetMaintenanceProgress(78, "Yerel veriler kaldırılıyor");
            await _cleanupService.CleanUninstallAsync(CancellationToken.None);

            MessageBox.Show(
                "Discorder kaldırıldı. Uygulama kapanacak.",
                "Discorder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _allowClose = true;
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            IsEnabled = true;
            StatusMessage.Text = "Kaldırma tamamlanamadı";
            StatusDetail.Text = exception.Message;

            MessageBox.Show(
                "Uygulama kaldırılamadı.\n\n" + exception.Message,
                "Discorder",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.Snapshot.IsBusy)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            "Discorder bağlantıyı kapatacak; profil, wgcf aracı ve kurucu önbelleğini yeniden üretilecek hale getirecek. " +
            "Ayarlar, WireSock kurulumu ve tanılama kayıtları korunur.\n\nDevam edilsin mi?",
            "Discorder onar",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        ToggleButton.IsEnabled = false;
        RepairButton.IsEnabled = false;
        SetUpdateControlsEnabled(false);
        BrowserAccessToggle.IsEnabled = false;
        _operationCancellation?.Cancel();
        StatusMessage.Text = "Onarım çalışıyor";
        StatusDetail.Text = "Bağlantı kapatılıyor, koruma geri alınıyor ve profil dosyaları yenileniyor.";
        SetMaintenanceProgress(36, "Onarım hazırlanıyor");
        _diagnostics.Info("ui.repair", "Kullanıcı onarımı başlattı.");

        try
        {
            await _controller.DisconnectAsync(CancellationToken.None);
            SetMaintenanceProgress(68, "Profil ve önbellek temizleniyor");
            await _cleanupService.RepairAsync(CancellationToken.None);

            StatusMessage.Text = "Onarım tamamlandı";
            StatusDetail.Text = "Sonraki Bağlan işleminde profil ve wgcf dosyaları yeniden üretilecek.";
            SetMaintenanceProgress(100, "Onarım tamamlandı");

            MessageBox.Show(
                "Discorder onarıldı. Sonraki bağlantıda profil yeniden oluşturulacak.",
                "Discorder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage.Text = "Onarım tamamlanamadı";
            StatusDetail.Text = exception.Message;
            SetMaintenanceProgress(100, "Müdahale gerekiyor");

            MessageBox.Show(
                "Onarım tamamlanamadı.\n\n" + exception.Message,
                "Discorder",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ToggleButton.IsEnabled = !_controller.Snapshot.IsBusy;
            RepairButton.IsEnabled = !_controller.Snapshot.IsBusy;
            ApplyUpdateControls(_controller.Snapshot.IsBusy);
            BrowserAccessToggle.IsEnabled = !_controller.Snapshot.IsBusy
                && !_controller.Snapshot.IsConnected;
        }
    }

    private void SetMaintenanceProgress(double value, string label)
    {
        StageProgressBar.Value = value;
        StageProgressBar.Foreground = (System.Windows.Media.Brush)
            System.Windows.Application.Current.Resources["AccentCyanBrush"];
        StageProgressLabel.Text = label;
        StageProgressPercent.Text = $"{value:0}%";
    }

    private void SetUpdateStageProgress(double value, string label)
    {
        _isUpdateProgressPinned = true;
        SetMaintenanceProgress(value, label);
    }

    private void BackgroundVideo_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateBackgroundVideoForSnapshot(_controller.Snapshot);
    }

    private void BackgroundVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (!ShouldPlayBackgroundVideo(_controller.Snapshot))
        {
            StopBackgroundVideo();
            return;
        }

        BackgroundVideo.Position = TimeSpan.Zero;
        BackgroundVideo.Play();
        _isBackgroundVideoStarted = true;
    }

    private void BackgroundVideo_MediaFailed(
        object sender,
        ExceptionRoutedEventArgs e)
    {
        if (!ShouldPlayBackgroundVideo(_controller.Snapshot))
        {
            StopBackgroundVideo();
            return;
        }

        if (IsBackgroundVideoRemoteFallbackEnabled()
            && !_backgroundVideoRemoteFallbackTried
            && BackgroundVideo.Source is not null
            && BackgroundVideo.Source.IsFile)
        {
            _backgroundVideoRemoteFallbackTried = true;
            _isBackgroundVideoStarted = false;
            _diagnostics.Warning(
                "ui.backgroundVideo",
                "Yerel arka plan videosu oynatılamadı, uzak video deneniyor.",
                new Dictionary<string, string?>
                {
                    ["path"] = LocalBackgroundVideoPath,
                    ["error"] = e.ErrorException?.Message
                });
            BackgroundVideo.Source = BackgroundVideoUri;
            BackgroundVideo.Play();
            _isBackgroundVideoStarted = true;
            return;
        }

        _isBackgroundVideoStarted = false;
        BackgroundVideo.Visibility = Visibility.Collapsed;
        _diagnostics.Warning(
            "ui.backgroundVideo",
            "Arka plan videosu yüklenemedi.",
            new Dictionary<string, string?>
            {
                ["error"] = e.ErrorException?.Message
            });
    }

    private static bool IsBackgroundVideoDisabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("DISCORDER_DISABLE_BACKGROUND_VIDEO"),
            "1",
            StringComparison.Ordinal);
    }

    private static bool IsBackgroundVideoRemoteFallbackEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("DISCORDER_BACKGROUND_VIDEO_REMOTE_FALLBACK"),
            "1",
            StringComparison.Ordinal);
    }

    private bool ShouldPlayBackgroundVideo(TunnelSnapshot snapshot)
    {
        if (IsBackgroundVideoDisabled())
        {
            return false;
        }

        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            return false;
        }

        return true;
    }

    private void UpdateBackgroundVideoForSnapshot(TunnelSnapshot snapshot)
    {
        if (ShouldPlayBackgroundVideo(snapshot))
        {
            StartBackgroundVideo();
            return;
        }

        StopBackgroundVideo();
    }

    private void StopBackgroundVideo()
    {
        if (!_isBackgroundVideoStarted
            && BackgroundVideo.Source is null
            && BackgroundVideo.Visibility == Visibility.Collapsed)
        {
            return;
        }

        try
        {
            BackgroundVideo.Stop();
            BackgroundVideo.Source = null;
            BackgroundVideo.Visibility = Visibility.Collapsed;
            _isBackgroundVideoStarted = false;
        }
        catch (InvalidOperationException)
        {
            _isBackgroundVideoStarted = false;
        }
    }

    private void StartBackgroundVideo()
    {
        if (IsBackgroundVideoDisabled())
        {
            StopBackgroundVideo();
            return;
        }

        if (_isBackgroundVideoStarted && BackgroundVideo.Source is not null)
        {
            return;
        }

        var videoUri = GetBackgroundVideoUri();
        if (videoUri is null)
        {
            StopBackgroundVideo();
            return;
        }

        _backgroundVideoRemoteFallbackTried = false;
        BackgroundVideo.Visibility = Visibility.Visible;
        BackgroundVideo.Source ??= videoUri;

        if (BackgroundVideo.IsLoaded)
        {
            BackgroundVideo.Play();
            _isBackgroundVideoStarted = true;
        }
    }

    private static Uri? GetBackgroundVideoUri()
    {
        if (File.Exists(LocalBackgroundVideoPath))
        {
            return new Uri(LocalBackgroundVideoPath, UriKind.Absolute);
        }

        return IsBackgroundVideoRemoteFallbackEnabled()
            ? BackgroundVideoUri
            : null;
    }

    public void HideToTrayOnStartup()
    {
        if (_isRunInBackgroundEnabled)
        {
            HideToTray(showNotification: false);
        }
    }

    private void HideToTray(bool showNotification)
    {
        EnsureTrayIcon();
        StopBackgroundVideo();
        ShowInTaskbar = false;
        Hide();
        _diagnostics.Info("ui.tray", "Discorder bildirim alanına alındı.");

        if (showNotification && !_hasShownTrayNotice && _trayIcon is not null)
        {
            _hasShownTrayNotice = true;
            _trayIcon.ShowBalloonTip(
                2500,
                "Discorder arka planda çalışıyor",
                "Geri açmak veya tamamen çıkmak için bildirim alanı simgesini kullanın.",
                Forms.ToolTipIcon.Info);
        }
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        UpdateBackgroundVideoForSnapshot(_controller.Snapshot);
        _diagnostics.Info("ui.tray", "Discorder penceresi geri açıldı.");
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Göster", null, (_, _) =>
            Dispatcher.BeginInvoke(new Action(RestoreFromTray)));
        menu.Items.Add("Bağlantıyı kes", null, (_, _) =>
            Dispatcher.BeginInvoke(new Action(async () =>
                await DisconnectFromTrayAsync())));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) =>
            Dispatcher.BeginInvoke(new Action(ExitFromTray)));

        _trayIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = LoadTrayIcon(),
            Text = "Discorder",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) =>
            Dispatcher.BeginInvoke(new Action(RestoreFromTray));
    }

    private async Task DisconnectFromTrayAsync()
    {
        if (_controller.Snapshot.IsBusy)
        {
            _operationCancellation?.Cancel();
        }

        await _controller.DisconnectAsync(CancellationToken.None);
    }

    private void ExitFromTray()
    {
        _allowClose = true;
        RestoreFromTray();
        Close();
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return Drawing.Icon.ExtractAssociatedIcon(path)
                ?? Drawing.SystemIcons.Application;
        }

        return Drawing.SystemIcons.Application;
    }

    private void OpenWireSock_Click(object sender, RoutedEventArgs e)
    {
        OpenUri(_wireSockBootstrapper.ProductPage);
    }

    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _paths.EnsureDirectories();
            _diagnostics.Info("ui.diagnostics", "Tanılama istendi.");
            _diagnostics.WriteHealth(
                "tanılama istendi",
                new Dictionary<string, string?>
                {
                    ["browserAccess"] = _controller.IncludeBrowserAccess.ToString(),
                    ["state"] = _controller.Snapshot.State.ToString()
                });
            var bundlePath = _diagnostics.CreateBundle();
            var bundleDirectory = !string.IsNullOrWhiteSpace(bundlePath)
                ? Path.GetDirectoryName(bundlePath)
                : _paths.DiagnosticBundleDirectory;
            DiagnosticsStatus.Text = "Son tanılama hazır. Klasör açıldı.";

            if (!string.IsNullOrWhiteSpace(bundlePath))
            {
                MessageBox.Show(
                    "Tanılama hazırlandı.\n\n" + bundlePath,
                    "Discorder tanılama",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = bundleDirectory ?? _paths.DiagnosticBundleDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            DiagnosticsStatus.Text = "Tanılama hazırlanamadı. Ayrıntı loglara yazıldı.";
            _diagnostics.Failure(
                "ui.diagnostics",
                "Tanılama hazırlanamadı.",
                exception);
            MessageBox.Show(
                "Tanılama hazırlanamadı.\n\n" + exception.Message,
                "Discorder tanılama",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        OpenUri(RepositoryUri);
    }

    private async void AutoUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdateOperationRunning)
        {
            return;
        }

        if (_controller.Snapshot.IsBusy)
        {
            MessageBox.Show(
                "Devam eden işlem bitince güncellemeyi tekrar başlatın.",
                "Discorder güncelleme",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _isUpdateOperationRunning = true;
        SetUpdateControlsEnabled(false);
        ToggleButton.IsEnabled = false;
        RepairButton.IsEnabled = false;

        try
        {
            _diagnostics.Info("ui.update.check", "Güncelleme denetimi istendi.");
            DiagnosticsStatus.Text = "Güncelleme denetleniyor…";
            SetUpdateStageProgress(12, "Güncelleme denetleniyor");
            using var timeoutSource = new CancellationTokenSource(
                TimeSpan.FromMinutes(3));
            var update = await _updateService.CheckLatestUpdateAsync(
                GetCurrentVersion(),
                timeoutSource.Token);

            if (update.Status == AppUpdateCheckStatus.UpToDate)
            {
                _pendingUpdate = null;
                DiagnosticsStatus.Text = "Discorder güncel.";
                SetUpdateStageProgress(100, "Discorder güncel");
                MessageBox.Show(
                    $"Discorder güncel.\n\nKurulu sürüm: v{AppUpdateService.FormatVersion(update.CurrentVersion)}",
                    "Discorder güncelleme",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _pendingUpdate = update;
            DiagnosticsStatus.Text =
                $"v{AppUpdateService.FormatVersion(update.LatestVersion)} bulundu. Yükle, bu Discorder klasörünü günceller.";
            SetUpdateStageProgress(100, "Güncelleme bulundu");
            _diagnostics.Info(
                "ui.update.available",
                "Güncelleme bulundu.",
                new Dictionary<string, string?>
                {
                    ["latestVersion"] = AppUpdateService.FormatVersion(update.LatestVersion),
                    ["expectedSha256"] = update.ExpectedSha256
                });
        }
        catch (OperationCanceledException)
        {
            DiagnosticsStatus.Text = "Güncelleme denetimi iptal edildi.";
            SetUpdateStageProgress(100, "Denetim iptal edildi");
        }
        catch (Exception exception)
        {
            _pendingUpdate = null;
            DiagnosticsStatus.Text = "Güncelleme denetlenemedi. Mevcut sürüm kullanılabilir.";
            SetUpdateStageProgress(100, "Denetim tamamlanamadı");
            _diagnostics.Failure(
                "ui.update",
                "Güncelleme denetimi tamamlanamadı.",
                exception);
            MessageBox.Show(
                "Güncelleme denetlenemedi.\n\n" + exception.Message,
                "Discorder güncelleme",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isUpdateOperationRunning = false;

            if (!_allowClose)
            {
                ApplySnapshot(_controller.Snapshot);
            }
        }
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdateOperationRunning)
        {
            return;
        }

        if (_pendingUpdate is null
            || _pendingUpdate.Status != AppUpdateCheckStatus.UpdateAvailable)
        {
            DiagnosticsStatus.Text = "Önce güncellemeyi denetleyin.";
            return;
        }

        if (_controller.Snapshot.IsBusy)
        {
            MessageBox.Show(
                "Devam eden işlem bitince güncellemeyi yükleyin.",
                "Discorder güncelleme",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _isUpdateOperationRunning = true;
        SetUpdateControlsEnabled(false);
        ToggleButton.IsEnabled = false;
        RepairButton.IsEnabled = false;

        try
        {
            _diagnostics.Info("ui.update.install", "Güncelleme yükleme istendi.");
            ResetUpdateProgressLogThrottle();

            if (_controller.Snapshot.IsConnected)
            {
                DiagnosticsStatus.Text = "Bağlantı kapatılıyor. Ardından güncelleme yüklenecek.";
                SetUpdateStageProgress(8, "Bağlantı kapatılıyor");
                await _controller.DisconnectAsync(CancellationToken.None);
            }

            using var timeoutSource = new CancellationTokenSource(
                TimeSpan.FromMinutes(15));
            var executableName = GetExecutableName();
            DiagnosticsStatus.Text =
                $"v{AppUpdateService.FormatVersion(_pendingUpdate.LatestVersion)} indiriliyor ve doğrulanıyor…";
            SetUpdateStageProgress(18, "Güncelleme hazırlanıyor");
            var progress = new Progress<AppUpdateProgress>(
                value => SetUpdateProgress(value));
            var update = await _updateService.PrepareCheckedUpdateAsync(
                _pendingUpdate,
                AppContext.BaseDirectory,
                executableName,
                timeoutSource.Token,
                progress);

            if (update.Status == AppUpdatePreparationStatus.UpToDate)
            {
                _pendingUpdate = null;
                DiagnosticsStatus.Text = "Discorder güncel.";
                SetUpdateStageProgress(100, "Discorder güncel");
                return;
            }

            DiagnosticsStatus.Text =
                "Güncelleme hazır. Discorder kapanıp yeni sürümle açılacak.";
            SetUpdateStageProgress(96, "Yükleme penceresi açılıyor");
            _diagnostics.Info(
                "ui.update.prepared",
                "Güncelleme paketi hazırlandı.",
                new Dictionary<string, string?>
                {
                    ["latestVersion"] = AppUpdateService.FormatVersion(update.LatestVersion),
                    ["packagePath"] = update.PackagePath,
                    ["payloadDirectory"] = update.PayloadDirectory,
                    ["expectedSha256"] = update.ExpectedSha256
                });

            await StartUpdateApplicatorAsync(
                update,
                executableName,
                timeoutSource.Token);
            SetUpdateStageProgress(100, "Discorder yeniden başlatılıyor");
            _allowClose = true;
            System.Windows.Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            DiagnosticsStatus.Text = "Güncelleme yüklemesi iptal edildi.";
            SetUpdateStageProgress(100, "Yükleme iptal edildi");
        }
        catch (Exception exception)
        {
            DiagnosticsStatus.Text = "Güncelleme yüklenemedi. Mevcut sürüm kullanılabilir.";
            SetUpdateStageProgress(100, "Güncelleme başarısız");
            _diagnostics.Failure(
                "ui.update.install",
                "Güncelleme yükleme tamamlanamadı.",
                exception);
            MessageBox.Show(
                "Güncelleme yüklenemedi.\n\nMevcut sürüm kullanılabilir. Ayrıntılar tanılama loglarına yazıldı.\n\n" + exception.Message,
                "Discorder güncelleme",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isUpdateOperationRunning = false;

            if (!_allowClose)
            {
                ApplySnapshot(_controller.Snapshot);
            }
        }
    }

    private async Task StartUpdateApplicatorAsync(
        AppUpdatePreparation update,
        string executableName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(update.ApplicatorPath)
            || string.IsNullOrWhiteSpace(update.PackagePath)
            || string.IsNullOrWhiteSpace(update.ExpectedSha256))
        {
            throw new InvalidOperationException(
                "Güncelleme hazırlığı eksik olduğu için uygulanamadı.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = update.ApplicatorPath,
            WorkingDirectory = Path.GetDirectoryName(update.ApplicatorPath),
            UseShellExecute = false,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal
        };
        startInfo.ArgumentList.Add("--process-id");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(
            CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--package");
        startInfo.ArgumentList.Add(update.PackagePath);
        startInfo.ArgumentList.Add("--expected-sha256");
        startInfo.ArgumentList.Add(update.ExpectedSha256);
        startInfo.ArgumentList.Add("--expected-version");
        startInfo.ArgumentList.Add(AppUpdateService.FormatVersion(update.LatestVersion));
        if (!string.IsNullOrWhiteSpace(update.ExpectedSignerThumbprint))
        {
            startInfo.ArgumentList.Add("--expected-signer-thumbprint");
            startInfo.ArgumentList.Add(update.ExpectedSignerThumbprint);
        }
        startInfo.ArgumentList.Add("--target-directory");
        startInfo.ArgumentList.Add(AppContext.BaseDirectory);
        startInfo.ArgumentList.Add("--executable-name");
        startInfo.ArgumentList.Add(executableName);
        startInfo.ArgumentList.Add("--log");
        startInfo.ArgumentList.Add(Path.Combine(_paths.LogDirectory, "update.log"));

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Güncelleme uygulayıcısı başlatılamadı.");
        await Task.WhenAny(
            process.WaitForExitAsync(cancellationToken),
            Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken));
        if (process.HasExited)
        {
            throw new InvalidOperationException(
                $"Güncelleme uygulayıcısı erken kapandı. Çıkış kodu: {process.ExitCode}.");
        }
    }

    private void SetUpdateProgress(AppUpdateProgress progress)
    {
        SetUpdateStageProgress(progress.Percent, progress.Message);
        DiagnosticsStatus.Text = string.IsNullOrWhiteSpace(progress.Detail)
            ? progress.Message
            : $"{progress.Message}. {progress.Detail}";
        if (ShouldLogUpdateProgress(progress))
        {
            _diagnostics.Info(
                "ui.update.progress",
                progress.Message,
                new Dictionary<string, string?>
                {
                    ["percent"] = progress.Percent.ToString("0", CultureInfo.InvariantCulture),
                    ["detail"] = progress.Detail
                });
        }
    }

    private void ResetUpdateProgressLogThrottle()
    {
        _lastLoggedUpdateProgressKey = null;
        _lastLoggedUpdateProgressPercent = -1;
    }

    private bool ShouldLogUpdateProgress(AppUpdateProgress progress)
    {
        var percent = (int)Math.Floor(progress.Percent);
        var key = IsByteDownloadDetail(progress.Detail)
            ? progress.Message
            : $"{progress.Message}|{progress.Detail}";
        if (!string.Equals(
                key,
                _lastLoggedUpdateProgressKey,
                StringComparison.Ordinal)
            || percent >= 100
            || percent - _lastLoggedUpdateProgressPercent >= 2)
        {
            _lastLoggedUpdateProgressKey = key;
            _lastLoggedUpdateProgressPercent = percent;
            return true;
        }

        return false;
    }

    private static bool IsByteDownloadDetail(string? detail)
    {
        return !string.IsNullOrWhiteSpace(detail)
            && detail.EndsWith(" indirildi.", StringComparison.Ordinal);
    }

    private static Version GetCurrentVersion()
    {
        return typeof(MainWindow).Assembly.GetName().Version
            ?? new Version(0, 0, 0, 0);
    }

    private static string GetExecutableName()
    {
        var executableName = Path.GetFileName(Environment.ProcessPath);
        return string.IsNullOrWhiteSpace(executableName)
            ? "Discorder.exe"
            : executableName;
    }

    private void ApplyUpdateControls(bool isBusy)
    {
        SetUpdateControlsEnabled(!_isUpdateOperationRunning && !isBusy);
        InstallUpdateButton.Visibility = _pendingUpdate?.Status == AppUpdateCheckStatus.UpdateAvailable
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SetUpdateControlsEnabled(bool enabled)
    {
        AutoUpdateButton.IsEnabled = enabled;
        InstallUpdateButton.IsEnabled = enabled
            && _pendingUpdate?.Status == AppUpdateCheckStatus.UpdateAvailable;
    }

    private static void OpenUri(Uri uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (_isRunInBackgroundEnabled)
        {
            e.Cancel = true;
            HideToTray(showNotification: true);
            return;
        }

        e.Cancel = true;
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        IsEnabled = false;
        StatusMessage.Text = "Discorder kapanıyor";
        StatusDetail.Text = "Bağlantı ve koruma durumu güvenle kapatılıyor.";
        _operationCancellation?.Cancel();
        StopBackgroundVideo();

        try
        {
            await _controller.DisposeAsync();
        }
        catch (Exception exception)
        {
            _diagnostics.Failure(
                "ui.shutdown",
                "Kapanış temizliği tamamlanamadı.",
                exception);
            _isClosing = false;
            IsEnabled = true;
            Show();
            Activate();
            StatusMessage.Text = "Kapanış tamamlanamadı";
            StatusDetail.Text = "Bağlantı koruması doğrulanamadı. Tanılama raporu hazırlayın.";
            return;
        }

        Dispose();
        _allowClose = true;
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopBackgroundVideo();
        DisposeTrayIcon();
        _controller.StatusChanged -= OnStatusChanged;
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        GC.SuppressFinalize(this);
    }
}
