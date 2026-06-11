using Discorder.Core.Configuration;
using Discorder.Core.Infrastructure;

namespace Discorder.Core.Firewall;

public sealed class WindowsFirewallDiscordAccessLock : IDiscordAccessLock
{
    public const string RuleName = "Discorder.BlockDiscordDomains";
    public const string KeywordId = "{4f8219da-70d9-4c14-a8d4-0ed28af940d0}";

    private const string Keyword = "DiscorderDiscordDomains";
    private const string DisplayName = "Discorder VPN kilidi - Discord alan adları";
    private const string DiscordDomains =
        "discord.com,*.discord.com," +
        "discordapp.com,*.discordapp.com," +
        "discordapp.net,*.discordapp.net," +
        "discord.gg,*.discord.gg," +
        "discordcdn.com,*.discordcdn.com," +
        "media.discordapp.net,*.media.discordapp.net";

    private readonly AppPaths _paths;
    private readonly ICommandRunner _commandRunner;
    private readonly string _powerShellPath;

    public WindowsFirewallDiscordAccessLock(
        AppPaths paths,
        ICommandRunner commandRunner,
        string? powerShellPath = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _commandRunner = commandRunner
            ?? throw new ArgumentNullException(nameof(commandRunner));
        _powerShellPath = string.IsNullOrWhiteSpace(powerShellPath)
            ? GetDefaultPowerShellPath()
            : powerShellPath;
    }

    public Task EnableAsync(CancellationToken cancellationToken)
    {
        return RunScriptAsync(BuildEnableScript(), cancellationToken);
    }

    public Task DisableAsync(CancellationToken cancellationToken)
    {
        return RunScriptAsync(BuildDisableScript(), cancellationToken);
    }

    private async Task RunScriptAsync(
        string script,
        CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();

        var result = await _commandRunner.RunAsync(
            _powerShellPath,
            [
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                script
            ],
            _paths.DataDirectory,
            TimeSpan.FromSeconds(20),
            cancellationToken);

        if (result.Succeeded)
        {
            return;
        }

        var diagnostic = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        throw new InvalidOperationException(
            "Discord VPN kilidi Windows Firewall üzerinde güncellenemedi: " +
            diagnostic.Trim().ReplaceLineEndings(" "));
    }

    private static string BuildEnableScript()
    {
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            $"$keywordId = '{KeywordId}'",
            $"$keyword = '{Keyword}'",
            $"$addresses = '{DiscordDomains}'",
            $"$ruleName = '{RuleName}'",
            $"$displayName = '{DisplayName}'",
            "$keywordObject = Get-NetFirewallDynamicKeywordAddress -Id $keywordId -ErrorAction SilentlyContinue",
            "if ($null -eq $keywordObject) {",
            "    New-NetFirewallDynamicKeywordAddress -Id $keywordId -Keyword $keyword -Addresses $addresses -AutoResolve $true | Out-Null",
            "} else {",
            "    Update-NetFirewallDynamicKeywordAddress -Id $keywordId -Addresses $addresses | Out-Null",
            "}",
            "$rule = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue",
            "if ($null -eq $rule) {",
            "    New-NetFirewallRule -Name $ruleName -DisplayName $displayName -Direction Outbound -Action Block -RemoteDynamicKeywordAddresses $keywordId -Enabled True | Out-Null",
            "} else {",
            "    Set-NetFirewallRule -Name $ruleName -NewDisplayName $displayName -Direction Outbound -Action Block -RemoteDynamicKeywordAddresses $keywordId -Enabled True | Out-Null",
            "}"
        ]);
    }

    private static string BuildDisableScript()
    {
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            $"$ruleName = '{RuleName}'",
            "$rule = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue",
            "if ($null -ne $rule) {",
            "    Set-NetFirewallRule -Name $ruleName -Enabled False | Out-Null",
            "}"
        ]);
    }

    private static string GetDefaultPowerShellPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
    }
}
