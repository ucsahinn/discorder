using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Discorder.Core.Configuration;
using Discorder.Core.Connection;
using Discorder.Core.WireSock;

namespace Discorder.App;

public partial class MainWindow : Window, IDisposable
{
    private static readonly Uri RepositoryUri = new(
        "https://github.com/ucsahinn/discorder");

    private readonly DiscordTunnelController _controller;
    private readonly AppPaths _paths;
    private readonly IWireSockBootstrapper _wireSockBootstrapper;
    private CancellationTokenSource? _operationCancellation;
    private bool _allowClose;
    private bool _isClosing;
    private bool _disposed;

    public MainWindow(
        DiscordTunnelController controller,
        AppPaths paths,
        IWireSockBootstrapper wireSockBootstrapper)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _wireSockBootstrapper = wireSockBootstrapper
            ?? throw new ArgumentNullException(nameof(wireSockBootstrapper));

        InitializeComponent();
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
            TunnelState.Connected => ("AÇIK", Color.FromRgb(59, 165, 92)),
            TunnelState.Error => ("HATA", Color.FromRgb(237, 66, 69)),
            TunnelState.Preparing or TunnelState.Connecting or TunnelState.Disconnecting
                => ("İŞLENİYOR", Color.FromRgb(250, 166, 26)),
            _ => ("KAPALI", Color.FromRgb(157, 166, 178))
        };

        var brush = new SolidColorBrush(color);
        StatusLabel.Text = label;
        StatusDot.Fill = brush;
        ToggleButton.BorderBrush = brush;

        if (powerIcon is not null)
        {
            powerIcon.Foreground = brush;
        }
    }

    private static string GetDetail(TunnelState state)
    {
        return state switch
        {
            TunnelState.Connected => "Discord uygulamaları ve desteklenen tarayıcılar kapsamda.",
            TunnelState.Preparing => "Kurulum, dijital imza ve Discord web profili doğrulanıyor.",
            TunnelState.Connecting => "WireSock VPN Client süreci başlatılıyor.",
            TunnelState.Disconnecting => "Tünel süreci güvenli biçimde sonlandırılıyor.",
            TunnelState.Error => "Tanılama klasörünü açarak ayrıntıları inceleyin.",
            _ => "Discorder kapalıyken VPN süreci çalışmaz."
        };
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

        e.Cancel = true;
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        IsEnabled = false;
        _operationCancellation?.Cancel();

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.StatusChanged -= OnStatusChanged;
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        GC.SuppressFinalize(this);
    }
}
