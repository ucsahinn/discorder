using System.IO;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Discorder.App;
using Discorder.App.Installation;
using Discorder.App.Security;
using Discorder.Core.Configuration;
using Discorder.Core.Connection;
using Discorder.Core.Discord;
using Discorder.Core.Firewall;
using Discorder.Core.Infrastructure;
using Discorder.Core.Maintenance;
using Discorder.Core.Provisioning;
using Discorder.Core.Updates;
using Discorder.Core.WireSock;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("ATLANDI Windows paket doğrulama testleri Windows gerektirir.");
    return 0;
}

var verifier = new WireSockPackageVerifier();

if (args.Length == 1)
{
    verifier.VerifyInstaller(Path.GetFullPath(args[0]));
    Console.WriteLine("GEÇTİ Resmi WireSock kurucu güven doğrulaması");
    return 0;
}

RenderWindows();

var temporaryFile = Path.Combine(
    Path.GetTempPath(),
    $"Discorder-unsigned-{Guid.NewGuid():N}.exe");

try
{
    await File.WriteAllTextAsync(temporaryFile, "authenticode-imzali-olmayan-paket");

    try
    {
        verifier.VerifyInstaller(temporaryFile);
    }
    catch (InvalidDataException)
    {
        Console.WriteLine("GEÇTİ İmzasız WireSock paketi reddedildi");
        return 0;
    }

    Console.Error.WriteLine("KALDI İmzasız WireSock paketi kabul edildi");
    return 1;
}
finally
{
    File.Delete(temporaryFile);
}

static void RenderWindows()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var application = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/Discorder;component/Resources/Theme.xaml",
                    UriKind.Absolute)
            });

            application.Startup += (_, _) =>
            {
                try
                {
                    RenderMainWindow();
                    RenderConsentWindow();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    application.Shutdown();
                }
            };

            application.Run();
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure is not null)
    {
        ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

static void RenderMainWindow()
{
    MainWindow? window = null;
    var root = CreateTemporaryDirectory();
    var previousVideoSetting = Environment.GetEnvironmentVariable(
        "DISCORDER_DISABLE_BACKGROUND_VIDEO");
    var previousRemoteFallbackSetting = Environment.GetEnvironmentVariable(
        "DISCORDER_BACKGROUND_VIDEO_REMOTE_FALLBACK");

    try
    {
        Environment.SetEnvironmentVariable(
            "DISCORDER_DISABLE_BACKGROUND_VIDEO",
            null);
        Environment.SetEnvironmentVariable(
            "DISCORDER_BACKGROUND_VIDEO_REMOTE_FALLBACK",
            null);

        window = CreateMainWindow(root);
        window.Show();
        window.UpdateLayout();

        var backgroundVideo = FindVisualChildren<MediaElement>(window).Single();
        Assert(backgroundVideo.Visibility == Visibility.Collapsed);
        Assert(backgroundVideo.Source is null);

        var text = string.Join(
            "\n",
            FindVisualChildren<TextBlock>(window).Select(block => block.Text));
        Assert(text.Contains("Discorder", StringComparison.Ordinal));
        Assert(text.Contains("Çalışma modu hazır", StringComparison.Ordinal));
        Assert(text.Contains("Çalışma modu", StringComparison.Ordinal));
        Assert(text.Contains("Tarayıcı modu", StringComparison.Ordinal));
        Assert(text.Contains("İŞLETİM MERKEZİ", StringComparison.Ordinal));
        Assert(text.Contains("Arka planda çalıştır", StringComparison.Ordinal));
        Assert(text.Contains("Windows açılışında çalıştır", StringComparison.Ordinal));
        Assert(text.Contains("Tanılama", StringComparison.Ordinal));
        Assert(text.Contains("Hazır", StringComparison.Ordinal));
        Assert(text.Contains("Discorder Bağlı Değil", StringComparison.Ordinal));
        Assert(text.Contains(
            "Bağlantı sorunlarını incelemek için rapor hazırla",
            StringComparison.Ordinal));
        var buttons = FindVisualChildren<Button>(window)
            .Select(button => button.Content?.ToString())
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .ToArray();
        Assert(buttons.Contains("🛠 Onar"));
        Assert(buttons.Contains("⛔ Uygulamayı kaldır"));
        Assert(buttons.Contains("🧾 Tanılama"));
        Assert(buttons.Contains("↻ Güncelle"));
        Assert(buttons.Contains("Yükle"));
        var installUpdateButton = FindVisualChildren<Button>(window)
            .Single(button => button.Name == "InstallUpdateButton");
        Assert(installUpdateButton.Visibility == Visibility.Collapsed);
        var switches = FindVisualChildren<CheckBox>(window).ToArray();
        var browserSwitch = switches.Single(toggle =>
            toggle.Name == "BrowserAccessToggle");
        var runInBackgroundSwitch = switches.Single(toggle =>
            toggle.Name == "RunInBackgroundToggle");
        var startupSwitch = switches.Single(toggle =>
            toggle.Name == "StartupToggle");
        Assert(browserSwitch.IsChecked == true);
        Assert(runInBackgroundSwitch.IsChecked == false);
        Assert(startupSwitch.IsChecked == false);
        Assert(FindVisualChildren<ProgressBar>(window).Any());
        Assert(text.Contains("DNS SUNUCUSU", StringComparison.Ordinal));
        Assert(text.Contains("BAĞLANTI DURUMU", StringComparison.Ordinal));
        Assert(text.Contains("UYGULAMA KAPSAMI", StringComparison.Ordinal));
        Assert(!text.Contains("ÖZEL BAĞLANTI", StringComparison.Ordinal));
        Assert(!text.Contains("Discord bağlantısını yönet", StringComparison.Ordinal));
        Assert(!text.Contains("KAPANIŞ", StringComparison.Ordinal));
        Assert(!text.Contains("Advanced SplitWire", StringComparison.OrdinalIgnoreCase));
        Assert(!text.Contains("Discord-only", StringComparison.OrdinalIgnoreCase));

        SaveWindowPng(window, Path.Combine(
            FindRepositoryRoot(),
            "artifacts",
            "ui-window.png"));

        window.Close();
        window = null;

        Environment.SetEnvironmentVariable(
            "DISCORDER_BACKGROUND_VIDEO_REMOTE_FALLBACK",
            "1");

        window = CreateMainWindow(root);
        window.Show();
        window.UpdateLayout();

        backgroundVideo = FindVisualChildren<MediaElement>(window).Single();
        Assert(backgroundVideo.Visibility == Visibility.Visible);
        Assert(backgroundVideo.Source is not null);
    }
    finally
    {
        window?.Close();
        Environment.SetEnvironmentVariable(
            "DISCORDER_DISABLE_BACKGROUND_VIDEO",
            previousVideoSetting);
        Environment.SetEnvironmentVariable(
            "DISCORDER_BACKGROUND_VIDEO_REMOTE_FALLBACK",
            previousRemoteFallbackSetting);
        Directory.Delete(root, recursive: true);
    }

    Console.WriteLine("GEÇTİ Ana pencere çizildi");
}

static MainWindow CreateMainWindow(string root)
{
    var bootstrapper = new FakeWireSockBootstrapper();
    var paths = new AppPaths(root);
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        bootstrapper,
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(),
        TimeSpan.Zero);

    return new MainWindow(
        controller,
        paths,
        bootstrapper,
        new AppSettingsStore(paths),
        new DiscorderCleanupService(
            paths,
            new NullDiscordAccessLock()),
        new FakeStartupLaunchService(),
        new FakeWireSockUninstaller(),
        new AppUpdateService(
            new HttpClient(),
            paths,
            new FakeVerifiedDownloader(),
            requireUpdateAuthenticode: false));
}

static void RenderConsentWindow()
{
    WireSockConsentWindow? window = null;

    try
    {
        window = new WireSockConsentWindow(new FakeWireSockBootstrapper());
        window.Show();
        window.UpdateLayout();

        var text = string.Join(
            "\n",
            FindVisualChildren<TextBlock>(window).Select(block => block.Text));
        Assert(text.Contains("İlk kurulum gerekiyor", StringComparison.Ordinal));
        Assert(text.Contains("WireSock VPN Client 1.4.7.1", StringComparison.Ordinal));
        Assert(text.Contains("Cloudflare WARP", StringComparison.Ordinal));

        var buttons = FindVisualChildren<Button>(window)
            .Select(button => button.Content?.ToString())
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .ToArray();
        Assert(buttons.Contains("Lisansı aç"));
        Assert(buttons.Contains("WARP koşulları"));
        Assert(buttons.Contains("Vazgeç"));
        Assert(buttons.Contains("Kabul et ve kur"));

        SaveWindowPng(window, Path.Combine(
            FindRepositoryRoot(),
            "artifacts",
            "ui-consent-window.png"));
    }
    finally
    {
        window?.Close();
    }

    Console.WriteLine("GEÇTİ WireSock kurulum onay penceresi çizildi");
}

static void SaveWindowPng(Window window, string outputPath)
{
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    var width = (int)Math.Ceiling(window.ActualWidth);
    var height = (int)Math.Ceiling(window.ActualHeight);
    Assert(width >= 500);
    Assert(height >= 470);

    var bitmap = new RenderTargetBitmap(
        width,
        height,
        96,
        96,
        PixelFormats.Pbgra32);
    bitmap.Render(window);

    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(outputPath);
    encoder.Save(stream);
}

static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
    where T : DependencyObject
{
    for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
    {
        var child = VisualTreeHelper.GetChild(root, index);

        if (child is T typedChild)
        {
            yield return typedChild;
        }

        foreach (var descendant in FindVisualChildren<T>(child))
        {
            yield return descendant;
        }
    }
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Environment.CurrentDirectory);

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Discorder.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Environment.CurrentDirectory;
}

static string CreateTemporaryDirectory()
{
    var path = Path.Combine(
        Path.GetTempPath(),
        "Discorder.Windows.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void Assert(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Doğrulama koşulu başarısız oldu.");
    }
}

file sealed class FakeWireSockBootstrapper : IWireSockBootstrapper
{
    public string RequiredVersion => WireSockPackage.Version;

    public Uri ProductPage => WireSockPackage.ProductPage;

    public bool IsInstalled => false;

    public bool IsSetupConsentAccepted => false;

    public void AcceptSetupConsent()
    {
    }

    public Task<string> EnsureInstalledAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}

file sealed class FakeStartupLaunchService : IStartupLaunchService
{
    public bool Enabled { get; private set; }

    public bool IsEnabled() => Enabled;

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
    }
}

file sealed class FakeWireSockUninstaller : IWireSockUninstaller
{
    public Task UninstallIfDiscorderInstalledAsync(
        bool installedByDiscorder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

file sealed class FakeVerifiedDownloader : IVerifiedDownloader
{
    public Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken,
        long? maxBytes = null,
        IProgress<DownloadProgress>? progress = null)
    {
        throw new NotSupportedException();
    }
}

file sealed class FakeProfileProvisioner(string profilePath) : IProfileProvisioner
{
    public Task<string> EnsureProfileAsync(
        IReadOnlyList<string> allowedApplications,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(profilePath);
    }
}

file sealed class FakeCommandRunner : ICommandRunner
{
    public Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new CommandResult(0, string.Empty, string.Empty));
    }
}

file sealed class FakeProcessLauncher : IProcessLauncher
{
    public IManagedProcess Start(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath)
    {
        return new FakeManagedProcess();
    }
}

file sealed class FakeManagedProcess : IManagedProcess
{
    public event EventHandler? Exited;

    public bool HasExited { get; private set; }

    public int? ExitCode => HasExited ? 0 : null;

    public Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        HasExited = true;
        Exited?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        HasExited = true;
        return ValueTask.CompletedTask;
    }
}
