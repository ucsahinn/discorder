using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using Discorder.Core.Updates;
using Discorder.Core.WireSock;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Discord kapsamı parametresiz çağrıda sadece uygulamayı içerir", DiscordScopeDefaultsToAppOnlyAsync),
    ("Tarayıcı modu uygulama ve desteklenen tarayıcıları içerir", DiscordScopeIncludesBrowsersWhenEnabledAsync),
    ("Profil üretici geniş AllowedApps değerlerini değiştirir", ProfileBuilderIsStrictAsync),
    ("Profil üretici yapılandırma enjeksiyonunu reddeder", ProfileBuilderRejectsInjectionAsync),
    ("wgcf Discord uygulama ve web profili üretir", WgcfProvisionerBuildsDiscordAccessProfileAsync),
    ("wgcf boş hata çıktısını tanısız bırakmaz", WgcfProvisionerEmptyFailureIsDiagnosticAsync),
    ("SHA-256 doğrulayıcı yalnızca sabit özeti kabul eder", HashVerifierIsStrictAsync),
    ("Doğrulanmış indirici geçici zaman aşımını tekrar dener", VerifiedDownloaderRetriesTransientTimeoutAsync),
    ("Doğrulanmış indirici geçici DNS hatasını tekrar dener", VerifiedDownloaderRetriesTransientDnsFailureAsync),
    ("Doğrulanmış indirici duran veri akışını tekrar dener", VerifiedDownloaderRetriesStalledBodyAsync),
    ("Doğrulanmış indirici uzunluğu bilinmeyen büyük dosyayı reddeder", VerifiedDownloaderRejectsOversizedUnknownLengthAsync),
    ("Doğrulanmış indirici uzunluğu bilinmeyen indirmeyi tamamlandı bildirir", VerifiedDownloaderReportsUnknownLengthCompletionAsync),
    ("Doğrulanmış indirici eksik gelen bilinen boyutu reddeder", VerifiedDownloaderRejectsTruncatedKnownLengthAsync),
    ("WireSock kurucu indirmesi canlı ilerleme bildirir", BootstrapReportsDownloadProgressAsync),
    ("Otomatik güncelleme güncel sürümde indirme yapmaz", AppUpdateSkipsCurrentReleaseAsync),
    ("Otomatik güncelleme yeni sürümü indirmeden bildirir", AppUpdateCheckFindsReleaseWithoutDownloadAsync),
    ("Otomatik güncelleme geçici release hatasını tekrar dener", AppUpdateCheckRetriesTransientMetadataFailureAsync),
    ("Otomatik güncelleme geçici release gövde hatasını tekrar dener", AppUpdateCheckRetriesTransientMetadataBodyFailureAsync),
    ("Otomatik güncelleme geçici checksum hatasını tekrar dener", AppUpdateCheckRetriesTransientChecksumFailureAsync),
    ("Otomatik güncelleme yeni GitHub paketini hazırlar", AppUpdatePreparesVerifiedReleaseAsync),
    ("Otomatik güncelleme indirme ilerlemesini log dostu sınırlar", AppUpdateThrottlesDownloadProgressAsync),
    ("Otomatik güncelleme indirme tekrar denemesini görünür tutar", AppUpdatePreservesDownloadRetryProgressAsync),
    ("Otomatik güncelleme GitHub digest uyuşmazlığını reddeder", AppUpdateRejectsDigestMismatchAsync),
    ("Otomatik güncelleme manifest sürüm uyuşmazlığını reddeder", AppUpdateRejectsManifestVersionMismatchAsync),
    ("Otomatik güncelleme büyük checksum dosyasını reddeder", AppUpdateRejectsOversizedChecksumAsync),
    ("Otomatik güncelleme güvensiz zip yolunu reddeder", AppUpdateRejectsUnsafeArchiveEntryAsync),
    ("Otomatik güncelleme manifest dışı zip dosyasını reddeder", AppUpdateRejectsArchiveEntryOutsideManifestAsync),
    ("Otomatik güncelleme extract öncesi paket özetini yeniden doğrular", AppUpdateExtractRejectsPackageHashMismatchAsync),
    ("Otomatik güncelleme staging sürüm klasör adını doğrular", AppUpdateStagingRejectsUnsafeVersionDirectoryAsync),
    ("Otomatik güncelleme varsayılan olarak GitHub doğrulamalı paketi hazırlar", AppUpdateDefaultsToGitHubVerifiedPackageAsync),
    ("Otomatik güncelleme imza modu açılırsa tüm PE dosyalarını doğrulamaya alır", AppUpdateRequiresSignaturesForAllPortableBinariesAsync),
    ("Ayarlar WireSock onayını sürüm bazında saklar", SettingsPersistConsentAsync),
    ("Eski tarayıcı modu açık ayarı güvenli varsayılana taşınır", BrowserAccessLegacyImplicitEnabledMigratesToOptInAsync),
    ("WireSock hazırlığı onaysız kurulumu reddeder", BootstrapRequiresConsentAsync),
    ("WireSock hazırlığı güvenilir kurulumu yeniden kullanır", BootstrapReusesTrustedInstallAsync),
    ("WireSock hazırlığı güvenilmeyen kurulumu yok sayar", BootstrapIgnoresUntrustedInstallAsync),
    ("WireSock hazırlığı resmi paketi doğrulayıp kurar", BootstrapLifecycleAsync),
    ("WireSock hazırlığı doğrulanmış yerel kurucuyu indirmeden kullanır", BootstrapUsesVerifiedLocalInstallerAsync),
    ("WireSock hazırlığı yeniden başlatma gerektiren başarı kodunu kabul eder", BootstrapAcceptsRestartRequiredExitCodeAsync),
    ("Denetleyici idempotent bağlanır ve keser", ControllerLifecycleAsync),
    ("Denetleyici temiz kapanışta firewall scriptini tekrar çalıştırmaz", ControllerDisposeSkipsDisconnectedCleanupAsync),
    ("Denetleyici kilit doğrulanmadan kapanışta kilidi yeniler", ControllerDisposeRefreshesUnconfirmedDisconnectedLockAsync),
    ("Denetleyici aktif bağlantıyı kapanışta güvenle temizler", ControllerDisposeCleansActiveConnectionAsync),
    ("Denetleyici WireSock kapanışı doğrulanmazsa süreç işaretini korur", ControllerKeepsWireSockMarkerWhenExitIsUnconfirmedAsync),
    ("Denetleyici yanlış WireSock süreç işaretini temizler", ControllerDeletesMismatchedWireSockMarkerAsync),
    ("Denetleyici bağlantı kapanınca Discord'u kapatmayı dener", ControllerClosesDiscordOnDisconnectAsync),
    ("Denetleyici web kapsamını bağlıyken kilitler", ControllerLocksBrowserScopeWhileConnectedAsync),
    ("Denetleyici bağlantıyı Discord oturumu sanmadan doğrular", ControllerVerifiesTunnelWithoutClaimingDiscordSessionAsync),
    ("Windows Discord başlatma planı updater yerine uygulamayı hedefler", WindowsDiscordLaunchPlanUsesAppExecutableAsync),
    ("Windows Discord başlatma planı updater fallback kullanabilir", WindowsDiscordLaunchPlanCanUseUpdaterFallbackAsync),
    ("Denetleyici Discord kapalıyken uygulamayı açmayı dener", ControllerOpensDiscordWhenItIsClosedAsync),
    ("Denetleyici Discord otomatik yenileme başarısızlığını ayrı durum yapar", ControllerKeepsTunnelActiveWhenDiscordRestartFailsAsync),
    ("Denetleyici Discord penceresi görünmezse hazır demeyi bırakır", ControllerRequiresManualActionWhenDiscordWindowIsHiddenAsync),
    ("Denetleyici Discord updater döngüsünü bağlantıyı yenileyerek kurtarır", ControllerRecoversDiscordUpdaterLoopAsync),
    ("Denetleyici recovery sonrası Discord'u yeniden başlatmadan doğrular", ControllerRequiresManualActionWhenPostRecoveryVerificationFailsAsync),
    ("Denetleyici Discord updater recovery tamamlanamazsa manuel aksiyon durumunu korur", ControllerKeepsTunnelActiveWhenDiscordUpdaterDirectRestartFailsAsync),
    ("Denetleyici Discord updater recovery hatasında kilidi geri açar", ControllerRestoresLockWhenDiscordUpdaterRecoveryFailsAsync),
    ("Denetleyici WireSock hazırlık hatasını bildirir", ControllerBootstrapFailureAsync),
    ("Denetleyici GitHub DNS hatasını Türkçe açıklar", ControllerNetworkFailureIsUserFriendlyAsync),
    ("Denetleyici indirme zaman aşımını Türkçe açıklar", ControllerDownloadTimeoutIsUserFriendlyAsync),
    ("Denetleyici duran indirme zaman aşımını Türkçe açıklar", ControllerDirectDownloadTimeoutIsUserFriendlyAsync),
    ("Denetleyici bağlantı koruması hatasını Türkçe açıklar", ControllerAccessLockFailureIsUserFriendlyAsync),
    ("Windows Firewall koruması Discord alan adı kuralını yönetir", WindowsFirewallAccessLockBuildsExpectedCommandsAsync),
    ("Onarım ayarları ve logları koruyup üretilen veriyi yeniler", CleanupServiceRepairsGeneratedStateAsync),
    ("Uygulamayı kaldırma Discorder verisini ve korumayı siler", CleanupServiceRemovesDiscorderStateAsync),
    ("Uygulamayı kaldırma tanılama loglarını geri oluşturmaz", CleanupServiceStopsDiagnosticsAfterRemovingStateAsync),
    ("Uygulamayı kaldırma beklenmeyen veri kökünü reddeder", CleanupServiceRejectsUnexpectedDataRootAsync),
    ("Uygulamayı kaldırma salt okunur Discorder dosyalarını siler", CleanupServiceDeletesReadOnlyFilesAsync),
    ("Tanılama logları devops paketi üretir", DiagnosticsWritesDevOpsBundleAsync),
    ("Debug tanılama yalnızca açıkken ayrıntılı paket üretir", DiagnosticsWritesDebugBundleOnlyWhenEnabledAsync),
    ("Tanılama kalıcı hata loglarını redakte eder", DiagnosticsRedactsPersistentErrorLogAsync),
    ("WireSock süreç logları yazılırken redakte edilir", ProcessLauncherRedactsTunnelLogAndConfirmsExitAsync),
    ("Tanılama özeti son bilgi durumunu gecikmeli yazar", DiagnosticsFlushesDebouncedSummaryAsync)
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
    var discordAppDirectory = Path.Combine(root, "Discord", "app-1.0.9999");
    Directory.CreateDirectory(discordAppDirectory);
    File.WriteAllText(Path.Combine(discordAppDirectory, "Discord.exe"), "discord");
    Directory.CreateDirectory(Path.Combine(root, "Google", "Chrome", "Application"));

    try
    {
        var apps = new DiscordAppScope(root, root, root).GetAllowedApplications();

        Assert(apps.Any(app => app.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals(
            Path.Combine(root, "Discord"),
            StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals(
            Path.Combine(discordAppDirectory, "Discord.exe"),
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
    var discordAppDirectory = Path.Combine(root, "Discord", "app-1.0.9999");
    Directory.CreateDirectory(discordAppDirectory);
    File.WriteAllText(Path.Combine(discordAppDirectory, "Discord.exe"), "discord");
    Directory.CreateDirectory(Path.Combine(
        root,
        "Google",
        "Chrome",
        "Application"));
    File.WriteAllText(
        Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe"),
        "chrome");
    Directory.CreateDirectory(Path.Combine(
        root,
        "Chromium",
        "Application"));
    File.WriteAllText(
        Path.Combine(root, "Chromium", "Application", "chromium.exe"),
        "chromium");
    Directory.CreateDirectory(Path.Combine(
        root,
        "Mozilla Firefox"));
    File.WriteAllText(Path.Combine(root, "Mozilla Firefox", "firefox.exe"), "firefox");
    Directory.CreateDirectory(Path.Combine(root, "Opera"));
    File.WriteAllText(Path.Combine(root, "Opera", "opera.exe"), "opera");

    try
    {
        var apps = new DiscordAppScope(root, root, root)
            .GetAllowedApplications(includeBrowserAccess: true);

        Assert(apps.All(app => !app.Equals("brave.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Equals("chromium.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Equals("opera.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.All(app => !app.Equals("vivaldi.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals(
            Path.Combine(root, "Discord"),
            StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals(
            Path.Combine(discordAppDirectory, "Discord.exe"),
            StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals(
            Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe"),
            StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals(
            Path.Combine(root, "Chromium", "Application", "chromium.exe"),
            StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals(
            Path.Combine(root, "Mozilla Firefox", "firefox.exe"),
            StringComparison.OrdinalIgnoreCase)));
        Assert(apps.Any(app => app.Equals(
            Path.Combine(root, "Opera", "opera.exe"),
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
    var progressMessages = new List<string>();

    try
    {
        var discordDirectory = Path.Combine(root, "Discord");
        var profilePath = await provisioner.EnsureProfileAsync(
            ["Discord.exe", "chrome.exe", "msedge.exe", discordDirectory],
            new ImmediateProgress<string>(progressMessages.Add),
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
        Assert(progressMessages.Any(message => message.Contains(
            "Cloudflare WARP aracı",
            StringComparison.OrdinalIgnoreCase)));
        Assert(progressMessages.Any(message => message.Contains(
            "20 B / 20 B",
            StringComparison.OrdinalIgnoreCase)));
        Assert(progressMessages.Any(message => message.Contains(
            "Discord bağlantı profili hazır",
            StringComparison.OrdinalIgnoreCase)));
        Assert(downloader.LastMaxBytes == WgcfProvisioner.WindowsX64MaxBytes);

        await provisioner.EnsureProfileAsync(
            ["Discord.exe"],
            null,
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
                null,
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
    var progressEvents = new List<DownloadProgress>();

    try
    {
        await downloader.DownloadAsync(
            new Uri("https://downloads.example.test/wiresock.msi"),
            destination,
            expectedSha256,
            CancellationToken.None,
            progress: new ImmediateProgress<DownloadProgress>(
                progressEvents.Add));

        Assert(handler.RequestCount == 2);
        Assert(await File.ReadAllTextAsync(destination) == "dogrulanmis-kurucu");
        Assert(!File.Exists(destination + ".download"));
        Assert(progressEvents.Any(item =>
            item.IsRetry
            && item.Attempt == 2
            && item.MaxAttempts == 2
            && item.Message is not null
            && item.Message.Contains(
                "tekrar deneniyor",
                StringComparison.OrdinalIgnoreCase)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task VerifiedDownloaderRetriesTransientDnsFailureAsync()
{
    var root = CreateTemporaryDirectory();
    var payload = Encoding.UTF8.GetBytes("dogrulanmis-kurucu");
    var expectedSha256 = Convert.ToHexString(SHA256.HashData(payload));
    var handler = new FlakyDnsDownloadHandler(payload);
    using var httpClient = new HttpClient(handler);
    var downloader = new VerifiedDownloader(
        httpClient,
        maxAttempts: 2,
        retryDelay: TimeSpan.Zero);
    var destination = Path.Combine(root, "wiresock.msi");
    var progressEvents = new List<DownloadProgress>();

    try
    {
        await downloader.DownloadAsync(
            new Uri("https://github.com/example/wiresock.msi"),
            destination,
            expectedSha256,
            CancellationToken.None,
            progress: new ImmediateProgress<DownloadProgress>(
                progressEvents.Add));

        Assert(handler.RequestCount == 2);
        Assert(await File.ReadAllTextAsync(destination) == "dogrulanmis-kurucu");
        Assert(!File.Exists(destination + ".download"));
        Assert(progressEvents.Any(item =>
            item.IsRetry
            && item.Message is not null
            && item.Message.Contains(
                "Bağlantı kurulamadı",
                StringComparison.OrdinalIgnoreCase)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task VerifiedDownloaderRetriesStalledBodyAsync()
{
    var root = CreateTemporaryDirectory();
    var handler = new StalledDownloadHandler();
    using var httpClient = new HttpClient(handler);
    var downloader = new VerifiedDownloader(
        httpClient,
        maxAttempts: 2,
        retryDelay: TimeSpan.Zero,
        readIdleTimeout: TimeSpan.FromMilliseconds(20));
    var destination = Path.Combine(root, "wiresock.msi");
    var progressEvents = new List<DownloadProgress>();

    try
    {
        await AssertThrowsAsync<TimeoutException>(
            () => downloader.DownloadAsync(
                new Uri("https://downloads.example.test/wiresock.msi"),
                destination,
                new string('0', 64),
                CancellationToken.None,
                maxBytes: 64 * 1024,
                progress: new ImmediateProgress<DownloadProgress>(
                    progressEvents.Add)));

        Assert(handler.RequestCount == 2);
        Assert(!File.Exists(destination));
        Assert(!File.Exists(destination + ".download"));
        Assert(progressEvents.Any(item => item.IsRetry));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task VerifiedDownloaderRejectsOversizedUnknownLengthAsync()
{
    var root = CreateTemporaryDirectory();
    var payload = Encoding.UTF8.GetBytes("bu-dosya-beklenen-sinirdan-buyuk");
    var expectedSha256 = Convert.ToHexString(SHA256.HashData(payload));
    var uri = new Uri("https://downloads.example.test/wiresock.msi");
    var handler = new MapHttpMessageHandler();
    handler.AddResponse(uri, () => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new UnknownLengthContent(payload)
    });
    using var httpClient = new HttpClient(handler);
    var downloader = new VerifiedDownloader(httpClient);
    var destination = Path.Combine(root, "wiresock.msi");

    try
    {
        await AssertThrowsAsync<InvalidDataException>(
            () => downloader.DownloadAsync(
                uri,
                destination,
                expectedSha256,
                CancellationToken.None,
                maxBytes: 8));

        Assert(!File.Exists(destination));
        Assert(!File.Exists(destination + ".download"));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task VerifiedDownloaderReportsUnknownLengthCompletionAsync()
{
    var root = CreateTemporaryDirectory();
    var payload = Encoding.UTF8.GetBytes("dogrulanmis-kurucu");
    var expectedSha256 = Convert.ToHexString(SHA256.HashData(payload));
    var uri = new Uri("https://downloads.example.test/wiresock.msi");
    var handler = new MapHttpMessageHandler();
    handler.AddResponse(uri, () => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new UnknownLengthContent(payload)
    });
    using var httpClient = new HttpClient(handler);
    var downloader = new VerifiedDownloader(httpClient);
    var destination = Path.Combine(root, "wiresock.msi");
    var progressEvents = new List<DownloadProgress>();

    try
    {
        await downloader.DownloadAsync(
            uri,
            destination,
            expectedSha256,
            CancellationToken.None,
            maxBytes: 64 * 1024,
            progress: new ImmediateProgress<DownloadProgress>(
                progressEvents.Add));

        var finalProgress = progressEvents.Last();
        Assert(finalProgress.BytesReceived == payload.Length);
        Assert(finalProgress.TotalBytes == payload.Length);
        Assert(finalProgress.Percent == 100);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task VerifiedDownloaderRejectsTruncatedKnownLengthAsync()
{
    var root = CreateTemporaryDirectory();
    var payload = Encoding.UTF8.GetBytes("eksik");
    var expectedSha256 = Convert.ToHexString(SHA256.HashData(payload));
    var uri = new Uri("https://downloads.example.test/wiresock.msi");
    var handler = new MapHttpMessageHandler();
    handler.AddResponse(uri, () => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new DeclaredLengthContent(payload, declaredLength: payload.Length + 10)
    });
    using var httpClient = new HttpClient(handler);
    var downloader = new VerifiedDownloader(httpClient);
    var destination = Path.Combine(root, "wiresock.msi");

    try
    {
        var exception = await AssertThrowsAsync<InvalidDataException>(
            () => downloader.DownloadAsync(
                uri,
                destination,
                expectedSha256,
                CancellationToken.None,
                maxBytes: 64 * 1024));

        Assert(exception.Message.Contains(
            "beklenen boyuta",
            StringComparison.OrdinalIgnoreCase));
        Assert(!File.Exists(destination));
        Assert(!File.Exists(destination + ".download"));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateSkipsCurrentReleaseAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var handler = new MapHttpMessageHandler();
        handler.AddJson(latestUri, """
            {
              "tag_name": "v2.0.12",
              "html_url": "https://github.com/ucsahinn/discorder/releases/tag/v2.0.12",
              "assets": []
            }
            """);
        using var httpClient = new HttpClient(handler);
        var downloader = new CapturingVerifiedDownloader([]);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri);

        var update = await service.PrepareLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            root,
            "Discorder.exe",
            CancellationToken.None);

        Assert(update.Status == AppUpdatePreparationStatus.UpToDate);
        Assert(downloader.DownloadCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateCheckFindsReleaseWithoutDownloadAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Discorder-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var downloader = new CapturingVerifiedDownloader(packageBytes);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri);

        var check = await service.CheckLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            CancellationToken.None);

        Assert(check.Status == AppUpdateCheckStatus.UpdateAvailable);
        Assert(check.LatestVersion == new Version(2, 0, 14, 0));
        Assert(check.PackageUri == zipUri);
        Assert(check.ExpectedSha256 == expectedSha256);
        Assert(downloader.DownloadCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateCheckRetriesTransientMetadataFailureAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        var attempts = 0;
        handler.AddResponse(
            latestUri,
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        CreateReleaseJson(
                            "2.0.14",
                            zipUri,
                            checksumUri,
                            expectedSha256,
                            packageBytes.Length),
                        Encoding.UTF8,
                        "application/json")
                };
            });
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Discorder-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var downloader = new CapturingVerifiedDownloader(packageBytes);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri,
            requireUpdateAuthenticode: false);

        var check = await service.CheckLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            CancellationToken.None);

        Assert(check.Status == AppUpdateCheckStatus.UpdateAvailable);
        Assert(attempts == 2);
        Assert(downloader.DownloadCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateCheckRetriesTransientMetadataBodyFailureAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        var attempts = 0;
        handler.AddResponse(
            latestUri,
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new FailingHttpContent(new IOException("temporary body failure"))
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        CreateReleaseJson(
                            "2.0.14",
                            zipUri,
                            checksumUri,
                            expectedSha256,
                            packageBytes.Length),
                        Encoding.UTF8,
                        "application/json")
                };
            });
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Discorder-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var downloader = new CapturingVerifiedDownloader(packageBytes);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri,
            requireUpdateAuthenticode: false);

        var check = await service.CheckLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            CancellationToken.None);

        Assert(check.Status == AppUpdateCheckStatus.UpdateAvailable);
        Assert(attempts == 2);
        Assert(downloader.DownloadCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateCheckRetriesTransientChecksumFailureAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        var checksumAttempts = 0;
        handler.AddResponse(
            checksumUri,
            () =>
            {
                checksumAttempts++;
                if (checksumAttempts == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{expectedSha256}  Discorder-2.0.14-win-x64.zip",
                        Encoding.UTF8,
                        "text/plain")
                };
            });
        using var httpClient = new HttpClient(handler);
        var downloader = new CapturingVerifiedDownloader(packageBytes);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri,
            requireUpdateAuthenticode: false);

        var check = await service.CheckLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            CancellationToken.None);

        Assert(check.Status == AppUpdateCheckStatus.UpdateAvailable);
        Assert(checksumAttempts == 2);
        Assert(downloader.DownloadCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdatePreparesVerifiedReleaseAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Discorder-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var downloader = new CapturingVerifiedDownloader(packageBytes);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri,
            requireUpdateAuthenticode: false);
        await WriteUpdaterHelperAsync(root);
        var progressEvents = new List<AppUpdateProgress>();
        var progress = new ImmediateProgress<AppUpdateProgress>(
            progressEvents.Add);

        var check = await service.CheckLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            CancellationToken.None);
        var update = await service.PrepareCheckedUpdateAsync(
            check,
            root,
            "Discorder.exe",
            CancellationToken.None,
            progress);

        Assert(update.Status == AppUpdatePreparationStatus.Prepared);
        Assert(downloader.DownloadCount == 1);
        Assert(downloader.LastSource == zipUri);
        Assert(downloader.LastExpectedSha256 == expectedSha256);
        Assert(downloader.LastMaxBytes == packageBytes.Length);
        Assert(File.Exists(update.PackagePath));
        Assert(File.Exists(Path.Combine(update.PayloadDirectory!, "Discorder.exe")));
        Assert(File.Exists(Path.Combine(
            update.PayloadDirectory!,
            UpdatePackageValidator.ManifestFileName)));
        Assert(File.Exists(update.ApplicatorPath));
        Assert(Path.GetFileName(update.ApplicatorPath!) == "Discorder.Updater.exe");
        Assert(File.Exists(Path.Combine(
            Path.GetDirectoryName(update.ApplicatorPath!)!,
            "runtimes",
            "win",
            "native",
            "helper.dll")));
        Assert(update.ExpectedSignerThumbprint is null);
        Assert(progressEvents.Any(item => item.Message.Contains(
            "indiriliyor",
            StringComparison.OrdinalIgnoreCase)));
        Assert(progressEvents.Any(item => item.Percent >= 90));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateThrottlesDownloadProgressAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Discorder-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var downloader = new FloodingVerifiedDownloader(packageBytes, reports: 5000);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri,
            requireUpdateAuthenticode: false);
        await WriteUpdaterHelperAsync(root);
        var progressEvents = new List<AppUpdateProgress>();
        var progress = new ImmediateProgress<AppUpdateProgress>(
            progressEvents.Add);

        var check = await service.CheckLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            CancellationToken.None);
        var update = await service.PrepareCheckedUpdateAsync(
            check,
            root,
            "Discorder.exe",
            CancellationToken.None,
            progress);

        Assert(update.Status == AppUpdatePreparationStatus.Prepared);
        Assert(downloader.ReportCount == 5000);
        Assert(progressEvents.Count < 80);
        Assert(progressEvents.Any(item => item.Message.Contains(
            "indiriliyor",
            StringComparison.OrdinalIgnoreCase)));
        Assert(progressEvents.Any(item => item.Percent >= 90));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdatePreservesDownloadRetryProgressAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Discorder-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var downloader = new RetryProgressVerifiedDownloader(packageBytes);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri,
            requireUpdateAuthenticode: false);
        await WriteUpdaterHelperAsync(root);
        var progressEvents = new List<AppUpdateProgress>();
        var progress = new ImmediateProgress<AppUpdateProgress>(
            progressEvents.Add);

        var check = await service.CheckLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            CancellationToken.None);
        var update = await service.PrepareCheckedUpdateAsync(
            check,
            root,
            "Discorder.exe",
            CancellationToken.None,
            progress);

        Assert(update.Status == AppUpdatePreparationStatus.Prepared);
        Assert(progressEvents.Any(item => item.Message.Contains(
            "tekrar",
            StringComparison.OrdinalIgnoreCase)));
        Assert(progressEvents.Any(item => item.Detail?.Contains(
            "Deneme 2/2",
            StringComparison.OrdinalIgnoreCase) == true));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateRejectsDigestMismatchAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson(
                "2.0.14",
                zipUri,
                checksumUri,
                new string('A', 64),
                packageBytes.Length));
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Discorder-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            new CapturingVerifiedDownloader(packageBytes),
            latestUri,
            requireUpdateAuthenticode: false);

        await AssertThrowsAsync<InvalidDataException>(
            () => service.CheckLatestUpdateAsync(
                new Version(2, 0, 12, 0),
                CancellationToken.None));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateDefaultsToGitHubVerifiedPackageAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Discorder-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var downloader = new CapturingVerifiedDownloader(packageBytes);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            downloader,
            latestUri);
        await WriteUpdaterHelperAsync(root);

        var update = await service.PrepareLatestUpdateAsync(
            new Version(2, 0, 12, 0),
            root,
            "Discorder.exe",
            CancellationToken.None);

        Assert(update.Status == AppUpdatePreparationStatus.Prepared);
        Assert(update.ExpectedSignerThumbprint is null);
        Assert(downloader.LastExpectedSha256 == expectedSha256);
        Assert(File.Exists(update.PackagePath));
        Assert(File.Exists(Path.Combine(update.PayloadDirectory!, "Discorder.exe")));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateRejectsManifestVersionMismatchAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage(version: "2.0.15");
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        handler.AddText(
            checksumUri,
            $"{expectedSha256}  Discorder-2.0.14-win-x64.zip");
        using var httpClient = new HttpClient(handler);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            new CapturingVerifiedDownloader(packageBytes),
            latestUri,
            requireUpdateAuthenticode: false);
        await WriteUpdaterHelperAsync(root);

        await AssertThrowsAsync<InvalidDataException>(
            () => service.PrepareLatestUpdateAsync(
                new Version(2, 0, 12, 0),
                root,
                "Discorder.exe",
                CancellationToken.None));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task AppUpdateRejectsOversizedChecksumAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var latestUri = new Uri("https://updates.example.test/releases/latest");
        var zipUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.zip");
        var checksumUri = new Uri("https://github.com/ucsahinn/discorder/releases/download/v2.0.14/Discorder-2.0.14-win-x64.sha256.txt");
        var packageBytes = CreateUpdatePackage();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var handler = new MapHttpMessageHandler();
        handler.AddJson(
            latestUri,
            CreateReleaseJson("2.0.14", zipUri, checksumUri, expectedSha256, packageBytes.Length));
        handler.AddText(
            checksumUri,
            new string('A', 8193));
        using var httpClient = new HttpClient(handler);
        var service = new AppUpdateService(
            httpClient,
            new AppPaths(root),
            new CapturingVerifiedDownloader(packageBytes),
            latestUri,
            requireUpdateAuthenticode: false);

        await AssertThrowsAsync<InvalidDataException>(
            () => service.CheckLatestUpdateAsync(
                new Version(2, 0, 12, 0),
                CancellationToken.None));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task AppUpdateRejectsUnsafeArchiveEntryAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var path = Path.Combine(root, "unsafe.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            archive.CreateEntry("Discorder.exe");
            archive.CreateEntry("../outside.txt");
        }

        AssertThrows<InvalidDataException>(() =>
            UpdatePackageValidator.ValidateArchive(path, "Discorder.exe"));
        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task AppUpdateRejectsArchiveEntryOutsideManifestAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var path = Path.Combine(root, "extra.zip");
        File.WriteAllBytes(path, CreateUpdatePackage(extraEntryName: "unexpected.txt"));

        AssertThrows<InvalidDataException>(() =>
            UpdatePackageValidator.ValidateArchive(
                path,
                "Discorder.exe",
                expectedVersion: "2.0.14"));
        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task AppUpdateExtractRejectsPackageHashMismatchAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        var packagePath = Path.Combine(root, "Discorder-2.0.14-win-x64.zip");
        File.WriteAllBytes(packagePath, CreateUpdatePackage());
        var destination = Path.Combine(root, "payload");

        AssertThrows<InvalidDataException>(
            () => UpdatePackageValidator.ExtractToDirectory(
                packagePath,
                destination,
                "Discorder.exe",
                "2.0.14",
                expectedSha256: new string('0', 64)));

        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task AppUpdateStagingRejectsUnsafeVersionDirectoryAsync()
{
    var root = CreateTemporaryDirectory();
    try
    {
        AssertThrows<ArgumentException>(
            () => ProtectedUpdateStaging.CreateVersionDirectory(
                Path.Combine(root, "updates"),
                "..\\2.0.14",
                restrictAccess: false));

        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static Task AppUpdateRequiresSignaturesForAllPortableBinariesAsync()
{
    Assert(AuthenticodeSignatureVerifier.ShouldVerify("Discorder.exe"));
    Assert(AuthenticodeSignatureVerifier.ShouldVerify("Discorder.Updater.dll"));
    Assert(AuthenticodeSignatureVerifier.ShouldVerify("third-party/native-helper.exe"));
    Assert(AuthenticodeSignatureVerifier.ShouldVerify("third-party/native-helper.dll"));
    Assert(!AuthenticodeSignatureVerifier.ShouldVerify("discorder.update-manifest.json"));
    Assert(!AuthenticodeSignatureVerifier.ShouldVerify("README.md"));
    return Task.CompletedTask;
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
        Assert(!firstStore.IsDebugDiagnosticsEnabled());
        Assert(!firstStore.IsWireSockInstalledByDiscorder());

        firstStore.SetBrowserAccessEnabled(true);
        firstStore.SetRunInBackgroundOnCloseEnabled(true);
        firstStore.SetStartWithWindowsEnabled(true);
        firstStore.SetDebugDiagnosticsEnabled(true);
        firstStore.SetWireSockInstalledByDiscorder(true);
        firstStore.AcceptSetupConsent(WireSockPackage.Version);

        var reloadedStore = new AppSettingsStore(paths);
        Assert(reloadedStore.IsSetupConsentAccepted(WireSockPackage.Version));
        Assert(!reloadedStore.IsSetupConsentAccepted("next-version"));
        Assert(reloadedStore.IsBrowserAccessEnabled());
        Assert(reloadedStore.IsRunInBackgroundOnCloseEnabled());
        Assert(reloadedStore.IsStartWithWindowsEnabled());
        Assert(reloadedStore.IsDebugDiagnosticsEnabled());
        Assert(reloadedStore.IsWireSockInstalledByDiscorder());

        reloadedStore.SetBrowserAccessEnabled(false);
        reloadedStore.SetRunInBackgroundOnCloseEnabled(false);
        reloadedStore.SetStartWithWindowsEnabled(false);
        reloadedStore.SetDebugDiagnosticsEnabled(false);
        reloadedStore.SetWireSockInstalledByDiscorder(false);
        var disabledStore = new AppSettingsStore(paths);
        Assert(!disabledStore.IsBrowserAccessEnabled());
        Assert(!disabledStore.IsRunInBackgroundOnCloseEnabled());
        Assert(!disabledStore.IsStartWithWindowsEnabled());
        Assert(!disabledStore.IsDebugDiagnosticsEnabled());
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
        Assert(!legacyStore.IsDebugDiagnosticsEnabled());
        Assert(!legacyStore.IsWireSockInstalledByDiscorder());
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task BrowserAccessLegacyImplicitEnabledMigratesToOptInAsync()
{
    var root = CreateTemporaryDirectory();

    try
    {
        var paths = new AppPaths(root);
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.SettingsFile, """
            {
              "AcceptedWireSockVersion": "1.4.7.1",
              "AcceptedCloudflareWarpTerms": true,
              "BrowserAccessEnabled": true,
              "RunInBackgroundOnClose": true,
              "StartWithWindows": true
            }
            """);

        var legacyStore = new AppSettingsStore(paths);
        Assert(legacyStore.EnsureBrowserAccessPreferenceInitialized());
        Assert(!legacyStore.IsBrowserAccessEnabled());
        Assert(legacyStore.IsRunInBackgroundOnCloseEnabled());
        Assert(legacyStore.IsStartWithWindowsEnabled());

        var migratedStore = new AppSettingsStore(paths);
        Assert(!migratedStore.EnsureBrowserAccessPreferenceInitialized());
        Assert(!migratedStore.IsBrowserAccessEnabled());

        migratedStore.SetBrowserAccessEnabled(true);
        var explicitStore = new AppSettingsStore(paths);
        Assert(!explicitStore.EnsureBrowserAccessPreferenceInitialized());
        Assert(explicitStore.IsBrowserAccessEnabled());
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
        Assert(verifier.InstallerVerifyCount == 2);
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
        Assert(downloader.LastMaxBytes == WireSockPackage.WindowsX64MaxBytes);
        Assert(verifier.InstallerVerifyCount == 2);
        Assert(verifier.ClientVerifyCount == 1);
        Assert(launcher.LaunchCount == 1);
        var launchedInstallerPath = launcher.LastInstallerPath
            ?? throw new InvalidOperationException("Kurucu yolu kaydedilmedi.");
        Assert(Path.GetFullPath(launchedInstallerPath).StartsWith(
            Path.GetFullPath(paths.WireSockInstallerStagingDirectory),
            StringComparison.OrdinalIgnoreCase));
        Assert(!File.Exists(launchedInstallerPath));
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

static async Task BootstrapUsesVerifiedLocalInstallerAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var settings = new AppSettingsStore(paths);
    var locator = new MutableWireSockLocator();
    var downloader = new FakeVerifiedDownloader();
    var verifier = new FakeWireSockPackageVerifier();
    var installedPath = Path.Combine(root, "WireSock", "wiresock-client.exe");
    var launcher = new FakeInstallerLauncher(() => locator.Path = installedPath);
    var localInstallerPath = Path.Combine(
        paths.InstallerDirectory,
        WireSockPackage.InstallerFileName);
    var hashVerifyCount = 0;
    var bootstrapper = new WireSockBootstrapper(
        paths,
        settings,
        locator,
        downloader,
        verifier,
        launcher,
        (path, expectedSha256, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            hashVerifyCount++;
            Assert(expectedSha256 == WireSockPackage.WindowsX64Sha256);
            return Task.FromResult(string.Equals(
                Path.GetFullPath(path),
                Path.GetFullPath(localInstallerPath),
                StringComparison.OrdinalIgnoreCase));
        });
    var progressMessages = new List<string>();

    try
    {
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(localInstallerPath, "yerel-kurucu");

        bootstrapper.AcceptSetupConsent();
        var result = await bootstrapper.EnsureInstalledAsync(
            new ImmediateProgress<string>(progressMessages.Add),
            CancellationToken.None);

        Assert(result == installedPath);
        Assert(downloader.DownloadCount == 0);
        Assert(hashVerifyCount >= 1);
        Assert(verifier.InstallerVerifyCount == 3);
        Assert(verifier.ClientVerifyCount == 1);
        Assert(launcher.LaunchCount == 1);
        Assert(File.Exists(localInstallerPath));
        var launchedInstallerPath = launcher.LastInstallerPath
            ?? throw new InvalidOperationException("Kurucu yolu kaydedilmedi.");
        Assert(Path.GetFullPath(launchedInstallerPath).StartsWith(
            Path.GetFullPath(paths.WireSockInstallerStagingDirectory),
            StringComparison.OrdinalIgnoreCase));
        Assert(!File.Exists(launchedInstallerPath));
        Assert(progressMessages.Any(message => message.Contains(
            "yerel paketten",
            StringComparison.OrdinalIgnoreCase)));
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
        Assert(verifier.InstallerVerifyCount == 2);
        Assert(verifier.ClientVerifyCount == 1);
        Assert(launcher.LaunchCount == 1);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task BootstrapReportsDownloadProgressAsync()
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
    var progressMessages = new List<string>();

    try
    {
        bootstrapper.AcceptSetupConsent();
        await bootstrapper.EnsureInstalledAsync(
            new ImmediateProgress<string>(progressMessages.Add),
            CancellationToken.None);

        Assert(progressMessages.Any(message => message.Contains(
            "WireSock kurucusu",
            StringComparison.OrdinalIgnoreCase)));
        Assert(progressMessages.Any(message => message.Contains(
            "20 B / 20 B",
            StringComparison.OrdinalIgnoreCase)));
        Assert(downloader.LastMaxBytes == WireSockPackage.WindowsX64MaxBytes);
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
    var chromePath = Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
    var edgePath = Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
    var pathChromePath = Path.Combine(root, "PathBrowsers", "chrome.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(chromePath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(edgePath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(pathChromePath)!);
    File.WriteAllText(chromePath, "chrome");
    File.WriteAllText(edgePath, "edge");
    File.WriteAllText(pathChromePath, "chrome");
    var originalPath = Environment.GetEnvironmentVariable("PATH");
    Environment.SetEnvironmentVariable(
        "PATH",
        Path.GetDirectoryName(pathChromePath) + Path.PathSeparator + originalPath);
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
        Assert(controller.Snapshot.Message.Contains("Bağlı Değil", StringComparison.Ordinal));

        await controller.ConnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(accessLock.DisableCount == 1);
        Assert(accessLock.ApplyTunnelScopeCount == 1);
        Assert(accessLock.LastIncludeBrowserAccess == true);
        Assert(profileProvisioner.LastAllowedApplications.Any(app =>
            app.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.Any(app =>
            app.Equals(chromePath, StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.Any(app =>
            app.Equals(edgePath, StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.Any(app =>
            app.Equals(pathChromePath, StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.All(app =>
            !app.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)));
        Assert(profileProvisioner.LastAllowedApplications.All(app =>
            !app.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase)));
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
        Environment.SetEnvironmentVariable("PATH", originalPath);
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDisposeSkipsDisconnectedCleanupAsync()
{
    var root = CreateTemporaryDirectory();
    var accessLock = new FakeDiscordAccessLock();
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
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
        await controller.EnsureDisconnectedLockAsync();
        await controller.DisposeAsync();

        Assert(accessLock.EnableCount == 1);
        Assert(accessLock.ClearTunnelScopeCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDisposeRefreshesUnconfirmedDisconnectedLockAsync()
{
    var root = CreateTemporaryDirectory();
    var accessLock = new FakeDiscordAccessLock();
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
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
        await controller.DisposeAsync();

        Assert(accessLock.EnableCount == 1);
        Assert(accessLock.ClearTunnelScopeCount == 0);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDisposeCleansActiveConnectionAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock);

    try
    {
        await controller.ConnectAsync();
        await controller.DisposeAsync();

        Assert(process.StopCount == 1);
        Assert(accessLock.ClearTunnelScopeCount == 1);
        Assert(accessLock.EnableCount == 1);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerKeepsWireSockMarkerWhenExitIsUnconfirmedAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess(
        exitOnStop: false,
        exitOnDispose: false);
    var accessLock = new FakeDiscordAccessLock();
    var paths = new AppPaths(root);
    var diagnostics = new DiscorderDiagnostics(
        paths,
        TimeSpan.Zero);
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        diagnostics);

    try
    {
        await controller.ConnectAsync();
        Assert(File.Exists(paths.WireSockProcessMarker));

        await controller.DisposeAsync();

        Assert(File.Exists(paths.WireSockProcessMarker));
        var events = await File.ReadAllTextAsync(paths.EventLog);
        Assert(events.Contains(
            "WireSock kapanışı doğrulanamadı",
            StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerDeletesMismatchedWireSockMarkerAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var accessLock = new FakeDiscordAccessLock();
    var controller = new DiscordTunnelController(
        paths,
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(new FakeManagedProcess()),
        TimeSpan.Zero,
        accessLock,
        new DiscorderDiagnostics(paths, TimeSpan.Zero));

    try
    {
        paths.EnsureDirectories();
        using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        var marker = new
        {
            ProcessId = currentProcess.Id,
            StartTime = new DateTimeOffset(currentProcess.StartTime),
            ProfilePath = paths.DiscordProfile
        };
        await File.WriteAllTextAsync(
            paths.WireSockProcessMarker,
            JsonSerializer.Serialize(marker));

        await controller.EnsureDisconnectedLockAsync();

        Assert(!File.Exists(paths.WireSockProcessMarker));
        Assert(accessLock.EnableCount == 1);
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerClosesDiscordOnDisconnectAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new DiscorderDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager);

    try
    {
        await controller.ConnectAsync();
        await controller.DisconnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Disconnected);
        Assert(process.StopCount == 1);
        Assert(discordManager.RestartCount == 1);
        Assert(discordManager.CloseCount == 1);

        var details = controller.CreateDiagnosticDetails();
        Assert(details["discordRestartStatus"] == "Discord kapatıldı.");
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

static async Task ControllerVerifiesTunnelWithoutClaimingDiscordSessionAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new DiscorderDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            2,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100, 101]));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(controller.Snapshot.Message.Contains(
            "Discord yenilendi",
            StringComparison.Ordinal));
        Assert(!controller.Snapshot.Message.Contains(
            "Discord uygulaması bağlı",
            StringComparison.OrdinalIgnoreCase));

        var health = await File.ReadAllTextAsync(
            Path.Combine(root, "Discorder", "logs", "health.json"));
        Assert(health.Contains("\"status\":\"bağlantı hazır\"", StringComparison.Ordinal));
        Assert(health.Contains("\"discordProcessCount\":\"2\"", StringComparison.Ordinal));
        Assert(health.Contains("\"discordRestartStatus\":\"Discord yenilendi.\"", StringComparison.Ordinal));
        var details = controller.CreateDiagnosticDetails();
        Assert(details["wireSockRunning"] == "True");
        Assert(details["discordProcessCount"] == "2");
        Assert(details["nextAction"] == "Discord yenilendi. Bağlantı hazır.");
        Assert(details["discordRestartStatus"] == "Discord yenilendi.");
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static Task WindowsDiscordLaunchPlanUsesAppExecutableAsync()
{
    var root = Path.Combine(
        CreateTemporaryDirectory(),
        "Discord");
    var executablePath = Path.Combine(
        root,
        "app-1.0.9999",
        "Discord.exe");

    try
    {
        var plan = WindowsDiscordProcessInspector.CreateLaunchPlanForTesting(
            [executablePath]);

        Assert(plan.Count == 1);
        Assert(plan[0].Contains(
            Path.Combine("app-1.0.9999", "Discord.exe"),
            StringComparison.OrdinalIgnoreCase));
        Assert(!plan[0].Contains(
            "Update.exe",
            StringComparison.OrdinalIgnoreCase));
        Assert(!plan[0].Contains(
            "--processStart",
            StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }
    finally
    {
        var testRoot = Directory.GetParent(root)?.FullName;
        if (!string.IsNullOrWhiteSpace(testRoot)
            && Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }
}

static Task WindowsDiscordLaunchPlanCanUseUpdaterFallbackAsync()
{
    var root = Path.Combine(
        CreateTemporaryDirectory(),
        "Discord");

    try
    {
        var plan = WindowsDiscordProcessInspector.CreateUpdaterLaunchPlanForTesting(
            root,
            "Discord.exe",
            "Discord");

        Assert(plan.Count == 1);
        Assert(plan[0].Contains(
            "Update.exe",
            StringComparison.OrdinalIgnoreCase));
        Assert(plan[0].Contains(
            "--processStart Discord.exe",
            StringComparison.OrdinalIgnoreCase));
        Assert(!plan[0].Contains(
            Path.Combine("app-1.0.9999", "Discord.exe"),
            StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }
    finally
    {
        var testRoot = Directory.GetParent(root)?.FullName;
        if (!string.IsNullOrWhiteSpace(testRoot)
            && Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }
}

static async Task ControllerOpensDiscordWhenItIsClosedAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new DiscorderDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(0, []),
        new DiscordRestartResult(true, "Discord açıldı."));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(controller.Snapshot.Message == "Discord açıldı. Bağlantı hazır");
        Assert(discordManager.RestartCount == 1);

        var details = controller.CreateDiagnosticDetails();
        Assert(details["discordRestartStatus"] == "Discord açıldı.");
        Assert(details["nextAction"] == "Discord açıldı. Bağlantı hazır.");
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerKeepsTunnelActiveWhenDiscordRestartFailsAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new DiscorderDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]),
        new DiscordRestartResult(
            false,
            "Discord otomatik yenilenemedi.",
            "access denied"));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.DiscordRestartRequired);
        Assert(controller.Snapshot.IsConnected);
        Assert(controller.Snapshot.Message.Contains(
            "Discord otomatik yenilenemedi.",
            StringComparison.Ordinal));
        Assert(discordManager.RestartCount == 1);

        var health = await File.ReadAllTextAsync(
            Path.Combine(root, "Discorder", "logs", "health.json"));
        Assert(health.Contains("\"status\":\"discord yeniden başlatılmalı\"", StringComparison.Ordinal));
        Assert(health.Contains("\"discordRestartStatus\":\"Discord otomatik yenilenemedi.\"", StringComparison.Ordinal));

        await controller.DisconnectAsync();
        Assert(controller.Snapshot.State == TunnelState.Disconnected);
        Assert(process.StopCount == 1);
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerRequiresManualActionWhenDiscordWindowIsHiddenAsync()
{
    var root = CreateTemporaryDirectory();
    var process = new FakeManagedProcess();
    var diagnostics = new DiscorderDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var discordManager = new FakeDiscordProcessManager(
        new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]),
        new DiscordRestartResult(
            false,
            "Discord açıldı ama pencere görünmedi. Görev çubuğundan Discord'u açın.",
            "No visible Discord main window was detected."));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        new FakeProcessLauncher(process),
        TimeSpan.Zero,
        diagnostics: diagnostics,
        discordProcessManager: discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.DiscordRestartRequired);
        Assert(controller.Snapshot.Message.Contains(
            "pencere görünmedi",
            StringComparison.OrdinalIgnoreCase));
        Assert(discordManager.RestartCount == 1);

        var health = await File.ReadAllTextAsync(
            Path.Combine(root, "Discorder", "logs", "health.json"));
        Assert(health.Contains("Discord açıldı ama pencere görünmedi", StringComparison.Ordinal));
        Assert(health.Contains("\"status\":\"discord yeniden başlatılmalı\"", StringComparison.Ordinal));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerRecoversDiscordUpdaterLoopAsync()
{
    var root = CreateTemporaryDirectory();
    var firstProcess = new FakeManagedProcess();
    var secondProcess = new FakeManagedProcess();
    var processLauncher = new SequencedProcessLauncher(firstProcess, secondProcess);
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new DiscorderDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var snapshot = new DiscordProcessSnapshot(
        1,
        [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
        [100]);
    var discordManager = new SequencedDiscordProcessManager(
        snapshot,
        new DiscordRestartResult(
            false,
            "Discord güncelleme ekranında kaldı. Bağlantı kısa süre yenileniyor.",
            "updater window",
            DiscordRestartFailureKind.UpdaterWindow),
        new DiscordRestartResult(true, "Discord açıldı."));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        processLauncher,
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Connected);
        Assert(controller.Snapshot.Message.Contains(
            "Discord yenilendi",
            StringComparison.Ordinal));
        Assert(processLauncher.StartCount == 2);
        Assert(firstProcess.HasExited);
        Assert(!secondProcess.HasExited);
        Assert(accessLock.ClearTunnelScopeCount == 1);
        Assert(accessLock.ApplyTunnelScopeCount == 2);
        Assert(discordManager.RestartCount == 2);

        var events = await File.ReadAllTextAsync(
            Path.Combine(root, "Discorder", "logs", "events.jsonl"));
        Assert(events.Contains("controller.discordUpdaterRecovery", StringComparison.Ordinal));

        var details = controller.CreateDiagnosticDetails();
        Assert(details["discordRestartStatus"] == "Discord güncellendi.");
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerRequiresManualActionWhenPostRecoveryVerificationFailsAsync()
{
    var cases = new[]
    {
        new DiscordRestartResult(
            false,
            "Discord kapanmış. Discord'u açıp tekrar deneyin.",
            "No trusted Discord process was running after tunnel resume.",
            DiscordRestartFailureKind.Unknown),
        new DiscordRestartResult(
            false,
            "Discord güncellendi ama ana pencere hazır olmadı. Discord'u kapatıp tekrar deneyin.",
            "Only a trusted Discord updater window was detected after tunnel resume.",
            DiscordRestartFailureKind.UpdaterWindow),
        new DiscordRestartResult(
            false,
            "Discord açıldı ama pencere görünmedi. Görev çubuğundan Discord'u açın.",
            "No trusted visible Discord main window was detected after tunnel resume.",
            DiscordRestartFailureKind.Unknown),
        new DiscordRestartResult(
            false,
            "Discord yolu doğrulanamadı.",
            "No trusted Discord executable path was available after tunnel resume.",
            DiscordRestartFailureKind.Unknown)
    };

    foreach (var verifyResult in cases)
    {
        var root = CreateTemporaryDirectory();
        var firstProcess = new FakeManagedProcess();
        var secondProcess = new FakeManagedProcess();
        var processLauncher = new SequencedProcessLauncher(firstProcess, secondProcess);
        var accessLock = new FakeDiscordAccessLock();
        var diagnostics = new DiscorderDiagnostics(
            new AppPaths(root),
            TimeSpan.Zero);
        var snapshot = new DiscordProcessSnapshot(
            1,
            [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
            [100]);
        var discordManager = new SequencedDiscordProcessManager(
            snapshot,
            new DiscordRestartResult(
                false,
                "Discord güncelleme ekranında kaldı. Bağlantı kısa süre yenileniyor.",
                "updater window",
                DiscordRestartFailureKind.UpdaterWindow),
            new DiscordRestartResult(true, "Discord açıldı."))
        {
            VerifyResult = verifyResult
        };
        var controller = new DiscordTunnelController(
            new AppPaths(root),
            new DiscordAppScope(root, root, root),
            new FakeWireSockBootstrapper(Path.Combine(
                root,
                "WireSock VPN Client",
                "bin",
                WireSockPackage.CliExecutableFileName)),
            new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
            processLauncher,
            TimeSpan.Zero,
            accessLock,
            diagnostics,
            discordManager);

        try
        {
            await controller.ConnectAsync();

            Assert(controller.Snapshot.State == TunnelState.DiscordRestartRequired);
            Assert(controller.Snapshot.Message == verifyResult.Message);
            Assert(processLauncher.StartCount == 2);
            Assert(firstProcess.HasExited);
            Assert(!secondProcess.HasExited);
            Assert(accessLock.ClearTunnelScopeCount == 1);
            Assert(accessLock.ApplyTunnelScopeCount == 2);
            Assert(discordManager.RestartCount == 2);
            Assert(discordManager.VerifyReadyCount == 1);

            var details = controller.CreateDiagnosticDetails();
            Assert(details["discordRestartStatus"] == verifyResult.Message);
        }
        finally
        {
            await controller.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ControllerKeepsTunnelActiveWhenDiscordUpdaterDirectRestartFailsAsync()
{
    var root = CreateTemporaryDirectory();
    var firstProcess = new FakeManagedProcess();
    var secondProcess = new FakeManagedProcess();
    var processLauncher = new SequencedProcessLauncher(firstProcess, secondProcess);
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new DiscorderDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var snapshot = new DiscordProcessSnapshot(
        1,
        [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
        [100]);
    var discordManager = new SequencedDiscordProcessManager(
        snapshot,
        new DiscordRestartResult(
            false,
            "Discord güncelleme ekranında kaldı. Bağlantı kısa süre yenileniyor.",
            "updater window",
            DiscordRestartFailureKind.UpdaterWindow),
        new DiscordRestartResult(
            false,
            "Discord update ekranında kaldı.",
            "direct retry failed",
            DiscordRestartFailureKind.UpdaterWindow));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        processLauncher,
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.DiscordRestartRequired);
        Assert(controller.Snapshot.Message.Contains(
            "Discord güncelleme bağlantısı tamamlanamadı",
            StringComparison.Ordinal));
        Assert(processLauncher.StartCount == 2);
        Assert(firstProcess.HasExited);
        Assert(!secondProcess.HasExited);
        Assert(accessLock.ClearTunnelScopeCount == 1);
        Assert(accessLock.ApplyTunnelScopeCount == 2);
        Assert(discordManager.RestartCount == 2);

        var details = controller.CreateDiagnosticDetails();
        Assert(details["wireSockRunning"] == "True");
        Assert((details["discordRestartStatus"] ?? string.Empty).Contains(
            "Discord güncelleme bağlantısı tamamlanamadı",
            StringComparison.Ordinal));
    }
    finally
    {
        await controller.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}

static async Task ControllerRestoresLockWhenDiscordUpdaterRecoveryFailsAsync()
{
    var root = CreateTemporaryDirectory();
    var firstProcess = new FakeManagedProcess();
    var processLauncher = new SequencedProcessLauncher(firstProcess);
    var accessLock = new FakeDiscordAccessLock(
        applyTunnelScopeException: new InvalidOperationException("Tunnel scope failed."),
        applyTunnelScopeFailureAttempt: 2);
    var diagnostics = new DiscorderDiagnostics(
        new AppPaths(root),
        TimeSpan.Zero);
    var snapshot = new DiscordProcessSnapshot(
        1,
        [Path.Combine(root, "Discord", "app-1.0.9999", "Discord.exe")],
        [100]);
    var discordManager = new SequencedDiscordProcessManager(
        snapshot,
        new DiscordRestartResult(
            false,
            "Discord güncelleme ekranında kaldı. Bağlantı kısa süre yenileniyor.",
            "updater window",
            DiscordRestartFailureKind.UpdaterWindow),
        new DiscordRestartResult(true, "Discord açıldı."));
    var controller = new DiscordTunnelController(
        new AppPaths(root),
        new DiscordAppScope(root, root, root),
        new FakeWireSockBootstrapper(Path.Combine(
            root,
            "WireSock VPN Client",
            "bin",
            WireSockPackage.CliExecutableFileName)),
        new FakeProfileProvisioner(Path.Combine(root, "discord.conf")),
        processLauncher,
        TimeSpan.Zero,
        accessLock,
        diagnostics,
        discordManager);

    try
    {
        await controller.ConnectAsync();

        Assert(controller.Snapshot.State == TunnelState.Error);
        Assert(controller.Snapshot.Message.Contains(
            "Tunnel scope failed",
            StringComparison.Ordinal));
        Assert(processLauncher.StartCount == 1);
        Assert(firstProcess.HasExited);
        Assert(accessLock.ApplyTunnelScopeCount == 2);
        Assert(accessLock.ClearTunnelScopeCount >= 2);
        Assert(accessLock.EnableCount == 1);

        var health = await File.ReadAllTextAsync(
            Path.Combine(root, "Discorder", "logs", "health.json"));
        Assert(health.Contains("\"operation\":\"connect\"", StringComparison.Ordinal));
        Assert(health.Contains("Tunnel scope failed", StringComparison.Ordinal));
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

static async Task ControllerDirectDownloadTimeoutIsUserFriendlyAsync()
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
            new TimeoutException(
                "İndirme sırasında veri akışı zaman aşımına uğradı.")),
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
            "veri akışı",
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
            "Discord bağlantı koruması güncellenemedi",
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
            "'ptb.discord.com'",
            StringComparison.Ordinal));
        Assert(runner.Commands[0].Contains(
            "'updates.discord.com'",
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
            "Get-Process -Name $processName",
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            "CurrentVersion\\App Paths",
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            "Get-Command $name",
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            "chromium.exe",
            StringComparison.Ordinal));
        Assert(runner.Commands[2].Contains(
            "$allCandidates",
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
    var paths = new AppPaths(
        Path.Combine(root, "local"),
        Path.Combine(root, "shared"));
    var accessLock = new FakeDiscordAccessLock();
    var cleanup = new DiscorderCleanupService(
        paths,
        accessLock,
        allowNonDefaultDataRoots: true);

    try
    {
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.SettingsFile, "ayar");
        await File.WriteAllTextAsync(paths.DiscordProfile, "profil");
        await File.WriteAllTextAsync(paths.ErrorLog, "log");

        await cleanup.CleanUninstallAsync(CancellationToken.None);

        Assert(accessLock.RemoveCount == 1);
        Assert(!Directory.Exists(paths.DataDirectory));
        Assert(!Directory.Exists(paths.SharedDataDirectory));
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

static async Task CleanupServiceStopsDiagnosticsAfterRemovingStateAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(
        Path.Combine(root, "local"),
        Path.Combine(root, "shared"));
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new DiscorderDiagnostics(
        paths,
        TimeSpan.FromMilliseconds(25));
    var cleanup = new DiscorderCleanupService(
        paths,
        accessLock,
        diagnostics,
        allowNonDefaultDataRoots: true);

    try
    {
        paths.EnsureDirectories();
        diagnostics.Info("test.beforeCleanup", "kaldırma öncesi");

        await cleanup.CleanUninstallAsync(CancellationToken.None);

        diagnostics.Info("test.afterCleanup", "kaldırma sonrası");
        diagnostics.WriteHealth("kaldırma sonrası");
        await AssertThrowsAsync<InvalidOperationException>(
            () => Task.FromResult(diagnostics.CreateBundle()));
        await Task.Delay(80);

        Assert(accessLock.RemoveCount == 1);
        Assert(!Directory.Exists(paths.DataDirectory));
        Assert(!Directory.Exists(paths.SharedDataDirectory));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task CleanupServiceRejectsUnexpectedDataRootAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(
        Path.Combine(root, "local"),
        Path.Combine(root, "shared"));
    var accessLock = new FakeDiscordAccessLock();
    var diagnostics = new DiscorderDiagnostics(
        paths,
        TimeSpan.Zero);
    var cleanup = new DiscorderCleanupService(paths, accessLock, diagnostics);

    try
    {
        paths.EnsureDirectories();

        await AssertThrowsAsync<InvalidOperationException>(
            () => cleanup.CleanUninstallAsync(CancellationToken.None));

        Assert(accessLock.RemoveCount == 0);
        diagnostics.Info("test.afterRejectedCleanup", "beklenen hata sonrasi");
        Assert(File.Exists(paths.EventLog));
        Assert(Directory.Exists(paths.DataDirectory));
        Assert(Directory.Exists(paths.SharedDataDirectory));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task CleanupServiceDeletesReadOnlyFilesAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(
        Path.Combine(root, "local"),
        Path.Combine(root, "shared"));
    var accessLock = new FakeDiscordAccessLock();
    var cleanup = new DiscorderCleanupService(
        paths,
        accessLock,
        allowNonDefaultDataRoots: true);
    var localReadOnlyFile = Path.Combine(paths.LogDirectory, "readonly.log");
    var sharedReadOnlyFile = Path.Combine(paths.ProfileDirectory, "readonly.conf");

    try
    {
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(localReadOnlyFile, "log");
        await File.WriteAllTextAsync(sharedReadOnlyFile, "profile");
        File.SetAttributes(
            localReadOnlyFile,
            File.GetAttributes(localReadOnlyFile) | FileAttributes.ReadOnly);
        File.SetAttributes(
            sharedReadOnlyFile,
            File.GetAttributes(sharedReadOnlyFile) | FileAttributes.ReadOnly);

        await cleanup.CleanUninstallAsync(CancellationToken.None);

        Assert(accessLock.RemoveCount == 1);
        Assert(!Directory.Exists(paths.DataDirectory));
        Assert(!Directory.Exists(paths.SharedDataDirectory));
    }
    finally
    {
        ResetReadOnlyAttribute(localReadOnlyFile);
        ResetReadOnlyAttribute(sharedReadOnlyFile);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task CleanupServiceRepairsGeneratedStateAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(
        Path.Combine(root, "local"),
        Path.Combine(root, "shared"));
    var accessLock = new FakeDiscordAccessLock();
    var cleanup = new DiscorderCleanupService(
        paths,
        accessLock,
        allowNonDefaultDataRoots: true);

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
                ["path"] = paths.DataDirectory,
                ["programDataPath"] = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Discorder",
                    "updates"),
                ["programFilesPath"] = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Discorder")
            });
        diagnostics.WriteHealth(
            "test sağlıklı",
            new Dictionary<string, string?>
            {
                ["state"] = "ready"
            });
        await File.WriteAllTextAsync(
            Path.Combine(paths.LogDirectory, "unexpected.tmp"),
            "pakete alinmamali");
        await File.WriteAllTextAsync(
            paths.ErrorLog,
            "PrivateKey = fake-private-key-with-padding=\nAuthorization: Basic fake:token==\n");
        await File.WriteAllTextAsync(
            paths.TunnelLog,
            "access_token = fake-access-token\nnormal line\n");

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
        Assert(health.Contains("\"runtime\"", StringComparison.Ordinal));
        Assert(summary.Contains("Discorder tanılama özeti", StringComparison.Ordinal));
        Assert(summary.Contains("Performans", StringComparison.Ordinal));
        Assert(!events.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));
        AssertRedactedKnownFolder(
            events,
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "%PROGRAMDATA%");
        AssertRedactedKnownFolder(
            events,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "%PROGRAMFILES%");
        using (var healthDocument = JsonDocument.Parse(health))
        {
            var runtime = healthDocument.RootElement.GetProperty("runtime");
            AssertRuntimeMetricIsNonNegative(runtime, "workingSetBytes");
            AssertRuntimeMetricIsNonNegative(runtime, "privateMemoryBytes");
            AssertRuntimeMetricIsNonNegative(runtime, "managedMemoryBytes");
            AssertRuntimeMetricIsNonNegative(runtime, "gcHeapBytes");
            AssertRuntimeMetricIsNonNegative(runtime, "gcFragmentedBytes");
        }

        using var archive = ZipFile.OpenRead(bundlePath);
        Assert(archive.Entries.Any(entry =>
            entry.FullName.Equals("events.jsonl", StringComparison.OrdinalIgnoreCase)));
        Assert(archive.Entries.Any(entry =>
            entry.FullName.Equals("health.json", StringComparison.OrdinalIgnoreCase)));
        Assert(archive.Entries.Any(entry =>
            entry.FullName.Equals("diagnostics.md", StringComparison.OrdinalIgnoreCase)));
        Assert(archive.Entries.Any(entry =>
            entry.FullName.Equals("runtime.json", StringComparison.OrdinalIgnoreCase)));
        Assert(!archive.Entries.Any(entry =>
            entry.FullName.Equals("debug.json", StringComparison.OrdinalIgnoreCase)));
        Assert(!archive.Entries.Any(entry =>
            entry.FullName.Equals("unexpected.tmp", StringComparison.OrdinalIgnoreCase)));
        var bundledSummary = archive.GetEntry("diagnostics.md")
            ?? throw new InvalidOperationException("diagnostics.md pakette yok.");
        await using var bundledSummaryStream = bundledSummary.Open();
        using var bundledSummaryReader = new StreamReader(bundledSummaryStream);
        var bundledSummaryText = await bundledSummaryReader.ReadToEndAsync();
        Assert(!bundledSummaryText.Contains("unexpected.tmp", StringComparison.OrdinalIgnoreCase));

        var bundledRuntime = archive.GetEntry("runtime.json")
            ?? throw new InvalidOperationException("runtime.json pakette yok.");
        await using (var bundledRuntimeStream = bundledRuntime.Open())
        using (var bundledRuntimeDocument = await JsonDocument.ParseAsync(bundledRuntimeStream))
        {
            AssertRuntimeMetricIsNonNegative(
                bundledRuntimeDocument.RootElement,
                "workingSetBytes");
            AssertRuntimeMetricIsNonNegative(
                bundledRuntimeDocument.RootElement,
                "privateMemoryBytes");
            AssertRuntimeMetricIsNonNegative(
                bundledRuntimeDocument.RootElement,
                "managedMemoryBytes");
            AssertRuntimeMetricIsNonNegative(
                bundledRuntimeDocument.RootElement,
                "gcHeapBytes");
            AssertRuntimeMetricIsNonNegative(
                bundledRuntimeDocument.RootElement,
                "gcFragmentedBytes");
        }

        var bundledErrors = archive.GetEntry("errors.log")
            ?? throw new InvalidOperationException("errors.log pakette yok.");
        await using var bundledErrorsStream = bundledErrors.Open();
        using var bundledErrorsReader = new StreamReader(bundledErrorsStream);
        var bundledErrorsText = await bundledErrorsReader.ReadToEndAsync();
        Assert(!bundledErrorsText.Contains("fake-private-key", StringComparison.Ordinal));
        Assert(!bundledErrorsText.Contains("fake:token", StringComparison.Ordinal));
        Assert(bundledErrorsText.Contains("PrivateKey = [REDACTED]", StringComparison.Ordinal));
        Assert(bundledErrorsText.Contains("Authorization: [REDACTED]", StringComparison.Ordinal));

        var bundledTunnel = archive.GetEntry("tunnel.log")
            ?? throw new InvalidOperationException("tunnel.log pakette yok.");
        await using var bundledTunnelStream = bundledTunnel.Open();
        using var bundledTunnelReader = new StreamReader(bundledTunnelStream);
        var bundledTunnelText = await bundledTunnelReader.ReadToEndAsync();
        Assert(!bundledTunnelText.Contains("fake-access-token", StringComparison.Ordinal));
        Assert(bundledTunnelText.Contains("access_token = [REDACTED]", StringComparison.Ordinal));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void AssertRuntimeMetricIsNonNegative(
    JsonElement runtime,
    string propertyName)
{
    var value = runtime.GetProperty(propertyName).GetInt64();
    Assert(value >= 0);
}

static void AssertRedactedKnownFolder(
    string content,
    string knownFolderPath,
    string marker)
{
    if (string.IsNullOrWhiteSpace(knownFolderPath))
    {
        return;
    }

    Assert(!content.Contains(knownFolderPath, StringComparison.OrdinalIgnoreCase));
    Assert(content.Contains(marker, StringComparison.Ordinal));
}

static async Task DiagnosticsWritesDebugBundleOnlyWhenEnabledAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var debugEnabled = false;
    var diagnostics = new DiscorderDiagnostics(
        paths,
        isDebugDiagnosticsEnabled: () => debugEnabled);

    try
    {
        diagnostics.Info("test.debug", "normal tanılama");
        var normalBundle = diagnostics.CreateBundle();
        using (var normalArchive = ZipFile.OpenRead(normalBundle))
        {
            Assert(!normalArchive.Entries.Any(entry =>
                entry.FullName.Equals("debug.json", StringComparison.OrdinalIgnoreCase)));
        }

        debugEnabled = true;
        diagnostics.Info(
            "test.debug",
            "debug tanılama",
            new Dictionary<string, string?>
            {
                ["private_key"] = "raw-secret-value"
            });
        var debugBundle = diagnostics.CreateBundle();
        using var debugArchive = ZipFile.OpenRead(debugBundle);
        var debugEntry = debugArchive.GetEntry("debug.json")
            ?? throw new InvalidOperationException("debug.json pakette yok.");
        await using var debugStream = debugEntry.Open();
        using var debugDocument = await JsonDocument.ParseAsync(debugStream);

        Assert(debugDocument.RootElement.GetProperty("debugMode").GetString() == "enabled");
        Assert(debugDocument.RootElement.TryGetProperty("runtime", out _));
        Assert(debugDocument.RootElement.TryGetProperty("cpuSample", out _));
        Assert(debugDocument.RootElement.TryGetProperty("network", out _));
        Assert(debugDocument.RootElement.TryGetProperty("processes", out _));

        var eventLog = await File.ReadAllTextAsync(paths.EventLog);
        Assert(!eventLog.Contains("raw-secret-value", StringComparison.Ordinal));
        Assert(eventLog.Contains("[REDACTED]", StringComparison.Ordinal));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task DiagnosticsRedactsPersistentErrorLogAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var diagnostics = new DiscorderDiagnostics(paths);

    try
    {
        diagnostics.Failure(
            "test.secret",
            "PrivateKey = fake-private-key",
            new InvalidOperationException("Authorization: Bearer fake-token-value"),
            new Dictionary<string, string?>
            {
                ["access_token"] = "access_token = fake-access-token",
                ["private_key"] = "raw-private-key"
            });

        var eventLog = await File.ReadAllTextAsync(paths.EventLog);
        var errorLog = await File.ReadAllTextAsync(paths.ErrorLog);

        foreach (var text in new[] { eventLog, errorLog })
        {
            Assert(!text.Contains("fake-private-key", StringComparison.Ordinal));
            Assert(!text.Contains("fake-token-value", StringComparison.Ordinal));
            Assert(!text.Contains("fake-access-token", StringComparison.Ordinal));
            Assert(!text.Contains("raw-private-key", StringComparison.Ordinal));
            Assert(text.Contains("[REDACTED]", StringComparison.Ordinal));
        }
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ProcessLauncherRedactsTunnelLogAndConfirmsExitAsync()
{
    var root = CreateTemporaryDirectory();
    var logPath = Path.Combine(root, "logs", "tunnel.log");
    var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
    var commandShell = Environment.GetEnvironmentVariable("ComSpec");
    if (string.IsNullOrWhiteSpace(commandShell))
    {
        commandShell = Path.Combine(systemDirectory, "cmd.exe");
    }

    try
    {
        Assert(File.Exists(commandShell));
        var launcher = new ProcessLauncher();
        await using var process = launcher.Start(
            commandShell,
            [
                "/c",
                "echo access_token = fake-access-token && echo PrivateKey = fake-private-key"
            ],
            Path.GetDirectoryName(commandShell)!,
            logPath);

        for (var index = 0; index < 50 && !process.HasExited; index++)
        {
            await Task.Delay(50);
        }

        Assert(process.HasExited);
        await process.DisposeAsync();
        Assert(process.ExitConfirmed);

        var text = await File.ReadAllTextAsync(logPath);
        Assert(!text.Contains("fake-access-token", StringComparison.Ordinal));
        Assert(!text.Contains("fake-private-key", StringComparison.Ordinal));
        Assert(text.Contains("access_token = [REDACTED]", StringComparison.Ordinal));
        Assert(text.Contains("PrivateKey = [REDACTED]", StringComparison.Ordinal));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task DiagnosticsFlushesDebouncedSummaryAsync()
{
    var root = CreateTemporaryDirectory();
    var paths = new AppPaths(root);
    var diagnostics = new DiscorderDiagnostics(
        paths,
        TimeSpan.FromMilliseconds(50));

    try
    {
        diagnostics.Info("test.first", "ilk durum");
        diagnostics.Info("test.second", "son durum");

        await WaitForConditionAsync(async () =>
        {
            if (!File.Exists(paths.DiagnosticSummary))
            {
                return false;
            }

            var summary = await File.ReadAllTextAsync(paths.DiagnosticSummary);
            return summary.Contains("son durum", StringComparison.Ordinal);
        });
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

static void ResetReadOnlyAttribute(string path)
{
    if (!File.Exists(path))
    {
        return;
    }

    File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
}

static async Task WaitForConditionAsync(Func<Task<bool>> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

    while (!timeout.IsCancellationRequested)
    {
        if (await condition())
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token)
            .ContinueWith(_ => { }, TaskScheduler.Default);
    }

    throw new InvalidOperationException("Koşul beklenen sürede gerçekleşmedi.");
}

static byte[] CreateUpdatePackage(
    string version = "2.0.14",
    string? extraEntryName = null)
{
    using var buffer = new MemoryStream();
    var executableBytes = Encoding.UTF8.GetBytes("discorder-update");
    var executableHash = Convert.ToHexString(SHA256.HashData(executableBytes));
    var manifest = new UpdateManifest(
        version,
        [
            new UpdateManifestFile(
                "Discorder.exe",
                executableBytes.Length,
                executableHash)
        ]);
    var manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
        manifest,
        TestJsonOptions.Manifest));

    using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
        var executable = archive.CreateEntry("Discorder.exe");
        using (var stream = executable.Open())
        {
            stream.Write(executableBytes);
        }

        var manifestEntry = archive.CreateEntry(
            UpdatePackageValidator.ManifestFileName);
        using (var manifestStream = manifestEntry.Open())
        {
            manifestStream.Write(manifestBytes);
        }

        if (!string.IsNullOrWhiteSpace(extraEntryName))
        {
            var extraEntry = archive.CreateEntry(extraEntryName);
            using var extraStream = extraEntry.Open();
            extraStream.Write(Encoding.UTF8.GetBytes("unexpected"));
        }
    }

    return buffer.ToArray();
}

static string CreateReleaseJson(
    string version,
    Uri packageUri,
    Uri checksumUri,
    string packageSha256,
    long packageSize)
{
    return $$"""
        {
          "tag_name": "v{{version}}",
          "html_url": "https://github.com/ucsahinn/discorder/releases/tag/v{{version}}",
          "assets": [
            {
              "name": "Discorder-{{version}}-win-x64.zip",
              "browser_download_url": "{{packageUri.AbsoluteUri}}",
              "state": "uploaded",
              "size": {{packageSize}},
              "digest": "sha256:{{packageSha256}}"
            },
            {
              "name": "Discorder-{{version}}-win-x64.sha256.txt",
              "browser_download_url": "{{checksumUri.AbsoluteUri}}",
              "state": "uploaded",
              "size": 96,
              "digest": null
            }
          ]
        }
        """;
}

static async Task WriteUpdaterHelperAsync(string root)
{
    await File.WriteAllTextAsync(
        Path.Combine(root, "Discorder.Updater.exe"),
        "updater");
    await File.WriteAllTextAsync(
        Path.Combine(root, "Discorder.Core.dll"),
        "core");
    var runtimeDirectory = Path.Combine(root, "runtimes", "win", "native");
    Directory.CreateDirectory(runtimeDirectory);
    await File.WriteAllTextAsync(
        Path.Combine(runtimeDirectory, "helper.dll"),
        "runtime");
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

file static class TestJsonOptions
{
    public static readonly JsonSerializerOptions Manifest = new(
        JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}

file sealed class FakeDiscordAccessLock(
    Exception? disableException = null,
    Exception? applyTunnelScopeException = null,
    int applyTunnelScopeFailureAttempt = 1) : IDiscordAccessLock
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
        if (applyTunnelScopeException is not null
            && ApplyTunnelScopeCount == applyTunnelScopeFailureAttempt)
        {
            return Task.FromException(applyTunnelScopeException);
        }

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

file sealed class ImmediateProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value)
    {
        callback(value);
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

file sealed class FlakyDnsDownloadHandler(byte[] payload) : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestCount++;

        if (RequestCount == 1)
        {
            return Task.FromException<HttpResponseMessage>(
                new HttpRequestException(
                    "No such host is known. (github.com:443)",
                    new SocketException((int)SocketError.HostNotFound)));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });
    }
}

file sealed class StalledDownloadHandler : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestCount++;

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StalledHttpContent()
        });
    }
}

file sealed class MapHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = [];

    public void AddResponse(Uri uri, Func<HttpResponseMessage> response)
    {
        _responses[uri.AbsoluteUri] = response;
    }

    public void AddJson(Uri uri, string json)
    {
        _responses[uri.AbsoluteUri] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    public void AddText(Uri uri, string text)
    {
        _responses[uri.AbsoluteUri] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(text, Encoding.UTF8, "text/plain")
        };
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.RequestUri is not null
            && _responses.TryGetValue(request.RequestUri.AbsoluteUri, out var response))
        {
            return Task.FromResult(response());
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

file sealed class FailingHttpContent(Exception exception) : HttpContent
{
    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context)
    {
        return Task.FromException(exception);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }
}

file sealed class UnknownLengthContent(byte[] payload) : HttpContent
{
    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context)
    {
        return stream.WriteAsync(payload).AsTask();
    }

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }
}

file sealed class DeclaredLengthContent(
    byte[] payload,
    long declaredLength) : HttpContent
{
    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context)
    {
        return stream.WriteAsync(payload).AsTask();
    }

    protected override bool TryComputeLength(out long length)
    {
        length = declaredLength;
        return true;
    }

    protected override Task<Stream> CreateContentReadStreamAsync()
    {
        return Task.FromResult<Stream>(new MemoryStream(payload, writable: false));
    }
}

file sealed class StalledHttpContent : HttpContent
{
    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context)
    {
        return Task.CompletedTask;
    }

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }

    protected override Task<Stream> CreateContentReadStreamAsync()
    {
        return Task.FromResult<Stream>(new StalledStream());
    }
}

file sealed class StalledStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}

file sealed class CapturingVerifiedDownloader(byte[] payload) : IVerifiedDownloader
{
    public int DownloadCount { get; private set; }

    public Uri? LastSource { get; private set; }

    public string? LastExpectedSha256 { get; private set; }

    public long? LastMaxBytes { get; private set; }

    public async Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken,
        long? maxBytes = null,
        IProgress<DownloadProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DownloadCount++;
        LastSource = source;
        LastExpectedSha256 = expectedSha256;
        LastMaxBytes = maxBytes;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        progress?.Report(new DownloadProgress(0, payload.Length, 0));
        await File.WriteAllBytesAsync(destination, payload, cancellationToken);
        progress?.Report(new DownloadProgress(payload.Length, payload.Length, 100));
    }
}

file sealed class FloodingVerifiedDownloader(byte[] payload, int reports) : IVerifiedDownloader
{
    public int ReportCount { get; private set; }

    public async Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken,
        long? maxBytes = null,
        IProgress<DownloadProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        for (var index = 0; index < reports; index++)
        {
            var percent = reports <= 1
                ? 100
                : index * 100d / (reports - 1);
            var bytesReceived = (long)Math.Round(payload.Length * (percent / 100d));
            progress?.Report(new DownloadProgress(
                bytesReceived,
                payload.Length,
                percent));
            ReportCount++;
        }

        await File.WriteAllBytesAsync(destination, payload, cancellationToken);
    }
}

file sealed class RetryProgressVerifiedDownloader(byte[] payload) : IVerifiedDownloader
{
    public async Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken,
        long? maxBytes = null,
        IProgress<DownloadProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        progress?.Report(new DownloadProgress(
            0,
            payload.Length,
            0,
            Message: "İndirme bağlantısı kuruluyor.",
            Attempt: 1,
            MaxAttempts: 2));
        progress?.Report(new DownloadProgress(
            0,
            payload.Length,
            null,
            Message: "Bağlantı kurulamadı, tekrar deneniyor.",
            Attempt: 2,
            MaxAttempts: 2,
            IsRetry: true));
        progress?.Report(new DownloadProgress(
            payload.Length,
            payload.Length,
            100));

        await File.WriteAllBytesAsync(destination, payload, cancellationToken);
    }
}

file sealed class FakeVerifiedDownloader : IVerifiedDownloader
{
    public int DownloadCount { get; private set; }

    public long? LastMaxBytes { get; private set; }

    public async Task DownloadAsync(
        Uri source,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken,
        long? maxBytes = null,
        IProgress<DownloadProgress>? progress = null)
    {
        DownloadCount++;
        LastMaxBytes = maxBytes;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        progress?.Report(new DownloadProgress(0, 20, 0));
        await File.WriteAllTextAsync(
            destination,
            "dogrulanmis-kurucu",
            cancellationToken);
        progress?.Report(new DownloadProgress(20, 20, 100));
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

    public string? LastInstallerPath { get; private set; }

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
        LastInstallerPath = installerPath;
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
        IProgress<string>? progress,
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

        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        File.WriteAllText(profilePath, "test-profile");
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

file sealed class SequencedProcessLauncher(params FakeManagedProcess[] processes) : IProcessLauncher
{
    private int _index;

    public int StartCount { get; private set; }

    public IManagedProcess Start(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath)
    {
        if (_index >= processes.Length)
        {
            throw new InvalidOperationException("Beklenenden fazla süreç başlatıldı.");
        }

        StartCount++;
        return processes[_index++];
    }
}

file sealed class FakeManagedProcess : IManagedProcess
{
    private static int _nextProcessId = 1000;
    private readonly bool _exitOnStop;
    private readonly bool _exitOnDispose;

    public event EventHandler? Exited;

    public FakeManagedProcess(
        bool exitOnStop = true,
        bool exitOnDispose = true)
    {
        _exitOnStop = exitOnStop;
        _exitOnDispose = exitOnDispose;
        ProcessId = Interlocked.Increment(ref _nextProcessId);
        StartTime = DateTimeOffset.Now;
    }

    public bool HasExited { get; private set; }

    public bool ExitConfirmed => HasExited;

    public int? ExitCode => HasExited ? 0 : null;

    public int ProcessId { get; }

    public DateTimeOffset? StartTime { get; }

    public int StopCount { get; private set; }

    public Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        StopCount++;
        if (_exitOnStop)
        {
            HasExited = true;
            Exited?.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_exitOnDispose)
        {
            HasExited = true;
        }

        return ValueTask.CompletedTask;
    }
}

file sealed class SequencedDiscordProcessManager(
    DiscordProcessSnapshot snapshot,
    params DiscordRestartResult[] restartResults)
    : IDiscordProcessManager
{
    private int _restartIndex;

    public int RestartCount { get; private set; }

    public int VerifyReadyCount { get; private set; }

    public DiscordRestartResult VerifyResult { get; init; } =
        new(true, "Discord güncellendi.");

    public DiscordProcessSnapshot Capture() => snapshot;

    public Task<DiscordRestartResult> RestartAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_restartIndex >= restartResults.Length)
        {
            throw new InvalidOperationException("Beklenenden fazla Discord restart denendi.");
        }

        RestartCount++;
        return Task.FromResult(restartResults[_restartIndex++]);
    }

    public Task<DiscordRestartResult> VerifyReadyAsync(
        DiscordProcessSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        VerifyReadyCount++;
        return Task.FromResult(VerifyResult);
    }

    public Task<DiscordRestartResult> CloseAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken) =>
        Task.FromResult(new DiscordRestartResult(true, "Discord kapatıldı."));
}

file sealed class FakeDiscordProcessManager(
    DiscordProcessSnapshot snapshot,
    DiscordRestartResult? restartResult = null)
    : IDiscordProcessManager
{
    public int RestartCount { get; private set; }

    public int VerifyReadyCount { get; private set; }

    public int CloseCount { get; private set; }

    public DiscordProcessSnapshot Capture() => snapshot;

    public Task<DiscordRestartResult> RestartAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken)
    {
        RestartCount++;
        return Task.FromResult(
            restartResult
            ?? new DiscordRestartResult(true, "Discord yenilendi."));
    }

    public Task<DiscordRestartResult> VerifyReadyAsync(
        DiscordProcessSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        VerifyReadyCount++;
        return Task.FromResult(new DiscordRestartResult(true, "Discord güncellendi."));
    }

    public Task<DiscordRestartResult> CloseAsync(
        DiscordProcessSnapshot snapshot,
        TimeSpan gracefulTimeout,
        CancellationToken cancellationToken)
    {
        CloseCount++;
        return Task.FromResult(new DiscordRestartResult(true, "Discord kapatıldı."));
    }
}
