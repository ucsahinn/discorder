using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Discorder.Core.WireSock;

namespace Discorder.App.Installation;

public sealed class WindowsWireSockUninstaller : IWireSockUninstaller
{
    private const int ErrorCancelled = 1223;
    private const int RestartRequiredExitCode = 3010;

    private static readonly string[] UninstallKeyPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public async Task UninstallIfDiscorderInstalledAsync(
        bool installedByDiscorder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!installedByDiscorder)
        {
            return;
        }

        var productCode = FindWireSockProductCode();
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return;
        }

        int exitCode;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/x \"{productCode}\" /qn /norestart",
                UseShellExecute = true,
                Verb = "runas"
            }) ?? throw new InvalidOperationException(
                "WireSock kaldırıcısı başlatılamadı.");

            await process.WaitForExitAsync(CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            exitCode = process.ExitCode;
        }
        catch (Win32Exception exception)
            when (exception.NativeErrorCode == ErrorCancelled)
        {
            throw new InvalidOperationException(
                "WireSock kaldırma işlemi için yönetici onayı iptal edildi.",
                exception);
        }

        if (exitCode is not 0 and not RestartRequiredExitCode)
        {
            throw new InvalidOperationException(
                $"WireSock kaldırma işlemi başarısız oldu. Çıkış kodu: {exitCode}.");
        }
    }

    private static string? FindWireSockProductCode()
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var keyPath in UninstallKeyPaths)
            {
                using var uninstallRoot = root.OpenSubKey(keyPath, writable: false);
                if (uninstallRoot is null)
                {
                    continue;
                }

                foreach (var subKeyName in uninstallRoot.GetSubKeyNames())
                {
                    using var subKey = uninstallRoot.OpenSubKey(
                        subKeyName,
                        writable: false);
                    if (subKey is null || !IsWireSockEntry(subKey))
                    {
                        continue;
                    }

                    if (IsMsiProductCode(subKeyName))
                    {
                        return subKeyName;
                    }

                    var uninstallString = subKey.GetValue("QuietUninstallString") as string
                        ?? subKey.GetValue("UninstallString") as string;
                    var productCode = ExtractMsiProductCode(uninstallString);
                    if (productCode is not null)
                    {
                        return productCode;
                    }
                }
            }
        }

        return null;
    }

    private static bool IsWireSockEntry(RegistryKey? key)
    {
        if (key is null)
        {
            return false;
        }

        var displayName = key.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(displayName)
            || !displayName.Contains(
                "WireSock VPN Client",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var displayVersion = key.GetValue("DisplayVersion") as string;
        return string.IsNullOrWhiteSpace(displayVersion)
            || string.Equals(
                displayVersion,
                WireSockPackage.Version,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMsiProductCode(string value)
    {
        return value.Length == 38
            && value[0] == '{'
            && value[^1] == '}'
            && Guid.TryParse(value[1..^1], out _);
    }

    private static string? ExtractMsiProductCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = Regex.Match(
            value,
            @"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}");
        return match.Success ? match.Value : null;
    }
}
