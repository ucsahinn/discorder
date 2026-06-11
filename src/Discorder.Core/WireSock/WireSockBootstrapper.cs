using Discorder.Core.Configuration;
using Discorder.Core.Provisioning;

namespace Discorder.Core.WireSock;

public sealed class WireSockBootstrapper : IWireSockBootstrapper
{
    private const int DiscoveryAttempts = 20;
    private const int RestartRequiredExitCode = 3010;
    private static readonly TimeSpan DiscoveryDelay = TimeSpan.FromMilliseconds(500);

    private readonly AppPaths _paths;
    private readonly AppSettingsStore _settings;
    private readonly IWireSockLocator _locator;
    private readonly IVerifiedDownloader _downloader;
    private readonly IWireSockPackageVerifier _packageVerifier;
    private readonly IElevatedInstallerLauncher _installerLauncher;

    public WireSockBootstrapper(
        AppPaths paths,
        AppSettingsStore settings,
        IWireSockLocator locator,
        IVerifiedDownloader downloader,
        IWireSockPackageVerifier packageVerifier,
        IElevatedInstallerLauncher installerLauncher)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _packageVerifier = packageVerifier
            ?? throw new ArgumentNullException(nameof(packageVerifier));
        _installerLauncher = installerLauncher
            ?? throw new ArgumentNullException(nameof(installerLauncher));
    }

    public string RequiredVersion => WireSockPackage.Version;

    public Uri ProductPage => WireSockPackage.ProductPage;

    public bool IsInstalled => FindTrustedExecutable() is not null;

    public bool IsSetupConsentAccepted =>
        _settings.IsSetupConsentAccepted(RequiredVersion);

    public void AcceptSetupConsent()
    {
        _settings.AcceptSetupConsent(RequiredVersion);
    }

    public async Task<string> EnsureInstalledAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            throw new PlatformNotSupportedException(
                "WireSock otomatik kurulumu yalnızca Windows x64 sistemlerde destekleniyor.");
        }

        if (!IsSetupConsentAccepted)
        {
            throw new InvalidOperationException(
                "WireSock lisansı ve Cloudflare WARP koşulları kabul edilmeden " +
                "bağlantı hazırlanamaz.");
        }

        var installedExecutable = FindTrustedExecutable();
        if (installedExecutable is not null)
        {
            return installedExecutable;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _paths.EnsureDirectories();
        var installerPath = Path.Combine(
            _paths.InstallerDirectory,
            WireSockPackage.InstallerFileName);

        progress?.Report("WireSock resmi kurucusu indiriliyor");
        await _downloader.DownloadAsync(
            WireSockPackage.WindowsX64Download,
            installerPath,
            WireSockPackage.WindowsX64Sha256,
            cancellationToken);

        progress?.Report("WireSock kurucusunun imzası doğrulanıyor");
        _packageVerifier.VerifyInstaller(installerPath);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("WireSock kurulumu tamamlanmayı bekliyor");
        var exitCode = await _installerLauncher.InstallAsync(
            installerPath,
            cancellationToken);

        if (!IsSuccessfulInstallerExitCode(exitCode))
        {
            throw new InvalidOperationException(
                $"WireSock kurulumu başarısız oldu. Çıkış kodu: {exitCode}.");
        }

        installedExecutable = await FindInstalledExecutableAsync(
            cancellationToken);

        if (installedExecutable is null)
        {
            if (exitCode == RestartRequiredExitCode)
            {
                throw new InvalidOperationException(
                    "WireSock kurulumu Windows yeniden başlatması gerektiriyor olabilir. " +
                    "Windows'u yeniden başlatıp tekrar deneyin.");
            }

            throw new InvalidOperationException(
                "WireSock kurulumu tamamlandı ancak komut satırı aracı bulunamadı.");
        }

        _settings.SetWireSockInstalledByDiscorder(installed: true);
        TryDeleteInstaller(installerPath);
        return installedExecutable;
    }

    private static bool IsSuccessfulInstallerExitCode(int exitCode)
    {
        return exitCode is 0 or RestartRequiredExitCode;
    }

    private async Task<string?> FindInstalledExecutableAsync(
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < DiscoveryAttempts; attempt++)
        {
            var executable = FindTrustedExecutable();
            if (executable is not null)
            {
                return executable;
            }

            if (attempt + 1 < DiscoveryAttempts)
            {
                await Task.Delay(DiscoveryDelay, cancellationToken);
            }
        }

        return null;
    }

    private string? FindTrustedExecutable()
    {
        var executable = _locator.FindExecutable();
        if (executable is null)
        {
            return null;
        }

        try
        {
            _packageVerifier.VerifyClient(executable);
            return executable;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryDeleteInstaller(string installerPath)
    {
        try
        {
            File.Delete(installerPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
