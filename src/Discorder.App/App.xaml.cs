using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Discorder.Core.Configuration;
using Discorder.Core.Connection;
using Discorder.Core.Discord;
using Discorder.Core.Firewall;
using Discorder.Core.Infrastructure;
using Discorder.Core.Provisioning;
using Discorder.Core.WireSock;
using Discorder.App.Installation;
using Discorder.App.Security;

namespace Discorder.App;

public partial class App : Application, IDisposable
{
    private const string MutexName = @"Local\Discorder.ucsahinn.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private HttpClient? _httpClient;
    private DiscordTunnelController? _tunnelController;
    private AppPaths? _paths;
    private bool _disposed;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            MutexName,
            out var createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "Discorder zaten çalışıyor.",
                "Discorder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        _paths = new AppPaths();
        _paths.EnsureDirectories();

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Discorder", "2.0.0"));

        var downloader = new VerifiedDownloader(_httpClient);
        var wireSockLocator = new WireSockLocator();
        var settingsStore = new AppSettingsStore(_paths);
        var wireSockBootstrapper = new WireSockBootstrapper(
            _paths,
            settingsStore,
            wireSockLocator,
            downloader,
            new WireSockPackageVerifier(),
            new WindowsElevatedInstallerLauncher());
        var commandRunner = new CommandRunner();
        var provisioner = new WgcfProvisioner(
            _paths,
            downloader,
            commandRunner);

        _tunnelController = new DiscordTunnelController(
            _paths,
            new DiscordAppScope(),
            wireSockBootstrapper,
            provisioner,
            new ProcessLauncher(),
            accessLock: new WindowsFirewallDiscordAccessLock(
                _paths,
                commandRunner));

        var window = new MainWindow(
            _tunnelController,
            _paths,
            wireSockBootstrapper,
            settingsStore);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;

        if (_tunnelController is not null)
        {
            _tunnelController.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _httpClient?.Dispose();

        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _singleInstanceMutex.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception);
        MessageBox.Show(
            "Beklenmeyen bir hata oluştu. Ayrıntılar tanılama günlüğüne yazıldı.",
            "Discorder",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogException(exception);
        }
    }

    private void LogException(Exception exception)
    {
        try
        {
            if (_paths is null)
            {
                return;
            }

            File.AppendAllText(
                _paths.ErrorLog,
                $"{DateTimeOffset.Now:O}{Environment.NewLine}" +
                $"{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logException)
        {
            Debug.WriteLine(logException);
        }
    }
}
