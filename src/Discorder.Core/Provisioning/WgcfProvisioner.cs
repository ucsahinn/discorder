using Discorder.Core.Configuration;
using Discorder.Core.Infrastructure;
using Discorder.Core.Profiles;

namespace Discorder.Core.Provisioning;

public sealed class WgcfProvisioner : IProfileProvisioner
{
    public const string Version = "2.2.31";
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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(allowedApplications);
        _paths.EnsureDirectories();

        await _downloader.DownloadAsync(
            WindowsX64Download,
            _paths.WgcfExecutable,
            WindowsX64Sha256,
            cancellationToken);

        if (!File.Exists(_paths.WgcfAccount))
        {
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

        throw new InvalidOperationException(
            $"{operation} çıkış kodu {result.ExitCode} ile başarısız oldu: " +
            diagnostic.Trim().ReplaceLineEndings(" "));
    }
}
