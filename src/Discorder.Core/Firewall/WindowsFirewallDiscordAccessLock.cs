using Discorder.Core.Configuration;
using Discorder.Core.Infrastructure;

namespace Discorder.Core.Firewall;

public sealed class WindowsFirewallDiscordAccessLock : IDiscordAccessLock
{
    public const string RuleName = "Discorder.BlockDiscordDomains";
    public const string BrowserScopeGroup = "Discorder.TunnelScope.Browsers";

    private const string DisplayName = "Discorder VPN kilidi - Discord alan adları";
    private const string BrowserScopeDisplayName =
        "Discorder tünel kapsamı - tarayıcı Discord engeli";
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

    public Task ApplyTunnelScopeAsync(
        bool includeBrowserAccess,
        CancellationToken cancellationToken)
    {
        return RunScriptAsync(
            BuildApplyTunnelScopeScript(includeBrowserAccess),
            cancellationToken);
    }

    public Task ClearTunnelScopeAsync(CancellationToken cancellationToken)
    {
        return RunScriptAsync(BuildClearTunnelScopeScript(), cancellationToken);
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
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            diagnostic = $"PowerShell exit code {result.ExitCode}.";
        }

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
            $"$browserScopeGroup = '{BrowserScopeGroup}'",
            .. BuildHostsFileFunctions(),
            .. BuildFirewallGroupFunctions(),
            "$script:discorderHostsChanged = $false",
            "foreach ($group in @($browserScopeGroup)) {",
            "    Clear-DiscorderFirewallGroup $group",
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
            "    if ($null -ne $rule) {",
            "        Remove-NetFirewallRule -Name $ruleName | Out-Null",
            "    }",
            "    New-NetFirewallRule -Name $ruleName -DisplayName $displayName -Direction Outbound -Action Block -RemoteAddress $addressList -Enabled True | Out-Null",
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
            .. BuildHostsFileFunctions(),
            "$script:discorderHostsChanged = $false",
            "Remove-DiscorderHostsLock",
            "if ($script:discorderHostsChanged) {",
            "    ipconfig /flushdns | Out-Null",
            "}",
            "$rule = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue",
            "if ($null -ne $rule) {",
            "    Set-NetFirewallRule -Name $ruleName -Enabled False | Out-Null",
            "}"
        ]);
    }

    private static string BuildApplyTunnelScopeScript(bool includeBrowserAccess)
    {
        var includeBrowserAccessLiteral = includeBrowserAccess ? "$true" : "$false";
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            $"$includeBrowserAccess = {includeBrowserAccessLiteral}",
            $"$domains = @({DiscordDomains})",
            $"$browserScopeGroup = '{BrowserScopeGroup}'",
            $"$browserScopeDisplayName = '{BrowserScopeDisplayName}'",
            .. BuildFirewallGroupFunctions(),
            "function Join-DiscorderPath([string]$root, [string]$relative) {",
            "    if ([string]::IsNullOrWhiteSpace($root)) { return $null }",
            "    return (Join-Path $root $relative)",
            "}",
            "function Clear-DiscorderTunnelScope {",
            "    foreach ($group in @($browserScopeGroup)) {",
            "        Clear-DiscorderFirewallGroup $group",
            "    }",
            "}",
            "function Get-DiscorderAddresses {",
            "    $resolvedAddresses = foreach ($domain in $domains) {",
            "        try {",
            "            Resolve-DnsName -Name $domain -ErrorAction Stop |",
            "                Where-Object { -not [string]::IsNullOrWhiteSpace($_.IPAddress) -and $_.IPAddress -ne '0.0.0.0' -and $_.IPAddress -ne '::1' -and $_.IPAddress -notlike '127.*' } |",
            "                ForEach-Object { $_.IPAddress }",
            "        } catch {",
            "        }",
            "    }",
            "    return @($resolvedAddresses | Sort-Object -Unique)",
            "}",
            "function Get-DiscorderBrowserPrograms {",
            "    $programFilesX86 = ${env:ProgramFiles(x86)}",
            "    $candidates = @(",
            "        (Join-DiscorderPath $env:ProgramFiles 'Google\\Chrome\\Application\\chrome.exe'),",
            "        (Join-DiscorderPath $programFilesX86 'Google\\Chrome\\Application\\chrome.exe'),",
            "        (Join-DiscorderPath $env:LOCALAPPDATA 'Google\\Chrome\\Application\\chrome.exe'),",
            "        (Join-DiscorderPath $env:ProgramFiles 'Microsoft\\Edge\\Application\\msedge.exe'),",
            "        (Join-DiscorderPath $programFilesX86 'Microsoft\\Edge\\Application\\msedge.exe'),",
            "        (Join-DiscorderPath $env:ProgramFiles 'BraveSoftware\\Brave-Browser\\Application\\brave.exe'),",
            "        (Join-DiscorderPath $programFilesX86 'BraveSoftware\\Brave-Browser\\Application\\brave.exe'),",
            "        (Join-DiscorderPath $env:LOCALAPPDATA 'BraveSoftware\\Brave-Browser\\Application\\brave.exe'),",
            "        (Join-DiscorderPath $env:ProgramFiles 'Mozilla Firefox\\firefox.exe'),",
            "        (Join-DiscorderPath $programFilesX86 'Mozilla Firefox\\firefox.exe'),",
            "        (Join-DiscorderPath $env:LOCALAPPDATA 'Programs\\Opera\\opera.exe'),",
            "        (Join-DiscorderPath $env:LOCALAPPDATA 'Programs\\Opera GX\\opera.exe'),",
            "        (Join-DiscorderPath $env:ProgramFiles 'Opera\\opera.exe'),",
            "        (Join-DiscorderPath $programFilesX86 'Opera\\opera.exe'),",
            "        (Join-DiscorderPath $env:LOCALAPPDATA 'Vivaldi\\Application\\vivaldi.exe'),",
            "        (Join-DiscorderPath $env:ProgramFiles 'Vivaldi\\Application\\vivaldi.exe'),",
            "        (Join-DiscorderPath $programFilesX86 'Vivaldi\\Application\\vivaldi.exe')",
            "    )",
            "    return @($candidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) } | Sort-Object -Unique)",
            "}",
            "Clear-DiscorderTunnelScope",
            "if ($includeBrowserAccess) { return }",
            "$addressList = Get-DiscorderAddresses",
            "if ($addressList.Count -eq 0) { return }",
            "$programs = Get-DiscorderBrowserPrograms",
            "$index = 0",
            "foreach ($program in $programs) {",
            "    $index++",
            "    New-NetFirewallRule -Name ($browserScopeGroup + '.' + $index) -DisplayName ($browserScopeDisplayName + ' ' + $index) -Group $browserScopeGroup -Direction Outbound -Action Block -Program $program -RemoteAddress $addressList -Enabled True | Out-Null",
            "}"
        ]);
    }

    private static string BuildClearTunnelScopeScript()
    {
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            $"$browserScopeGroup = '{BrowserScopeGroup}'",
            .. BuildFirewallGroupFunctions(),
            "foreach ($group in @($browserScopeGroup)) {",
            "    Clear-DiscorderFirewallGroup $group",
            "}"
        ]);
    }

    private static string BuildRemoveScript()
    {
        return string.Join(Environment.NewLine, [
            "$ErrorActionPreference = 'Stop'",
            $"$ruleName = '{RuleName}'",
            $"$browserScopeGroup = '{BrowserScopeGroup}'",
            "$hostsPath = Join-Path $env:SystemRoot 'System32\\drivers\\etc\\hosts'",
            "$beginMarker = '# BEGIN Discorder Discord kilidi'",
            "$endMarker = '# END Discorder Discord kilidi'",
            .. BuildHostsFileFunctions(),
            .. BuildFirewallGroupFunctions(),
            "$script:discorderHostsChanged = $false",
            "Remove-DiscorderHostsLock",
            "$rule = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue",
            "if ($null -ne $rule) {",
            "    Remove-NetFirewallRule -Name $ruleName | Out-Null",
            "}",
            "foreach ($group in @($browserScopeGroup)) {",
            "    Clear-DiscorderFirewallGroup $group",
            "}",
            "if ($script:discorderHostsChanged) {",
            "    ipconfig /flushdns | Out-Null",
            "}"
        ]);
    }

    private static string[] BuildFirewallGroupFunctions()
    {
        return [
            "function Clear-DiscorderFirewallGroup([string]$group) {",
            "    $rules = @(Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue)",
            "    foreach ($rule in $rules) {",
            "        try {",
            "            Remove-NetFirewallRule -Name $($rule.Name) -ErrorAction Stop | Out-Null",
            "        } catch {",
            "            $remaining = Get-NetFirewallRule -Name $($rule.Name) -ErrorAction SilentlyContinue",
            "            if ($null -ne $remaining) { throw }",
            "        }",
            "    }",
            "}"
        ];
    }

    private static string[] BuildHostsFileFunctions()
    {
        return [
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
            "function Read-DiscorderText([string]$path) {",
            "    if (-not [IO.File]::Exists($path)) { return '' }",
            "    return [IO.File]::ReadAllText($path, [Text.Encoding]::ASCII)",
            "}",
            "function Write-DiscorderText([string]$path, [string]$value) {",
            "    Invoke-DiscorderRetry { [IO.File]::WriteAllText($path, $value, [Text.Encoding]::ASCII) }",
            "}",
            "function Remove-DiscorderHostsLock {",
            "    if (-not [IO.File]::Exists($hostsPath)) { return }",
            "    $content = Read-DiscorderText $hostsPath",
            "    $pattern = '(?ms)^' + [regex]::Escape($beginMarker) + '\\r?\\n.*?^' + [regex]::Escape($endMarker) + '\\r?\\n?'",
            "    $updated = [regex]::Replace($content, $pattern, '')",
            "    if ($updated -ne $content) {",
            "        Write-DiscorderText $hostsPath $updated",
            "        $script:discorderHostsChanged = $true",
            "    }",
            "}",
            "function Enable-DiscorderHostsLock {",
            "    Remove-DiscorderHostsLock",
            "    $entries = foreach ($domain in $domains) {",
            "        '0.0.0.0 ' + $domain",
            "        '::1 ' + $domain",
            "    }",
            "    $block = @($beginMarker) + $entries + @($endMarker)",
            "    $content = Read-DiscorderText $hostsPath",
            "    if ($content.Length -gt 0 -and -not $content.EndsWith([Environment]::NewLine)) {",
            "        $content += [Environment]::NewLine",
            "    }",
            "    $content += ($block -join [Environment]::NewLine) + [Environment]::NewLine",
            "    Write-DiscorderText $hostsPath $content",
            "    $script:discorderHostsChanged = $true",
            "    ipconfig /flushdns | Out-Null",
            "}"
        ];
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
