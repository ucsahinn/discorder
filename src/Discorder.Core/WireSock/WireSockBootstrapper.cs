using Discorder.Core.Configuration;
using Discorder.Core.Provisioning;
using Discorder.Core.Security;
using Discorder.Core.Updates;

namespace Discorder.Core.WireSock;

public sealed class WireSockBootstrapper : IWireSockBootstrapper
{
    private const int DiscoveryAttempts = 20;
    private const int RestartRequiredExitCode = 3010;
    private const long ProgressByteInterval = 1024 * 1024;
    private const double ProgressPercentInterval = 5;
    private static readonly TimeSpan DiscoveryDelay = TimeSpan.FromMilliseconds(500);

    private readonly AppPaths _paths;
    private readonly AppSettingsStore _settings;
    private readonly IWireSockLocator _locator;
    private readonly IVerifiedDownloader _downloader;
    private readonly IWireSockPackageVerifier _packageVerifier;
    private readonly IElevatedInstallerLauncher _installerLauncher;
    private readonly Func<string, string, CancellationToken, Task<bool>> _hashVerifier;

    public WireSockBootstrapper(
        AppPaths paths,
        AppSettingsStore settings,
        IWireSockLocator locator,
        IVerifiedDownloader downloader,
        IWireSockPackageVerifier packageVerifier,
        IElevatedInstallerLauncher installerLauncher)
        : this(
            paths,
            settings,
            locator,
            downloader,
            packageVerifier,
            installerLauncher,
            FileHashVerifier.MatchesSha256Async)
    {
    }

    internal WireSockBootstrapper(
        AppPaths paths,
        AppSettingsStore settings,
        IWireSockLocator locator,
        IVerifiedDownloader downloader,
        IWireSockPackageVerifier packageVerifier,
        IElevatedInstallerLauncher installerLauncher,
        Func<string, string, CancellationToken, Task<bool>> hashVerifier)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _packageVerifier = packageVerifier
            ?? throw new ArgumentNullException(nameof(packageVerifier));
        _installerLauncher = installerLauncher
            ?? throw new ArgumentNullException(nameof(installerLauncher));
        _hashVerifier = hashVerifier
            ?? throw new ArgumentNullException(nameof(hashVerifier));
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
        var installerDirectory = ProtectedUpdateStaging.CreateVersionDirectory(
            _paths.WireSockInstallerStagingDirectory,
            WireSockPackage.Version,
            _paths.ProtectUpdateStaging);
        var installerPath = Path.Combine(
            installerDirectory,
            WireSockPackage.InstallerFileName);

        int exitCode;
        try
        {
            if (!await TryUseLocalInstallerAsync(
                    installerPath,
                    progress,
                    cancellationToken))
            {
                progress?.Report("WireSock resmi kurucusu indiriliyor");
                double? lastProgressPercent = null;
                long lastProgressBytes = -ProgressByteInterval;
                var downloadProgress = new DirectProgress<DownloadProgress>(
                    download =>
                    {
                        if (ShouldReportDownloadProgress(
                            download,
                            ref lastProgressPercent,
                            ref lastProgressBytes))
                        {
                            progress?.Report(FormatDownloadProgress(
                                "WireSock kurucusu",
                                download));
                        }
                    });

                await _downloader.DownloadAsync(
                    WireSockPackage.WindowsX64Download,
                    installerPath,
                    WireSockPackage.WindowsX64Sha256,
                    cancellationToken,
                    maxBytes: WireSockPackage.WindowsX64MaxBytes,
                    progress: downloadProgress);
            }

            progress?.Report("WireSock kurucusunun imzası doğrulanıyor");
            _packageVerifier.VerifyInstaller(installerPath);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("WireSock kurulumunun tamamlanması bekleniyor");
            _packageVerifier.VerifyInstaller(installerPath);
            exitCode = await _installerLauncher.InstallAsync(
                installerPath,
                cancellationToken);
        }
        finally
        {
            TryDeleteInstallerDirectory(installerDirectory);
        }

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
        File.WriteAllText(
            _paths.WireSockInstallMarker,
            $"{DateTimeOffset.Now:O}{Environment.NewLine}");
        return installedExecutable;
    }

    private async Task<bool> TryUseLocalInstallerAsync(
        string installerPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var candidates = GetLocalInstallerCandidates(installerPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                if (!await _hashVerifier(
                        candidate,
                        WireSockPackage.WindowsX64Sha256,
                        cancellationToken))
                {
                    continue;
                }

                _packageVerifier.VerifyInstaller(candidate);
                progress?.Report("WireSock kurucusu yerel paketten doğrulandı");
                if (!string.Equals(
                        Path.GetFullPath(candidate),
                        Path.GetFullPath(installerPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(candidate, installerPath, overwrite: true);
                }

                return true;
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    or IOException
                    or InvalidDataException
                    or NotSupportedException
                    or UnauthorizedAccessException)
            {
                progress?.Report(
                    "Yerel WireSock kurucusu doğrulanamadı, resmi indirme deneniyor");
            }
        }

        return false;
    }

    private IEnumerable<string> GetLocalInstallerCandidates(string installerPath)
    {
        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, WireSockPackage.InstallerFileName);
        yield return Path.Combine(
            baseDirectory,
            "installers",
            WireSockPackage.InstallerFileName);
        yield return Path.Combine(
            baseDirectory,
            "WireSock",
            WireSockPackage.InstallerFileName);
        yield return Path.Combine(
            _paths.InstallerDirectory,
            WireSockPackage.InstallerFileName);
        yield return installerPath;
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

    private static void TryDeleteInstallerDirectory(string installerDirectory)
    {
        try
        {
            if (Directory.Exists(installerDirectory))
            {
                Directory.Delete(installerDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool ShouldReportDownloadProgress(
        DownloadProgress progress,
        ref double? lastPercent,
        ref long lastBytes)
    {
        if (!string.IsNullOrWhiteSpace(progress.Message))
        {
            return true;
        }

        if (progress.BytesReceived <= 0)
        {
            lastBytes = 0;
            lastPercent = progress.Percent;
            return true;
        }

        if (progress.Percent >= 100)
        {
            lastBytes = progress.BytesReceived;
            lastPercent = progress.Percent;
            return true;
        }

        if (progress.Percent is { } percent)
        {
            if (lastPercent is null
                || percent - lastPercent.Value >= ProgressPercentInterval)
            {
                lastPercent = percent;
                lastBytes = progress.BytesReceived;
                return true;
            }

            return false;
        }

        if (progress.BytesReceived - lastBytes >= ProgressByteInterval)
        {
            lastBytes = progress.BytesReceived;
            return true;
        }

        return false;
    }

    private static string FormatDownloadProgress(
        string label,
        DownloadProgress progress)
    {
        var attempt = progress.Attempt is not null && progress.MaxAttempts is not null
            ? $" ({progress.Attempt}/{progress.MaxAttempts})"
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(progress.Message))
        {
            return $"{label}: {progress.Message}{attempt}";
        }

        if (progress.TotalBytes is > 0)
        {
            return $"{label} indiriliyor: {FormatBytes(progress.BytesReceived)} / {FormatBytes(progress.TotalBytes.Value)}";
        }

        return $"{label} indiriliyor: {FormatBytes(progress.BytesReceived)}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:0} {units[unitIndex]}"
            : $"{value:0.0} {units[unitIndex]}";
    }

    private sealed class DirectProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public DirectProgress(Action<T> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Report(T value)
        {
            _handler(value);
        }
    }
}
