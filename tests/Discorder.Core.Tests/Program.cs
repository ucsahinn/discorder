using System.Security.Cryptography;
using System.Text;
using Discorder.Core.Configuration;
using Discorder.Core.Connection;
using Discorder.Core.Discord;
using Discorder.Core.Infrastructure;
using Discorder.Core.Profiles;
using Discorder.Core.Provisioning;
using Discorder.Core.Security;
using Discorder.Core.WireSock;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Discord kapsamı yalnızca Discord uygulamalarını içerir", DiscordScopeIsStrictAsync),
    ("Profil üretici geniş AllowedApps değerlerini değiştirir", ProfileBuilderIsStrictAsync),
    ("Profil üretici yapılandırma enjeksiyonunu reddeder", ProfileBuilderRejectsInjectionAsync),
    ("wgcf yalnızca Discord profili üretir", WgcfProvisionerBuildsDiscordOnlyProfileAsync),
    ("SHA-256 doğrulayıcı yalnızca sabit özeti kabul eder", HashVerifierIsStrictAsync),
    ("Ayarlar WireSock onayını sürüm bazında saklar", SettingsPersistConsentAsync),
    ("WireSock hazırlığı onaysız kurulumu reddeder", BootstrapRequiresConsentAsync),
    ("WireSock hazırlığı güvenilir kurulumu yeniden kullanır", BootstrapReusesTrustedInstallAsync),
    ("WireSock hazırlığı güvenilmeyen kurulumu yok sayar", BootstrapIgnoresUntrustedInstallAsync),
    ("WireSock hazırlığı resmi paketi doğrulayıp kurar", BootstrapLifecycleAsync),
    ("WireSock hazırlığı yeniden başlatma gerektiren başarı kodunu kabul eder", BootstrapAcceptsRestartRequiredExitCodeAsync),
    ("Denetleyici idempotent bağlanır ve keser", ControllerLifecycleAsync),
    ("Denetleyici WireSock hazırlık hatasını bildirir", ControllerBootstrapFailureAsync)
};

var failures = new List<string>();

foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"GEÇTİ {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.Error.WriteLine($"KALDI {test.Name}");
        Console.Error.WriteLine(exception);
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"{failures.Count} test başarısız oldu.");
    return 1;
}

Console.WriteLine();
Console.WriteLine($"{tests.Length} test geçti.");
return 0;

static Task DiscordScopeIsStrictAsync()
{
    var root = CreateTemporaryDirectory();
    Directory.CreateDirectory(Path.Combine(root, "Discord"));
    Directory.CreateDirectory(Path.Combine(root, "Chrome"));

    try
    {
        var apps = new DiscordAppScope(root).GetAllowedApplications();

        Assert(apps.Any(app => app.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals(
            Path.Combine(root, "Discord"),
            StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => app.Contains("Discord", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Equals("Update.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Contains("chrome", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Contains("roblox", StringComparison.OrdinalIgnoreCase)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static Task ProfileBuilderIsStrictAsync()
{
    const string source = """
        [Interface]
        PrivateKey = secret
        Address = 172.16.0.2/32

        [Peer]
        PublicKey = public
        Endpoint = engage.cloudflareclient.com:2408
        AllowedApps = chrome.exe, roblox.exe, Update.exe
        AllowedIPs = 0.0.0.0/0
        """;

    var profile = WireGuardProfileBuilder.BuildDiscordOnly(
        source,
        ["Discord.exe", @"C:\Users\test\AppData\Local\Discord"]);

    Assert(profile.Contains("AllowedApps = ", StringComparison.Ordinal));
    Assert(profile.Contains("Discord.exe", StringComparison.Ordinal));
    Assert(!profile.Contains("chrome.exe", StringComparison.OrdinalIgnoreCase));
    Assert(!profile.Contains("roblox.exe", StringComparison.OrdinalIgnoreCase));
    Assert(!profile.Contains("Update.exe", StringComparison.OrdinalIgnoreCase));
    Assert(profile.Split("AllowedApps =", StringSplitOptions.None).Length == 2);
    return Task.CompletedTask;
}

static Task ProfileBuilderRejectsInjectionAsync()
{
    const string source = """
        [Interface]
        PrivateKey = secret
        [Peer]
        Endpoint = example.com:2408
        """;

    AssertThrows<InvalidDataException>(() =>
        WireGuardProfileBuilder.BuildDiscordOnly(
            source,
            ["Discord.exe\r\nAllowedApps = chrome.exe"]));
    return Task.CompletedTask;
}

static async Task WgcfProvisionerBuildsDiscordOnlyProfileAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var downloader = new FakeVerifiedDownloader();
    var commandRunner = new FakeWgcfCommandRunner(paths);
    var provisioner = new WgcfProvisioner(
        paths,
        downloader,
        commandRunner);

    try
    {
        var discordDirectory = Path.Combine(root, "Discord");
        var profilePath = await provisioner.EnsureProfileAsync(
            ["Discord.exe", discordDirectory],
            CancellationToken.None);

        var profile = await File.ReadAllTextAsync(profilePath);
        Assert(profilePath == paths.DiscordProfile);
        Assert(File.Exists(paths.WgcfExecutable));
        var allowedAppsLine = profile
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Single(line => line.StartsWith("AllowedApps =", StringComparison.Ordinal));
        Assert(allowedAppsLine.Contains("Discord.exe", StringComparison.Ordinal));
        Assert(allowedAppsLine.Contains(discordDirectory, StringComparison.OrdinalIgnoreCase));
        Assert(profile.Contains(discordDirectory, StringComparison.OrdinalIgnoreCase));
        Assert(!profile.Contains("chrome.exe", StringComparison.OrdinalIgnoreCase));
        Assert(!profile.Contains("roblox.exe", StringComparison.OrdinalIgnoreCase));
        Assert(!profile.Contains("Update.exe", StringComparison.OrdinalIgnoreCase));
        Assert(commandRunner.Commands.SequenceEqual(
            ["register --accept-tos", "generate"],
            StringComparer.Ordinal));

        await provisioner.EnsureProfileAsync(
            ["Discord.exe"],
            CancellationToken.None);
        Assert(commandRunner.Commands.Count == 2);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task HashVerifierIsStrictAsync()
{
    var root = CreateTemporaryDirectory();
    var path = Path.Combine(root, "payload.bin");
    var payload = Encoding.UTF8.GetBytes("discorder");
    await File.WriteAllBytesAsync(path, payload);
    var expected = Convert.ToHexString(SHA256.HashData(payload));

    try
    {
        Assert(await FileHashVerifier.MatchesSha256Async(path, expected));
        Assert(!await FileHashVerifier.MatchesSha256Async(path, new string('0', 64)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task SettingsPersistConsentAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var paths = new AppPaths(root);
        var firstStore = new AppSettingsStore(paths);
        Assert(!firstStore.IsSetupConsentAccepted(WireSockPackage.Version));

        firstStore.AcceptSetupConsent(WireSockPackage.Version);

        var reloadedStore = new AppSettingsStore(paths);
        Assert(reloadedStore.IsSetupConsentAccepted(WireSockPackage.Version));
        Assert(!reloadedStore.IsSetupConsentAccepted("next-version"));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static async Task BootstrapRequiresConsentAsync()
{
    var root = CreateTemporaryDirectory();
    var downloader = new FakeVerifiedDownloader();
    var verifier = new FakeWireSockPackageVerifier();
    var launcher = new FakeInstallerLauncher();
    var bootstrapper = new WireSockBootstrapper(
        new AppPaths(root),
        new AppSettingsStore(new AppPaths(root)),
        new MutableWireSockLocator(),
        downloader,
        verifier,
        launcher);

    try
    {
        await AssertThrowsAsync<InvalidOperationException>(
            () => bootstrapper.EnsureInstalledAsync(null, CancellationToken.None));
        Assert(downloader.DownloadCount == 0);
        Assert(verifier.InstallerVerifyCount == 0);
        Assert(verifier.ClientVerifyCount == 0);
        Assert(launcher.LaunchCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task BootstrapReusesTrustedInstallAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var settings = new AppSettingsStore(paths);
    var locator = new MutableWireSockLocator
    {
        Path = Path.Combine(root, "WireSock", "wiresock-client.exe")
    };
    var downloader = new FakeVerifiedDownloader();
    var verifier = new FakeWireSockPackageVerifier();
    var launcher = new FakeInstallerLauncher();
    var bootstrapper = new WireSockBootstrapper(
        paths,
        settings,
        locator,
        downloader,
        verifier,
        launcher);

    try
    {
        bootstrapper.AcceptSetupConsent();
        var result = await bootstrapper.EnsureInstalledAsync(
            null,
            CancellationToken.None);

        Assert(result == locator.Path);
        Assert(downloader.DownloadCount == 0);
        Assert(verifier.InstallerVerifyCount == 0);
        Assert(verifier.ClientVerifyCount == 1);
        Assert(launcher.LaunchCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task BootstrapIgnoresUntrustedInstallAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var settings = new AppSettingsStore(paths);
    var trustedPath = Path.Combine(root, "Trusted", "wiresock-client.exe");
    var locator = new MutableWireSockLocator
    {
        Path = Path.Combine(root, "Untrusted", "wiresock-client.exe")
    };
    var downloader = new FakeVerifiedDownloader();
    var verifier = new FakeWireSockPackageVerifier(path =>
        path.Contains("Untrusted", StringComparison.OrdinalIgnoreCase));
    var launcher = new FakeInstallerLauncher(() => locator.Path = trustedPath);
    var bootstrapper = new WireSockBootstrapper(
        paths,
        settings,
        locator,
        downloader,
        verifier,
        launcher);

    try
    {
        bootstrapper.AcceptSetupConsent();
        var result = await bootstrapper.EnsureInstalledAsync(
            null,
            CancellationToken.None);

        Assert(result == trustedPath);
        Assert(downloader.DownloadCount == 1);
        Assert(verifier.InstallerVerifyCount == 1);
        Assert(verifier.ClientVerifyCount == 2);
        Assert(launcher.LaunchCount == 1);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task BootstrapLifecycleAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var settings = new AppSettingsStore(paths);
    var locator = new MutableWireSockLocator();
    var downloader = new FakeVerifiedDownloader();
    var verifier = new FakeWireSockPackageVerifier();
    var installedPath = Path.Combine(root, "WireSock", "wiresock-client.exe");
    var launcher = new FakeInstallerLauncher(() => locator.Path = installedPath);
    var bootstrapper = new WireSockBootstrapper(
        paths,
        settings,
        locator,
        downloader,
        verifier,
        launcher);

    try
    {
        bootstrapper.AcceptSetupConsent();
        var result = await bootstrapper.EnsureInstalledAsync(
            null,
            CancellationToken.None);

        Assert(result == installedPath);
        Assert(downloader.DownloadCount == 1);
        Assert(verifier.InstallerVerifyCount == 1);
        Assert(verifier.ClientVerifyCount == 1);
        Assert(launcher.LaunchCount == 1);
        Assert(!File.Exists(Path.Combine(
            paths.InstallerDirectory,
            WireSockPackage.InstallerFileName)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task BootstrapAcceptsRestartRequiredExitCodeAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var settings = new AppSettingsStore(paths);
    var locator = new MutableWireSockLocator();
    var downloader = new FakeVerifiedDownloader();
    var verifier = new FakeWireSockPackageVerifier();
    var installedPath = Path.Combine(root, "WireSock", "wiresock-client.exe");
    var launcher = new FakeInstallerLauncher(
        () => locator.Path = installedPath,
        exitCode: 3010);
    var bootstrapper = new WireSockBootstrapper(
        paths,
        settings,
        locator,
        downloader,
        verifier,
        launcher);

    try
    {
        bootstrapper.AcceptSetupConsent();
        var result = await bootstrapper.EnsureInstalledAsync(
            null,
            CancellationToken.None);

        Assert(result == installedPath);
        Assert(downloader.DownloadCount == 1);
        Assert(verifier.InstallerVerifyCount == 1);
        Assert(verifier.ClientVerifyCount == 1);
        Assert(launcher.LaunchCount == 1);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerLifecycleAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var processLauncher = new FakeProcessLauncher(process);
    var wireSockExecutable = Path.Combine(
        root,
        "WireSock VPN Client",
        "bin",
        WireSockPackage.CliExecutableFileName);
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root),
        new FakeWireSockBootstrapper(wireSockExecutable),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        processLauncher,
        TimeSpan.Zero);

    try
    {
        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(processLauncher.LastExecutable == wireSockExecutable);
        Assert(processLauncher.LastArguments.SequenceEqual(
            [
                "run",
                "-config",
                Path.Combine(root, "discord.conf"),
                "-log-level",
                "error"
            ],
            StringComparer.Ordinal));

        await controller.ConnectAsync();
        Assert(process.StopCount == 0);

        await controller.DisconnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Disconnected);
        Assert(process.StopCount == 1);

        await controller.DisconnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Disconnected);
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerBootstrapFailureAsync()
{
    var root = CreateTemporaryDirectory();
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root),
        new FakeWireSockBootstrapper(
            new InvalidOperationException(
                "WireSock VPN Client kurulamadı.")),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(new FakeManagedProcess()),
        TimeSpan.Zero);

    try
    {
        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "WireSock VPN Client",
            StringComparison.Ordinal));
        Assert(File.Exists(new AppPaths(root).ErrorLog));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static string CreateTemporaryDirectory()
{
    var path = Path.Combine(
        Path.GetTempPath(),
        "Discorder.Tests",
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

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(
        $"{typeof(TException).Name} beklenen şekilde fırlatılmadı.");
}

static async Task AssertThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(
        $"{typeof(TException).Name} beklenen şekilde fırlatılmadı.");
}

file sealed class MutableWireSockLocator : IWireSockLocator
{
    public string? Path { get; set; }

    public string? FindExecutable() => Path;
}

file sealed class FakeWireSockBootstrapper : IWireSockBootstrapper
{
    private readonly string? _path;
    private readonly Exception? _exception;

    public FakeWireSockBootstrapper(string path)
    {
        _path = path;
    }

    public FakeWireSockBootstrapper(Exception exception)
    {
        _exception = exception;
    }

    public string RequiredVersion => WireSockPackage.Version;

    public Uri ProductPage => WireSockPackage.ProductPage;

    public bool IsInstalled => _path is not null;

    public bool IsSetupConsentAccepted => true;

    public void AcceptSetupConsent()
    {
    }

    public Task<string> EnsureInstalledAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_exception is not null)
        {
            return Task.FromException<string>(_exception);
        }

        return Task.FromResult(_path!);
    }
}

file sealed class FakeVerifiedDownloader : IVerifiedDownloader
{
    public int DownloadCount { get; private set; }

    public async Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        DownloadCount++;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(
            destination,
            "dogrulanmis-kurucu",
            cancellationToken);
    }
}

file sealed class FakeWgcfCommandRunner(AppPaths paths) : ICommandRunner
{
    public List<string> Commands { get; } = [];

    public Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Commands.Add(string.Join(" ", arguments));

        if (!string.Equals(executable, paths.WgcfExecutable, StringComparison.Ordinal))
        {
            return Task.FromResult(new CommandResult(
                1,
                string.Empty,
                "beklenmeyen çalıştırılabilir dosya"));
        }

        if (arguments.SequenceEqual(["register", "--accept-tos"]))
        {
            Directory.CreateDirectory(workingDirectory);
            File.WriteAllText(paths.WgcfAccount, "account = true");
            return Task.FromResult(new CommandResult(0, "registered", string.Empty));
        }

        if (arguments.SequenceEqual(["generate"]))
        {
            Directory.CreateDirectory(workingDirectory);
            File.WriteAllText(paths.WgcfBaseProfile, """
                [Interface]
                PrivateKey = test-private-key
                Address = 172.16.0.2/32

                [Peer]
                PublicKey = test-public-key
                Endpoint = engage.cloudflareclient.com:2408
                AllowedApps = chrome.exe, roblox.exe, Update.exe
                AllowedIPs = 0.0.0.0/0
                """);
            return Task.FromResult(new CommandResult(0, "generated", string.Empty));
        }

        return Task.FromResult(new CommandResult(
            1,
            string.Empty,
            "beklenmeyen komut"));
    }
}

file sealed class FakeWireSockPackageVerifier(
    Func<string, bool>? rejectClient = null) : IWireSockPackageVerifier
{
    public int InstallerVerifyCount { get; private set; }

    public int ClientVerifyCount { get; private set; }

    public void VerifyInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
        {
            throw new InvalidOperationException("Kurucu indirilmedi.");
        }

        InstallerVerifyCount++;
    }

    public void VerifyClient(string executablePath)
    {
        ClientVerifyCount++;

        if (rejectClient?.Invoke(executablePath) == true)
        {
            throw new InvalidDataException("İstemci güven doğrulaması başarısız oldu.");
        }
    }
}

file sealed class FakeInstallerLauncher(
    Action? onLaunch = null,
    int exitCode = 0)
    : IElevatedInstallerLauncher
{
    public int LaunchCount { get; private set; }

    public Task<int> InstallAsync(
        string installerPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(installerPath))
        {
            throw new InvalidOperationException("Kurucu indirilmedi.");
        }

        LaunchCount++;
        onLaunch?.Invoke();
        return Task.FromResult(exitCode);
    }
}

file sealed class FakeProfileProvisioner(string profilePath) : IProfileProvisioner
{
    public Task<string> EnsureProfileAsync(
        IReadOnlyList<string> allowedApplications,
        CancellationToken cancellationToken)
    {
        if (allowedApplications.Count == 0)
        {
            throw new InvalidOperationException("Uygulama listesi boş geldi.");
        }

        return Task.FromResult(profilePath);
    }
}

file sealed class RecordingCommandRunner : ICommandRunner
{
    public List<string> Commands { get; } = [];

    public Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Commands.Add(string.Join(" ", arguments));
        return Task.FromResult(new CommandResult(0, string.Empty, string.Empty));
    }
}

file sealed class FakeProcessLauncher(FakeManagedProcess process) : IProcessLauncher
{
    public string? LastExecutable { get; private set; }

    public IReadOnlyList<string> LastArguments { get; private set; } = [];

    public IManagedProcess Start(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath)
    {
        LastExecutable = executable;
        LastArguments = arguments.ToArray();
        return process;
    }
}

file sealed class FakeManagedProcess : IManagedProcess
{
    public event EventHandler? Exited;

    public bool HasExited { get; private set; }

    public int? ExitCode => HasExited ? 0 : null;

    public int StopCount { get; private set; }

    public Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        StopCount++;
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
