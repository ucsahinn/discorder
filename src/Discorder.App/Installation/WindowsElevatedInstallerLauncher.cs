using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Discorder.Core.WireSock;

namespace Discorder.App.Installation;

public sealed class WindowsElevatedInstallerLauncher : IElevatedInstallerLauncher
{
    private const int ErrorCancelled = 1223;

    public async Task<int> InstallAsync(
        string installerPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{installerPath}\" /qn /norestart",
                WorkingDirectory = Path.GetDirectoryName(installerPath)!,
                UseShellExecute = true,
                Verb = "runas"
            }) ?? throw new InvalidOperationException(
                "WireSock kurucusu başlatılamadı.");

            // Yükseltme sonrası kurucu yaşam döngüsü Windows Installer denetiminde kalır.
            await process.WaitForExitAsync(CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            return process.ExitCode;
        }
        catch (Win32Exception exception)
            when (exception.NativeErrorCode == ErrorCancelled)
        {
            throw new InvalidOperationException(
                "WireSock kurulumu için yönetici onayı iptal edildi.",
                exception);
        }
    }
}
