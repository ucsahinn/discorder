using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Discorder.App.Installation;
using Discorder.Core.Configuration;
using Discorder.Core.Connection;
using Discorder.Core.Diagnostics;
using Discorder.Core.Maintenance;
using Discorder.Core.WireSock;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;

namespace Discorder.App;

public partial class MainWindow : Window, IDisposable
{
    private static readonly Uri RepositoryUri = new(
        "https://github.com/ucsahinn/discorder");
    private static readonly Uri BackgroundVideoUri = new(
        "https://d8j0ntlcm91z4.cloudfront.net/user_38xzZboKViGWJOttwIXH07lWA1P/hf_20260606_154941_df1a96e1-a06f-450c-bd02-d863414cc1a0.mp4");

    private readonly DiscordTunnelController _controller;
    private readonly AppPaths _paths;
    private readonly IWireSockBootstrapper _wireSockBootstrapper;
    private readonly AppSettingsStore _settingsStore;
    private readonly DiscorderCleanupService _cleanupService;
    private readonly IStartupLaunchService _startupLaunchService;
    private readonly IWireSockUninstaller _wireSockUninstaller;
    private readonly IDiscorderDiagnostics _diagnostics;
    private bool _isApplyingSettings;
    private bool _isRunInBackgroundEnabled;
    private bool _isToggleOperationRunning;
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

            await _controller.ToggleAsync(_operationCancellation.Token);
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
        ToggleButton.IsEnabled = !_isToggleOperationRunning && !snapshot.IsBusy;
        RepairButton.IsEnabled = !snapshot.IsBusy;
        StatusMessage.Text = snapshot.Message;
        StatusDetail.Text = GetDetail(snapshot.State);
        ApplyBrowserAccessView(
            _controller.IncludeBrowserAccess,
            snapshot.IsBusy || snapshot.IsConnected);

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
            outerRing.Effect = CreateGlowEffect(58, 0.95, visual.GlowColor);
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

        ApplyProgress(snapshot, visual.StatusBrush);
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
                "KAPATILIYOR",
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
            CoreFill = new SolidColorBrush(MediaColor.FromArgb(
                76,
                coreColor.R,
                coreColor.G,
                coreColor.B));
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

        public SolidColorBrush CoreFill { get; }

        public SolidColorBrush BadgeBackground { get; }
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
            TunnelState.Connected => (100, "Tünel açık", 4),
            TunnelState.Connecting => (82, "Tünel başlatılıyor", 4),
            TunnelState.Disconnecting => (62, "Bağlantı kapatılıyor", 4),
            TunnelState.Error => (100, "Müdahale gerekiyor", 4),
            TunnelState.Preparing when message.Contains(
                    "kilidi",
                    StringComparison.OrdinalIgnoreCase)
                => (22, "Discord kilidi kaldırılıyor", 1),
            TunnelState.Preparing when message.Contains(
                    "WireSock",
                    StringComparison.OrdinalIgnoreCase)
                || message.Contains("kurul", StringComparison.OrdinalIgnoreCase)
                || message.Contains("indir", StringComparison.OrdinalIgnoreCase)
                || message.Contains("doğrulan", StringComparison.OrdinalIgnoreCase)
                => (48, "WireSock doğrulanıyor", 2),
            TunnelState.Preparing when message.Contains(
                    "profil",
                    StringComparison.OrdinalIgnoreCase)
                || message.Contains("wgcf", StringComparison.OrdinalIgnoreCase)
                => (68, "Profil hazırlanıyor", 3),
            TunnelState.Preparing => (38, "Hazırlık çalışıyor", 2),
            _ => (8, "Hazır, Discord kilidi aktif", 0)
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
                ? "Web modunda Discord uygulaması ve desteklenen tarayıcılar kapsamda."
                : "Uygulama modunda yalnızca Discord uygulaması kapsamda.",
            TunnelState.Preparing => "Kurulum, dijital imza ve tünel profili doğrulanıyor.",
            TunnelState.Connecting => "WireSock VPN Client süreci başlatılıyor.",
            TunnelState.Disconnecting => "Tünel süreci güvenli biçimde sonlandırılıyor.",
            TunnelState.Error => "Tanılama klasörünü açarak ayrıntıları inceleyin.",
            _ => "Discorder kapalıyken Discord VPN kilidi aktif kalır."
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
                    ? "Web modu bu oturumda açık. Uygulama moduna geçmek için önce bağlantıyı kes."
                    : "Web modu açık. Discord uygulaması ve desteklenen tarayıcılar tünellenir."
                : locked
                    ? "Uygulama modu bu oturumda açık. Web moduna geçmek için önce bağlantıyı kes."
                    : "Uygulama modu açık. Yalnızca Discord uygulaması tünellenir.";
        }
        finally
        {
            _isApplyingSettings = false;
        }
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
                ? "Pencere kapanınca tray'de kalır."
                : "Pencere kapanınca güvenli biçimde kapanır.";
            CloseBehaviorSummary.Text = enabled
                ? "Arka planda kalır"
                : "Discord kilitlenir";

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
                    ? "Windows başlangıcı açıldı."
                    : "Windows başlangıcı kapatıldı.");
            ApplyStartupSetting(enabled);
        }
        catch (Exception exception)
        {
            var currentState = TryGetStartupLaunchState();
            _settingsStore.SetStartWithWindowsEnabled(currentState);
            ApplyStartupSetting(currentState);

            MessageBox.Show(
                "Windows başlangıç ayarı kaydedilemedi.\n\n" + exception.Message,
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
                ? "Oturum açılışında otomatik başlar."
                : "Oturum açılışında başlamaz.";
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
            "Discorder bağlantıyı kapatacak, hosts ve Windows Firewall üzerindeki Discorder kilidini geri alacak, " +
            "%LOCALAPPDATA%\\Discorder altındaki profil, ayar, wgcf, kurucu, tanılama paketi ve log dosyalarını silecek. " +
            "WireSock VPN Client Discorder tarafından kurulduysa Windows'tan kaldırılacak.\n\nDevam edilsin mi?",
            "Discorder temiz kaldır",
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
        StatusMessage.Text = "Temiz kaldırma çalışıyor";
        StatusDetail.Text = "Tünel kapatılıyor, WireSock ve Discorder izleri temizleniyor.";
        SetMaintenanceProgress(24, "Tünel kapatılıyor");
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
            SetMaintenanceProgress(78, "Yerel veriler siliniyor");
            await _cleanupService.CleanUninstallAsync(CancellationToken.None);

            MessageBox.Show(
                "Discorder temiz kaldırıldı. Uygulama kapanacak.",
                "Discorder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _allowClose = true;
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            IsEnabled = true;
            StatusMessage.Text = "Temiz kaldırma tamamlanamadı";
            StatusDetail.Text = exception.Message;

            MessageBox.Show(
                "Temiz kaldırma tamamlanamadı.\n\n" + exception.Message,
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
        BrowserAccessToggle.IsEnabled = false;
        _operationCancellation?.Cancel();
        StatusMessage.Text = "Onarım çalışıyor";
        StatusDetail.Text = "Tünel kapatılıyor, Discord kilidi kuruluyor ve profil dosyaları yenileniyor.";
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

    private void BackgroundVideo_Loaded(object sender, RoutedEventArgs e)
    {
        StartBackgroundVideo();
    }

    private void BackgroundVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (IsBackgroundVideoDisabled())
        {
            return;
        }

        BackgroundVideo.Position = TimeSpan.Zero;
        BackgroundVideo.Play();
    }

    private void BackgroundVideo_MediaFailed(
        object sender,
        ExceptionRoutedEventArgs e)
    {
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

    private void StopBackgroundVideo()
    {
        try
        {
            BackgroundVideo.Stop();
            BackgroundVideo.Visibility = Visibility.Collapsed;
            BackgroundVideo.Source = null;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void StartBackgroundVideo()
    {
        if (IsBackgroundVideoDisabled())
        {
            StopBackgroundVideo();
            return;
        }

        BackgroundVideo.Visibility = Visibility.Visible;
        BackgroundVideo.Source ??= BackgroundVideoUri;

        if (BackgroundVideo.IsLoaded)
        {
            BackgroundVideo.Play();
        }
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
        StartBackgroundVideo();
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
            _diagnostics.Info("ui.diagnostics", "Tanılama paketi istendi.");
            _diagnostics.WriteHealth(
                "tanılama paketi istendi",
                new Dictionary<string, string?>
                {
                    ["browserAccess"] = _controller.IncludeBrowserAccess.ToString(),
                    ["state"] = _controller.Snapshot.State.ToString()
                });
            var bundlePath = _diagnostics.CreateBundle();
            DiagnosticsStatus.Text = "Son paket hazır. Log klasörü açıldı.";

            if (!string.IsNullOrWhiteSpace(bundlePath))
            {
                MessageBox.Show(
                    "Tanılama paketi hazırlandı.\n\n" + bundlePath,
                    "Discorder tanılama",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _paths.LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            DiagnosticsStatus.Text = "Tanılama paketi hazırlanamadı. Ayrıntı loglara yazıldı.";
            _diagnostics.Failure(
                "ui.diagnostics",
                "Tanılama paketi hazırlanamadı.",
                exception);
            MessageBox.Show(
                "Tanılama paketi hazırlanamadı.\n\n" + exception.Message,
                "Discorder tanılama",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        OpenUri(RepositoryUri);
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
        _operationCancellation?.Cancel();
        StopBackgroundVideo();

        try
        {
            await _controller.DisposeAsync();
        }
        finally
        {
            Dispose();
            _allowClose = true;
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
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
