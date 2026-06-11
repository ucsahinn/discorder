using Discorder.Core.Configuration;
using Discorder.Core.Infrastructure;

namespace Discorder.Core.Firewall;

public sealed class WindowsFirewallDiscordAccessLock : IDiscordAccessLock
{
    public const string RuleName = "Discorder.BlockDiscordDomains";

    private const string DisplayName = "Discorder VPN kilidi - Discord alan adları";
    private const string DiscordDomains =
        "'discord.com'," +
        "'discordapp.com'," +
        "'discordapp.net'," +
        "'discord.gg'," +
        "'discordcdn.com'," +
        "'cdn.discordapp.com'," +
        "'gateway.discord.gg'," +
        "'media.discordapp.net'," +
        "'images-ext-1.discordapp.net'," +
        "'images-ext-2.discordapp.net'";

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

    public Task RemoveAsync(CancellationToken cancellationToken)
    {
        return RunScriptAsync(BuildRemoveScript(), cancellationToken);
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
            "Discord VPN kilidi güncellenemedi: " +
            diagnostic.Trim().ReplaceLineEndings(" "));
    }

    private static string BuildEnableScript()
    {
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            $"$domains = @({DiscordDomains})",
            $"$ruleName = '{RuleName}'",
            $"$displayName = '{DisplayName}'",
            "$hostsPath = Join-Path $env:SystemRoot 'System32\\drivers\\etc\\hosts'",
            "$beginMarker = '# BEGIN Discorder Discord kilidi'",
            "$endMarker = '# END Discorder Discord kilidi'",
            "function Invoke-DiscorderRetry([scriptblock]$action) {",
            "    for ($attempt = 1; $attempt -le 8; $attempt++) {",
            "        try {",
            "            & $action",
            "            return",
            "        } catch {",
            "            if ($attempt -eq 8) { throw }",
            "            Start-Sleep -Milliseconds (120 * $attempt)",
            "        }",
            "    }",
            "}",
            "function Remove-DiscorderHostsLock {",
            "    if (-not (Test-Path -LiteralPath $hostsPath)) { return }",
            "    $content = [string](Get-Content -Raw -LiteralPath $hostsPath)",
            "    $pattern = '(?ms)^' + [regex]::Escape($beginMarker) + '\\r?\\n.*?^' + [regex]::Escape($endMarker) + '\\r?\\n?'",
            "    $updated = [regex]::Replace($content, $pattern, '')",
            "    if ($updated -ne $content) {",
            "        Invoke-DiscorderRetry { Set-Content -LiteralPath $hostsPath -Value $updated -NoNewline -Encoding ASCII }",
            "    }",
            "}",
            "function Enable-DiscorderHostsLock {",
            "    Remove-DiscorderHostsLock",
            "    $entries = foreach ($domain in $domains) {",
            "        '0.0.0.0 ' + $domain",
            "        '::1 ' + $domain",
            "    }",
            "    $block = @($beginMarker) + $entries + @($endMarker)",
            "    Invoke-DiscorderRetry { Add-Content -LiteralPath $hostsPath -Value ($block -join [Environment]::NewLine) -Encoding ASCII }",
            "    ipconfig /flushdns | Out-Null",
            "}",
            "Remove-DiscorderHostsLock",
            "ipconfig /flushdns | Out-Null",
            "$resolvedAddresses = foreach ($domain in $domains) {",
            "    try {",
            "        Resolve-DnsName -Name $domain -ErrorAction Stop |",
            "            Where-Object { -not [string]::IsNullOrWhiteSpace($_.IPAddress) -and $_.IPAddress -ne '0.0.0.0' -and $_.IPAddress -ne '::1' -and $_.IPAddress -notlike '127.*' } |",
            "            ForEach-Object { $_.IPAddress }",
            "    } catch {",
            "    }",
            "}",
            "$addressList = @($resolvedAddresses | Sort-Object -Unique)",
            "if ($addressList.Count -gt 0) {",
            "    $rule = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue",
            "    if ($null -eq $rule) {",
            "        New-NetFirewallRule -Name $ruleName -DisplayName $displayName -Direction Outbound -Action Block -RemoteAddress $addressList -Enabled True | Out-Null",
            "    } else {",
            "        Set-NetFirewallRule -Name $ruleName -NewDisplayName $displayName -Direction Outbound -Action Block -RemoteAddress $addressList -Enabled True | Out-Null",
            "    }",
            "}",
            "Enable-DiscorderHostsLock"
        ]);
    }

    private static string BuildDisableScript()
    {
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            $"$ruleName = '{RuleName}'",
            "$hostsPath = Join-Path $env:SystemRoot 'System32\\drivers\\etc\\hosts'",
            "$beginMarker = '# BEGIN Discorder Discord kilidi'",
            "$endMarker = '# END Discorder Discord kilidi'",
            "function Invoke-DiscorderRetry([scriptblock]$action) {",
            "    for ($attempt = 1; $attempt -le 8; $attempt++) {",
            "        try {",
            "            & $action",
            "            return",
            "        } catch {",
            "            if ($attempt -eq 8) { throw }",
            "            Start-Sleep -Milliseconds (120 * $attempt)",
            "        }",
            "    }",
            "}",
            "if (Test-Path -LiteralPath $hostsPath) {",
            "    $content = [string](Get-Content -Raw -LiteralPath $hostsPath)",
            "    $pattern = '(?ms)^' + [regex]::Escape($beginMarker) + '\\r?\\n.*?^' + [regex]::Escape($endMarker) + '\\r?\\n?'",
            "    $updated = [regex]::Replace($content, $pattern, '')",
            "    if ($updated -ne $content) {",
            "        Invoke-DiscorderRetry { Set-Content -LiteralPath $hostsPath -Value $updated -NoNewline -Encoding ASCII }",
            "        ipconfig /flushdns | Out-Null",
            "    }",
            "}",
            "$rule = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue",
            "if ($null -ne $rule) {",
            "    Set-NetFirewallRule -Name $ruleName -Enabled False | Out-Null",
            "}"
        ]);
    }

    private static string BuildRemoveScript()
    {
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            $"$ruleName = '{RuleName}'",
            "$hostsPath = Join-Path $env:SystemRoot 'System32\\drivers\\etc\\hosts'",
            "$beginMarker = '# BEGIN Discorder Discord kilidi'",
            "$endMarker = '# END Discorder Discord kilidi'",
            "function Invoke-DiscorderRetry([scriptblock]$action) {",
            "    for ($attempt = 1; $attempt -le 8; $attempt++) {",
            "        try {",
            "            & $action",
            "            return",
            "        } catch {",
            "            if ($attempt -eq 8) { throw }",
            "            Start-Sleep -Milliseconds (120 * $attempt)",
            "        }",
            "    }",
            "}",
            "$hostsChanged = $false",
            "if (Test-Path -LiteralPath $hostsPath) {",
            "    $content = [string](Get-Content -Raw -LiteralPath $hostsPath)",
            "    $pattern = '(?ms)^' + [regex]::Escape($beginMarker) + '\\r?\\n.*?^' + [regex]::Escape($endMarker) + '\\r?\\n?'",
            "    $updated = [regex]::Replace($content, $pattern, '')",
            "    if ($updated -ne $content) {",
            "        Invoke-DiscorderRetry { Set-Content -LiteralPath $hostsPath -Value $updated -NoNewline -Encoding ASCII }",
            "        $hostsChanged = $true",
            "    }",
            "}",
            "$rule = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue",
            "if ($null -ne $rule) {",
            "    Remove-NetFirewallRule -Name $ruleName | Out-Null",
            "}",
            "if ($hostsChanged) {",
            "    ipconfig /flushdns | Out-Null",
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
