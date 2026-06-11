using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Discorder.App.Installation;
using Discorder.Core.Configuration;
using Discorder.Core.Connection;
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
    private bool _isApplyingSettings;
    private bool _isBackgroundVideoEnabled = true;
    private bool _isRunInBackgroundEnabled;
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
        IWireSockUninstaller wireSockUninstaller)
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

        InitializeComponent();
        ApplyBrowserAccessSetting(_settingsStore.IsBrowserAccessEnabled());
        ApplyBackgroundVideoSetting(_settingsStore.IsBackgroundVideoEnabled());
        ApplyRunInBackgroundSetting(
            _settingsStore.IsRunInBackgroundOnCloseEnabled());
        ApplyStartupSetting(SynchronizeStartupLaunchSetting(
            _settingsStore.IsStartWithWindowsEnabled()));
        _controller.StatusChanged += OnStatusChanged;
        ApplySnapshot(_controller.Snapshot);
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.Snapshot.IsBusy)
        {
            return;
        }

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

        try
        {
            await _controller.ToggleAsync(_operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
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
        ToggleButton.IsEnabled = !snapshot.IsBusy;
        BrowserAccessToggle.IsEnabled = !snapshot.IsBusy && !snapshot.IsConnected;
        RepairButton.IsEnabled = !snapshot.IsBusy;
        StatusMessage.Text = snapshot.Message;
        StatusDetail.Text = GetDetail(snapshot.State);

        var templateLabel = ToggleButton.Template.FindName(
            "ToggleButtonLabel",
            ToggleButton) as System.Windows.Controls.TextBlock;
        var powerIcon = ToggleButton.Template.FindName(
            "PowerIcon",
            ToggleButton) as System.Windows.Controls.TextBlock;

        if (templateLabel is not null)
        {
            templateLabel.Text = snapshot.IsConnected ? "BAĞLANTIYI KES" : "BAĞLAN";
        }

        var (label, color) = snapshot.State switch
        {
            TunnelState.Connected => ("AÇIK", MediaColor.FromRgb(59, 165, 92)),
            TunnelState.Error => ("HATA", MediaColor.FromRgb(237, 66, 69)),
            TunnelState.Preparing or TunnelState.Connecting or TunnelState.Disconnecting
                => ("İŞLENİYOR", MediaColor.FromRgb(250, 166, 26)),
            _ => ("KAPALI", MediaColor.FromRgb(157, 166, 178))
        };

        var brush = new SolidColorBrush(color);
        StatusLabel.Text = label;
        StatusDot.Fill = brush;
        ToggleButton.BorderBrush = brush;

        if (powerIcon is not null)
        {
            powerIcon.Foreground = brush;
        }

        ApplyProgress(snapshot, brush);
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
            _ => (8, "Süreç hazır", 0)
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
                ? "Discord uygulamaları ve desteklenen tarayıcılar kapsamda."
                : "Varsayılan modda yalnızca Discord uygulamaları kapsamda.",
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
        _settingsStore.SetBrowserAccessEnabled(enabled);
        _controller.IncludeBrowserAccess = enabled;
        BrowserAccessStatus.Text = enabled
            ? "Tarayıcı modu açık. Discord web desteklenen tarayıcılardan tünellenir."
            : "Varsayılan mod. Yalnızca Discord uygulaması tünellenir.";
    }

    private void ApplyBrowserAccessSetting(bool enabled)
    {
        _isApplyingSettings = true;
        try
        {
            _controller.IncludeBrowserAccess = enabled;
            BrowserAccessToggle.IsChecked = enabled;
            BrowserAccessStatus.Text = enabled
                ? "Tarayıcı modu açık. Discord web desteklenen tarayıcılardan tünellenir."
                : "Varsayılan mod. Yalnızca Discord uygulaması tünellenir.";
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void BackgroundVideoToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        var enabled = BackgroundVideoToggle.IsChecked == true;
        _settingsStore.SetBackgroundVideoEnabled(enabled);
        ApplyBackgroundVideoState(enabled);
    }

    private void ApplyBackgroundVideoSetting(bool enabled)
    {
        _isApplyingSettings = true;
        try
        {
            BackgroundVideoToggle.IsChecked = enabled;
            ApplyBackgroundVideoState(enabled);
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void ApplyBackgroundVideoState(bool enabled)
    {
        _isBackgroundVideoEnabled = enabled;
        BackgroundVideoStatus.Text = enabled
            ? "Video açık, sahne canlı."
            : "Video kapalı, arayüz sade.";

        if (!enabled || IsBackgroundVideoDisabled())
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

    private void RunInBackgroundToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        var enabled = RunInBackgroundToggle.IsChecked == true;
        _settingsStore.SetRunInBackgroundOnCloseEnabled(enabled);
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
            "%LOCALAPPDATA%\\Discorder altındaki profil, ayar, wgcf, kurucu ve log dosyalarını silecek. " +
            "WireSock VPN Client bu uygulama tarafından kurulduysa Windows'tan kaldırılacak.\n\nDevam edilsin mi?",
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

        try
        {
            _startupLaunchService.SetEnabled(false);
            var removeWireSock = _settingsStore.IsWireSockInstalledByDiscorder();
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
            "Discorder bağlantıyı kapatacak; profil, wgcf, kurucu önbelleği ve logları sıfırlayacak. " +
            "Tarayıcı modu, video, arka plan çalışma ve başlangıç ayarları korunur.\n\nDevam edilsin mi?",
            "Discorder sıfırla",
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
        StatusMessage.Text = "Sıfırlama çalışıyor";
        StatusDetail.Text = "Tünel kapatılıyor, Discord kilidi kuruluyor ve profil dosyaları yenileniyor.";
        SetMaintenanceProgress(36, "Onarım hazırlanıyor");

        try
        {
            await _controller.DisconnectAsync(CancellationToken.None);
            SetMaintenanceProgress(68, "Profil ve önbellek temizleniyor");
            await _cleanupService.RepairAsync(CancellationToken.None);

            StatusMessage.Text = "Sıfırlama tamamlandı";
            StatusDetail.Text = "Sonraki Bağlan işleminde profil ve wgcf dosyaları yeniden üretilecek.";
            SetMaintenanceProgress(100, "Onarım tamamlandı");

            MessageBox.Show(
                "Discorder sıfırlandı. Sonraki bağlantıda profil yeniden oluşturulacak.",
                "Discorder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage.Text = "Sıfırlama tamamlanamadı";
            StatusDetail.Text = exception.Message;
            SetMaintenanceProgress(100, "Müdahale gerekiyor");

            MessageBox.Show(
                "Sıfırlama tamamlanamadı.\n\n" + exception.Message,
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
        if (!_isBackgroundVideoEnabled || IsBackgroundVideoDisabled())
        {
            StopBackgroundVideo();
            return;
        }

        BackgroundVideo.Visibility = Visibility.Visible;
        BackgroundVideo.Source ??= BackgroundVideoUri;
        BackgroundVideo.Play();
    }

    private void BackgroundVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (!_isBackgroundVideoEnabled || IsBackgroundVideoDisabled())
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
        BackgroundVideoStatus.Text = "Video yüklenemedi";
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
        if (_isBackgroundVideoEnabled)
        {
            ApplyBackgroundVideoState(enabled: true);
        }
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
        _paths.EnsureDirectories();
        Process.Start(new ProcessStartInfo
        {
            FileName = _paths.DataDirectory,
            UseShellExecute = true
        });
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
