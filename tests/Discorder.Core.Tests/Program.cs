using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using Discorder.Core.Configuration;
using Discorder.Core.Connection;
using Discorder.Core.Diagnostics;
using Discorder.Core.Discord;
using Discorder.Core.Firewall;
using Discorder.Core.Infrastructure;
using Discorder.Core.Maintenance;
using Discorder.Core.Profiles;
using Discorder.Core.Provisioning;
using Discorder.Core.Security;
using Discorder.Core.WireSock;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Discord kapsamı varsayılan olarak tarayıcıları kapalı tutar", DiscordScopeDefaultsToAppOnlyAsync),
    ("Discord web modu uygulama ve desteklenen tarayıcıları içerir", DiscordScopeIncludesBrowsersWhenEnabledAsync),
    ("Profil üretici geniş AllowedApps değerlerini değiştirir", ProfileBuilderIsStrictAsync),
    ("Profil üretici yapılandırma enjeksiyonunu reddeder", ProfileBuilderRejectsInjectionAsync),
    ("wgcf Discord uygulama ve web profili üretir", WgcfProvisionerBuildsDiscordAccessProfileAsync),
    ("wgcf boş hata çıktısını tanısız bırakmaz", WgcfProvisionerEmptyFailureIsDiagnosticAsync),
    ("SHA-256 doğrulayıcı yalnızca sabit özeti kabul eder", HashVerifierIsStrictAsync),
    ("Doğrulanmış indirici geçici zaman aşımını tekrar dener", VerifiedDownloaderRetriesTransientTimeoutAsync),
    ("Ayarlar WireSock onayını sürüm bazında saklar", SettingsPersistConsentAsync),
    ("WireSock hazırlığı onaysız kurulumu reddeder", BootstrapRequiresConsentAsync),
    ("WireSock hazırlığı güvenilir kurulumu yeniden kullanır", BootstrapReusesTrustedInstallAsync),
    ("WireSock hazırlığı güvenilmeyen kurulumu yok sayar", BootstrapIgnoresUntrustedInstallAsync),
    ("WireSock hazırlığı resmi paketi doğrulayıp kurar", BootstrapLifecycleAsync),
    ("WireSock hazırlığı yeniden başlatma gerektiren başarı kodunu kabul eder", BootstrapAcceptsRestartRequiredExitCodeAsync),
    ("Denetleyici idempotent bağlanır ve keser", ControllerLifecycleAsync),
    ("Denetleyici web kapsamını bağlıyken kilitler", ControllerLocksBrowserScopeWhileConnectedAsync),
    ("Denetleyici WireSock hazırlık hatasını bildirir", ControllerBootstrapFailureAsync),
    ("Denetleyici GitHub DNS hatasını Türkçe açıklar", ControllerNetworkFailureIsUserFriendlyAsync),
    ("Denetleyici indirme zaman aşımını Türkçe açıklar", ControllerDownloadTimeoutIsUserFriendlyAsync),
    ("Denetleyici hosts kilidi hatasını Türkçe açıklar", ControllerAccessLockFailureIsUserFriendlyAsync),
    ("Windows Firewall kilidi Discord alan adı kuralını yönetir", WindowsFirewallAccessLockBuildsExpectedCommandsAsync),
    ("Onarım ayarları ve logları koruyup üretilen veriyi yeniler", CleanupServiceRepairsGeneratedStateAsync),
    ("Temiz kaldırma Discorder verisini ve kilidini siler", CleanupServiceRemovesDiscorderStateAsync),
    ("Tanılama logları devops paketi üretir", DiagnosticsWritesDevOpsBundleAsync)
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

static Task DiscordScopeDefaultsToAppOnlyAsync()
{
    var root = CreateTemporaryDirectory();
    Directory.CreateDirectory(Path.Combine(root, "Discord"));
    Directory.CreateDirectory(Path.Combine(root, "Google", "Chrome", "Application"));

    try
    {
        var apps = new DiscordAppScope(root, root, root).GetAllowedApplications();

        Assert(apps.Any(app => app.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals(
            Path.Combine(root, "Discord"),
            StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Contains("Chrome", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Contains("roblox", StringComparison.OrdinalIgnoreCase)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static Task DiscordScopeIncludesBrowsersWhenEnabledAsync()
{
    var root = CreateTemporaryDirectory();
    Directory.CreateDirectory(Path.Combine(root, "Discord"));
    Directory.CreateDirectory(Path.Combine(
        root,
        "Google",
        "Chrome",
        "Application"));
    Directory.CreateDirectory(Path.Combine(
        root,
        "Mozilla Firefox"));

    try
    {
        var apps = new DiscordAppScope(root, root, root)
            .GetAllowedApplications(includeBrowserAccess: true);

        Assert(apps.Any(app => app.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals(
            Path.Combine(root, "Discord"),
            StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Equals(
            Path.Combine(root, "Google", "Chrome", "Application"),
            StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Equals(
            Path.Combine(root, "Mozilla Firefox"),
            StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Equals("Update.exe", StringComparison.OrdinalIgnoreCase)));
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
        [
            "Discord.exe",
            "chrome.exe",
            "msedge.exe",
            @"C:\Users\test\AppData\Local\Discord"
        ]);

    Assert(profile.Contains("AllowedApps = ", StringComparison.Ordinal));
    Assert(profile.Contains("Discord.exe", StringComparison.Ordinal));
    Assert(profile.Contains("chrome.exe", StringComparison.OrdinalIgnoreCase));
    Assert(profile.Contains("msedge.exe", StringComparison.OrdinalIgnoreCase));
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

static async Task WgcfProvisionerBuildsDiscordAccessProfileAsync()
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
            ["Discord.exe", "chrome.exe", "msedge.exe", discordDirectory],
            CancellationToken.None);

        var profile = await File.ReadAllTextAsync(profilePath);
        Assert(profilePath == paths.DiscordProfile);
        Assert(File.Exists(paths.WgcfExecutable));
        var allowedAppsLine = profile
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Single(line => line.StartsWith("AllowedApps =", StringComparison.Ordinal));
        Assert(allowedAppsLine.Contains("Discord.exe", StringComparison.Ordinal));
        Assert(allowedAppsLine.Contains("chrome.exe", StringComparison.OrdinalIgnoreCase));
        Assert(allowedAppsLine.Contains("msedge.exe", StringComparison.OrdinalIgnoreCase));
        Assert(allowedAppsLine.Contains(discordDirectory, StringComparison.OrdinalIgnoreCase));
        Assert(profile.Contains(discordDirectory, StringComparison.OrdinalIgnoreCase));
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

static async Task WgcfProvisionerEmptyFailureIsDiagnosticAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var provisioner = new WgcfProvisioner(
        paths,
        new FakeVerifiedDownloader(),
        new EmptyFailureCommandRunner(exitCode: 9));

    try
    {
        var exception = await AssertThrowsAsync<InvalidOperationException>(
            () => provisioner.EnsureProfileAsync(
                ["Discord.exe"],
                CancellationToken.None));

        Assert(exception.Message.Contains(
            "Cloudflare WARP kaydı çıkış kodu 9",
            StringComparison.Ordinal));
        Assert(exception.Message.Contains(
            "wgcf exit code 9",
            StringComparison.Ordinal));
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

static async Task VerifiedDownloaderRetriesTransientTimeoutAsync()
{
    var root = CreateTemporaryDirectory();
    var payload = Encoding.UTF8.GetBytes("dogrulanmis-kurucu");
    var expectedSha256 = Convert.ToHexString(SHA256.HashData(payload));
    var handler = new FlakyDownloadHandler(failuresBeforeSuccess: 1, payload);
    using var httpClient = new HttpClient(handler);
    var downloader = new VerifiedDownloader(
        httpClient,
        maxAttempts: 2,
        retryDelay: TimeSpan.Zero);
    var destination = Path.Combine(root, "wiresock.msi");

    try
    {
        await downloader.DownloadAsync(
            new Uri("https://downloads.example.test/wiresock.msi"),
            destination,
            expectedSha256,
            CancellationToken.None);

        Assert(handler.RequestCount == 2);
        Assert(await File.ReadAllTextAsync(destination) == "dogrulanmis-kurucu");
        Assert(!File.Exists(destination + ".download"));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task SettingsPersistConsentAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var paths = new AppPaths(root);
        var firstStore = new AppSettingsStore(paths);
        Assert(!firstStore.IsSetupConsentAccepted(WireSockPackage.Version));
        Assert(!firstStore.IsBrowserAccessEnabled());
        Assert(!firstStore.IsRunInBackgroundOnCloseEnabled());
        Assert(!firstStore.IsStartWithWindowsEnabled());
        Assert(!firstStore.IsWireSockInstalledByDiscorder());

        firstStore.SetBrowserAccessEnabled(true);
        firstStore.SetRunInBackgroundOnCloseEnabled(true);
        firstStore.SetStartWithWindowsEnabled(true);
        firstStore.SetWireSockInstalledByDiscorder(true);
        firstStore.AcceptSetupConsent(WireSockPackage.Version);

        var reloadedStore = new AppSettingsStore(paths);
        Assert(reloadedStore.IsSetupConsentAccepted(WireSockPackage.Version));
        Assert(!reloadedStore.IsSetupConsentAccepted("next-version"));
        Assert(reloadedStore.IsBrowserAccessEnabled());
        Assert(reloadedStore.IsRunInBackgroundOnCloseEnabled());
        Assert(reloadedStore.IsStartWithWindowsEnabled());
        Assert(reloadedStore.IsWireSockInstalledByDiscorder());

        reloadedStore.SetBrowserAccessEnabled(false);
        reloadedStore.SetRunInBackgroundOnCloseEnabled(false);
        reloadedStore.SetStartWithWindowsEnabled(false);
        reloadedStore.SetWireSockInstalledByDiscorder(false);
        var disabledStore = new AppSettingsStore(paths);
        Assert(!disabledStore.IsBrowserAccessEnabled());
        Assert(!disabledStore.IsRunInBackgroundOnCloseEnabled());
        Assert(!disabledStore.IsStartWithWindowsEnabled());
        Assert(!disabledStore.IsWireSockInstalledByDiscorder());

        await File.WriteAllTextAsync(paths.SettingsFile, """
            {
              "AcceptedWireSockVersion": "1.4.7.1",
              "AcceptedCloudflareWarpTerms": true
            }
            """);
        var legacyStore = new AppSettingsStore(paths);
        Assert(legacyStore.IsSetupConsentAccepted(WireSockPackage.Version));
        Assert(!legacyStore.IsBrowserAccessEnabled());
        Assert(!legacyStore.IsRunInBackgroundOnCloseEnabled());
        Assert(!legacyStore.IsStartWithWindowsEnabled());
        Assert(!legacyStore.IsWireSockInstalledByDiscorder());
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
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
        Assert(!settings.IsWireSockInstalledByDiscorder());
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
        Assert(settings.IsWireSockInstalledByDiscorder());
        Assert(File.Exists(paths.WireSockInstallMarker));
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
    var accessLock = new FakeDiscordAccessLock();
    var profileProvisioner = new FakeProfileProvisioner(
        Path.Combine(root, "discord.conf"));
    var wireSockExecutable = Path.Combine(
        root,
        "WireSock VPN Client",
        "bin",
        WireSockPackage.CliExecutableFileName);
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(wireSockExecutable),
        profileProvisioner,
        processLauncher,
        TimeSpan.Zero,
        accessLock)
    {
        IncludeBrowserAccess = true
    };

    try
    {
        await controller.EnsureDisconnectedLockAsync();
        Assert(accessLock.EnableCount == 1);
        Assert(controller.Snapshot.Message.Contains("kilitli", StringComparison.Ordinal));

        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(accessLock.DisableCount == 1);
        Assert(accessLock.ApplyTunnelScopeCount == 1);
        Assert(accessLock.LastIncludeBrowserAccess == true);
        Assert(profileProvisioner.LastAllowedApplications.Any(app =>
            app.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.Any(app =>
            app.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.Any(app =>
            app.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.All(app =>
            !app.Contains("roblox", StringComparison.OrdinalIgnoreCase)));
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
        Assert(accessLock.EnableCount == 2);
        Assert(accessLock.ClearTunnelScopeCount == 1);
        Assert(process.StopCount == 1);

        await controller.DisconnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Disconnected);
        Assert(accessLock.EnableCount == 3);
        Assert(accessLock.ClearTunnelScopeCount == 2);
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerLocksBrowserScopeWhileConnectedAsync()
{
    var root = CreateTemporaryDirectory();
    var accessLock = new FakeDiscordAccessLock();
    var profileProvisioner = new FakeProfileProvisioner(
        Path.Combine(root, "discord.conf"));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        profileProvisioner,
        new FakeProcessLauncher(new FakeManagedProcess()),
        TimeSpan.Zero,
        accessLock);

    try
    {
        Assert(controller.TrySetBrowserAccess(false));
        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(accessLock.LastIncludeBrowserAccess == false);
        Assert(profileProvisioner.LastAllowedApplications.Any(app =>
            app.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.All(app =>
            !app.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(!controller.TrySetBrowserAccess(true));
        Assert(!controller.IncludeBrowserAccess);

        await controller.DisconnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Disconnected);
        Assert(controller.TrySetBrowserAccess(true));
        Assert(controller.IncludeBrowserAccess);
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
    var accessLock = new FakeDiscordAccessLock();
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(
            new InvalidOperationException(
                "WireSock VPN Client kurulamadı.")),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(new FakeManagedProcess()),
        TimeSpan.Zero,
        accessLock);

    try
    {
        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "WireSock VPN Client",
            StringComparison.Ordinal));
        Assert(accessLock.DisableCount == 1);
        Assert(accessLock.EnableCount == 1);
        Assert(File.Exists(new AppPaths(root).ErrorLog));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerNetworkFailureIsUserFriendlyAsync()
{
    var root = CreateTemporaryDirectory();
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(
            Path.Combine(
                root,
                "WireSock VPN Client",
                "bin",
                WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(
            Path.Combine(root, "discord.conf"),
            new HttpRequestException(
                "No such host is known. (github.com:443)",
                new SocketException((int)SocketError.HostNotFound))),
        new FakeProcessLauncher(new FakeManagedProcess()),
        TimeSpan.Zero,
        accessLock);

    try
    {
        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "DNS",
            StringComparison.OrdinalIgnoreCase));
        Assert(!controller.Snapshot.Message.Contains(
            "No such host",
            StringComparison.OrdinalIgnoreCase));
        Assert(accessLock.EnableCount == 1);
        Assert(File.Exists(paths.ErrorLog));
        Assert(File.ReadAllText(paths.ErrorLog).Contains(
            "No such host",
            StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDownloadTimeoutIsUserFriendlyAsync()
{
    var root = CreateTemporaryDirectory();
    var accessLock = new FakeDiscordAccessLock();
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(
            Path.Combine(
                root,
                "WireSock VPN Client",
                "bin",
                WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(
            Path.Combine(root, "discord.conf"),
            new TaskCanceledException(
                "The operation was canceled.",
                new TimeoutException(
                    "A connection could not be established within the configured ConnectTimeout."))),
        new FakeProcessLauncher(new FakeManagedProcess()),
        TimeSpan.Zero,
        accessLock);

    try
    {
        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "zaman aşımına",
            StringComparison.OrdinalIgnoreCase));
        Assert(!controller.Snapshot.Message.Contains(
            "operation was canceled",
            StringComparison.OrdinalIgnoreCase));
        Assert(accessLock.EnableCount == 1);
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerAccessLockFailureIsUserFriendlyAsync()
{
    var root = CreateTemporaryDirectory();
    var accessLock = new FakeDiscordAccessLock(
        disableException: new InvalidOperationException(
            "Discord VPN kilidi güncellenemedi: Set-Content : Akış okunabilir değildi."));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(
            Path.Combine(
                root,
                "WireSock VPN Client",
                "bin",
                WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(new FakeManagedProcess()),
        TimeSpan.Zero,
        accessLock);

    try
    {
        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "Discord kilidi güncellenemedi",
            StringComparison.OrdinalIgnoreCase));
        Assert(controller.Snapshot.Message.Contains(
            "yönetici",
            StringComparison.OrdinalIgnoreCase));
        Assert(!controller.Snapshot.Message.Contains(
            "Set-Content",
            StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task WindowsFirewallAccessLockBuildsExpectedCommandsAsync()
{
    var root = CreateTemporaryDirectory();
    var runner = new RecordingCommandRunner();
    var accessLock = new WindowsFirewallDiscordAccessLock(
        new AppPaths(root),
        runner,
        "powershell.exe");

    try
    {
        await accessLock.EnableAsync(CancellationToken.None);
        await accessLock.DisableAsync(CancellationToken.None);
        await accessLock.ApplyTunnelScopeAsync(
            includeBrowserAccess: false,
            CancellationToken.None);
        await accessLock.ApplyTunnelScopeAsync(
            includeBrowserAccess: true,
            CancellationToken.None);
        await accessLock.ClearTunnelScopeAsync(CancellationToken.None);
        await accessLock.RemoveAsync(CancellationToken.None);

        Assert(runner.Commands.Count == 6);
        Assert(runner.Commands[0].Contains(
            "System32\\drivers\\etc\\hosts",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "# BEGIN Discorder Discord kilidi",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "0.0.0.0 ' + $domain",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "::1 ' + $domain",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "Resolve-DnsName",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "'gateway.discord.gg'",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "-RemoteAddress $addressList",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "-DisplayName $displayName",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "-Enabled True",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "[IO.File]::WriteAllText",
            StringComparison.Ordinal));
        Assert(!runner.Commands[0].Contains(
            "Set-Content -LiteralPath $hostsPath",
            StringComparison.Ordinal));
        Assert(!runner.Commands[0].Contains(
            "Add-Content -LiteralPath $hostsPath",
            StringComparison.Ordinal));
        Assert(runner.Commands[1].Contains(
            WindowsFirewallDiscordAccessLock.RuleName,
            StringComparison.Ordinal));
        Assert(runner.Commands[1].Contains(
            "# END Discorder Discord kilidi",
            StringComparison.Ordinal));
        Assert(runner.Commands[1].Contains(
            "-Enabled False",
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            WindowsFirewallDiscordAccessLock.BrowserScopeGroup,
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            "Get-DiscorderBrowserPrograms",
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            "-Program $program",
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            "-RemoteAddress $addressList",
            StringComparison.Ordinal));
        Assert(runner.Commands[3].Contains(
            "$includeBrowserAccess = $true",
            StringComparison.Ordinal));
        Assert(runner.Commands[3].Contains(
            "if ($includeBrowserAccess) { return }",
            StringComparison.Ordinal));
        Assert(runner.Commands[4].Contains(
            "Clear-DiscorderFirewallGroup",
            StringComparison.Ordinal));
        Assert(!runner.Commands[4].Contains(
            "System32\\drivers\\etc\\hosts",
            StringComparison.Ordinal));
        Assert(!runner.Commands[4].Contains(
            "# BEGIN Discorder Discord kilidi",
            StringComparison.Ordinal));
        Assert(runner.Commands[5].Contains(
            WindowsFirewallDiscordAccessLock.RuleName,
            StringComparison.Ordinal));
        Assert(runner.Commands[5].Contains(
            "# END Discorder Discord kilidi",
            StringComparison.Ordinal));
        Assert(runner.Commands[5].Contains(
            "Remove-NetFirewallRule",
            StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task CleanupServiceRemovesDiscorderStateAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var accessLock = new FakeDiscordAccessLock();
    var cleanup = new DiscorderCleanupService(paths, accessLock);

    try
    {
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.SettingsFile, "ayar");
        await File.WriteAllTextAsync(paths.DiscordProfile, "profil");
        await File.WriteAllTextAsync(paths.ErrorLog, "log");

        await cleanup.CleanUninstallAsync(CancellationToken.None);

        Assert(accessLock.RemoveCount == 1);
        Assert(!Directory.Exists(paths.DataDirectory));
        Assert(Directory.Exists(root));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task CleanupServiceRepairsGeneratedStateAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var accessLock = new FakeDiscordAccessLock();
    var cleanup = new DiscorderCleanupService(paths, accessLock);

    try
    {
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.SettingsFile, "ayar");
        await File.WriteAllTextAsync(paths.DiscordProfile, "profil");
        await File.WriteAllTextAsync(paths.WgcfExecutable, "wgcf");
        await File.WriteAllTextAsync(
            Path.Combine(paths.InstallerDirectory, "wiresock.msi"),
            "kurucu");
        await File.WriteAllTextAsync(paths.ErrorLog, "log");

        await cleanup.RepairAsync(CancellationToken.None);

        Assert(accessLock.EnableCount == 1);
        Assert(accessLock.RemoveCount == 0);
        Assert(File.Exists(paths.SettingsFile));
        Assert(Directory.Exists(paths.ProfileDirectory));
        Assert(Directory.Exists(paths.ToolsDirectory));
        Assert(Directory.Exists(paths.InstallerDirectory));
        Assert(Directory.Exists(paths.LogDirectory));
        Assert(!File.Exists(paths.DiscordProfile));
        Assert(!File.Exists(paths.WgcfExecutable));
        Assert(File.Exists(paths.ErrorLog));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task DiagnosticsWritesDevOpsBundleAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var diagnostics = new DiscorderDiagnostics(paths);

    try
    {
        diagnostics.Info(
            "test.start",
            "Tanılama başladı.",
            new Dictionary<string, string?>
            {
                ["path"] = paths.DataDirectory
            });
        diagnostics.WriteHealth(
            "test sağlıklı",
            new Dictionary<string, string?>
            {
                ["state"] = "ready"
            });

        var bundlePath = diagnostics.CreateBundle();

        Assert(File.Exists(paths.EventLog));
        Assert(File.Exists(paths.HealthReport));
        Assert(File.Exists(paths.DiagnosticSummary));
        Assert(File.Exists(bundlePath));

        var events = await File.ReadAllTextAsync(paths.EventLog);
        var health = await File.ReadAllTextAsync(paths.HealthReport);
        var summary = await File.ReadAllTextAsync(paths.DiagnosticSummary);

        Assert(events.Contains("\"source\":\"test.start\"", StringComparison.Ordinal));
        Assert(health.Contains("test sağlıklı", StringComparison.Ordinal));
        Assert(summary.Contains("Discorder tanılama özeti", StringComparison.Ordinal));
        Assert(!events.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));

        using var archive = ZipFile.OpenRead(bundlePath);
        Assert(archive.Entries.Any(entry =>
            entry.FullName.Equals("events.jsonl", StringComparison.OrdinalIgnoreCase)));
        Assert(archive.Entries.Any(entry =>
            entry.FullName.Equals("health.json", StringComparison.OrdinalIgnoreCase)));
        Assert(archive.Entries.Any(entry =>
            entry.FullName.Equals("diagnostics.md", StringComparison.OrdinalIgnoreCase)));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
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

static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException(
        $"{typeof(TException).Name} beklenen şekilde fırlatılmadı.");
}

file sealed class FakeDiscordAccessLock(Exception? disableException = null) : IDiscordAccessLock
{
    public int EnableCount { get; private set; }

    public int DisableCount { get; private set; }

    public int ApplyTunnelScopeCount { get; private set; }

    public bool? LastIncludeBrowserAccess { get; private set; }

    public int ClearTunnelScopeCount { get; private set; }

    public int RemoveCount { get; private set; }

    public Task EnableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnableCount++;
        return Task.CompletedTask;
    }

    public Task DisableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DisableCount++;
        if (disableException is not null)
        {
            return Task.FromException(disableException);
        }

        return Task.CompletedTask;
    }

    public Task ApplyTunnelScopeAsync(
        bool includeBrowserAccess,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyTunnelScopeCount++;
        LastIncludeBrowserAccess = includeBrowserAccess;
        return Task.CompletedTask;
    }

    public Task ClearTunnelScopeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClearTunnelScopeCount++;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveCount++;
        return Task.CompletedTask;
    }
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

file sealed class FlakyDownloadHandler(
    int failuresBeforeSuccess,
    byte[] payload) : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestCount++;

        if (RequestCount <= failuresBeforeSuccess)
        {
            return Task.FromException<HttpResponseMessage>(
                new TaskCanceledException(
                    "The operation was canceled.",
                    new TimeoutException(
                        "A connection could not be established within the configured ConnectTimeout.")));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });
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

file sealed class EmptyFailureCommandRunner(int exitCode) : ICommandRunner
{
    public Task<CommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandResult(
            exitCode,
            string.Empty,
            string.Empty));
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

file sealed class FakeProfileProvisioner(
    string profilePath,
    Exception? exception = null) : IProfileProvisioner
{
    public IReadOnlyList<string> LastAllowedApplications { get; private set; } = [];

    public Task<string> EnsureProfileAsync(
        IReadOnlyList<string> allowedApplications,
        CancellationToken cancellationToken)
    {
        LastAllowedApplications = allowedApplications.ToArray();

        if (allowedApplications.Count == 0)
        {
            throw new InvalidOperationException("Uygulama listesi boş geldi.");
        }

        if (exception is not null)
        {
            return Task.FromException<string>(exception);
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
