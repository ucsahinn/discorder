using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Discorder.Core.Configuration;
using Discorder.Core.Connection;
using Discorder.Core.Diagnostics;
using Discorder.Core.Discord;
using Discorder.Core.Firewall;
using Discorder.Core.Infrastructure;
using Discorder.Core.Maintenance;
using Discorder.Core.Provisioning;
using Discorder.Core.Updates;
using Discorder.Core.WireSock;
using Discorder.App.Installation;
using Discorder.App.Security;
using MessageBox = System.Windows.MessageBox;

namespace Discorder.App;

public partial class App : System.Windows.Application, IDisposable
{
    private const string MutexName = @"Local\Discorder.ucsahinn.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private HttpClient? _httpClient;
    private DiscordTunnelController? _tunnelController;
    private DiscorderDiagnostics? _diagnostics;
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
                "Discorder zaten çalışıyor. Simge bildirim alanında olabilir.",
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
        _diagnostics = new DiscorderDiagnostics(_paths);
        _diagnostics.Info(
            "app.startup",
            "Discorder başlatıldı.",
            new Dictionary<string, string?>
            {
                ["args"] = string.Join(" ", e.Args),
                ["processPath"] = Environment.ProcessPath
            });

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(20),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Discorder", "2.0.23"));

        var downloader = new VerifiedDownloader(_httpClient, maxAttempts: 5);
        var wireSockLocator = new WireSockLocator();
        var settingsStore = new AppSettingsStore(_paths);
        var wireSockBootstrapper = new WireSockBootstrapper(
            _paths,
            settingsStore,
            wireSockLocator,
            downloader,
            new WireSockPackageVerifier(),
            new WindowsElevatedInstallerLauncher());
        var updateService = new AppUpdateService(_httpClient, _paths, downloader);
        var commandRunner = new CommandRunner();
        var provisioner = new WgcfProvisioner(
            _paths,
            downloader,
            commandRunner);
        var accessLock = new WindowsFirewallDiscordAccessLock(
            _paths,
            commandRunner);

        _tunnelController = new DiscordTunnelController(
            _paths,
            new DiscordAppScope(),
            wireSockBootstrapper,
            provisioner,
            new ProcessLauncher(),
            accessLock: accessLock,
            diagnostics: _diagnostics);

        var window = new MainWindow(
            _tunnelController,
            _paths,
            wireSockBootstrapper,
            settingsStore,
            new DiscorderCleanupService(_paths, accessLock, _diagnostics),
            new WindowsStartupLaunchService(),
            new WindowsWireSockUninstaller(_diagnostics),
            updateService,
            _diagnostics);
        MainWindow = window;
        window.Show();

        if (e.Args.Any(arg => string.Equals(
                arg,
                "--background-start",
                StringComparison.OrdinalIgnoreCase))
            && settingsStore.IsRunInBackgroundOnCloseEnabled())
        {
            window.HideToTrayOnStartup();
        }
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
        _diagnostics?.Info("app.exit", "Discorder kapatıldı.");

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
        _diagnostics?.Failure(
            "app.dispatcherUnhandledException",
            "Beklenmeyen arayüz hatası yakalandı.",
            e.Exception);
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
            _diagnostics?.Failure(
                "app.unhandledException",
                "Beklenmeyen uygulama hatası yakalandı.",
                exception);
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
