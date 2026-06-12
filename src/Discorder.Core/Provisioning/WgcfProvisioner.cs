using Discorder.Core.Configuration;
using Discorder.Core.Infrastructure;
using Discorder.Core.Profiles;

namespace Discorder.Core.Provisioning;

public sealed class WgcfProvisioner : IProfileProvisioner
{
    private const long ProgressByteInterval = 1024 * 1024;
    private const double ProgressPercentInterval = 5;

    public const string Version = "2.2.31";
    public const long WindowsX64MaxBytes = 32L * 1024 * 1024;
    public const string WindowsX64Sha256 =
        "38cad8ab9cf44f8ec25c8a4e99179b1ee3510dd207e654c6aa1f6786e16d404c";

    public static readonly Uri WindowsX64Download = new(
        "https://github.com/ViRb3/wgcf/releases/download/v2.2.31/" +
        "wgcf_2.2.31_windows_amd64.exe");

    private readonly AppPaths _paths;
    private readonly IVerifiedDownloader _downloader;
    private readonly ICommandRunner _commandRunner;

    public WgcfProvisioner(
        AppPaths paths,
        IVerifiedDownloader downloader,
        ICommandRunner commandRunner)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public async Task<string> EnsureProfileAsync(
        IReadOnlyList<string> allowedApplications,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(allowedApplications);
        _paths.EnsureDirectories();

        progress?.Report("Cloudflare WARP aracı hazırlanıyor");
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
                        "Cloudflare WARP aracı",
                        download));
                }
            });

        await _downloader.DownloadAsync(
            WindowsX64Download,
            _paths.WgcfExecutable,
            WindowsX64Sha256,
            cancellationToken,
            maxBytes: WindowsX64MaxBytes,
            progress: downloadProgress);
        progress?.Report("Cloudflare WARP aracı hazır");

        if (!File.Exists(_paths.WgcfAccount))
        {
            progress?.Report("Cloudflare WARP hesabı hazırlanıyor");
            var registerResult = await _commandRunner.RunAsync(
                _paths.WgcfExecutable,
                ["register", "--accept-tos"],
                _paths.ProfileDirectory,
                TimeSpan.FromMinutes(2),
                cancellationToken);

            EnsureSucceeded("Cloudflare WARP kaydı", registerResult);

            if (!File.Exists(_paths.WgcfAccount))
            {
                throw new InvalidDataException(
                    "wgcf hesap dosyası oluşturmadan tamamlandı.");
            }
        }

        if (!File.Exists(_paths.WgcfBaseProfile))
        {
            progress?.Report("Cloudflare WARP profili hazırlanıyor");
            var generateResult = await _commandRunner.RunAsync(
                _paths.WgcfExecutable,
                ["generate"],
                _paths.ProfileDirectory,
                TimeSpan.FromMinutes(1),
                cancellationToken);

            EnsureSucceeded("WireGuard profil üretimi", generateResult);

            if (!File.Exists(_paths.WgcfBaseProfile))
            {
                throw new InvalidDataException(
                    "wgcf WireGuard profili oluşturmadan tamamlandı.");
            }
        }

        var sourceProfile = await File.ReadAllTextAsync(
            _paths.WgcfBaseProfile,
            cancellationToken);
        var discordProfile = WireGuardProfileBuilder.BuildDiscordOnly(
            sourceProfile,
            allowedApplications);

        await File.WriteAllTextAsync(
            _paths.DiscordProfile,
            discordProfile,
            cancellationToken);

        progress?.Report("Discord bağlantı profili hazır");
        return _paths.DiscordProfile;
    }

    private static void EnsureSucceeded(string operation, CommandResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        var diagnostic = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            diagnostic = $"wgcf exit code {result.ExitCode}.";
        }

        throw new InvalidOperationException(
            $"{operation} çıkış kodu {result.ExitCode} ile başarısız oldu: " +
            diagnostic.Trim().ReplaceLineEndings(" "));
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
