[CmdletBinding()]
param(
    [switch]$ProbeNetwork,
    [int]$ProbeTimeoutMs = 3000
)

$ErrorActionPreference = 'Stop'

$Domains = @(
    'discord.com',
    'discordapp.com',
    'discordapp.net',
    'discord.gg',
    'discordcdn.com',
    'cdn.discordapp.com',
    'gateway.discord.gg',
    'media.discordapp.net',
    'images-ext-1.discordapp.net',
    'images-ext-2.discordapp.net'
)
$RuleName = 'Discorder.BlockDiscordDomains'
$DisplayName = 'Discorder VPN kilidi - Discord alan adlari'
$HostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$BeginMarker = '# BEGIN Discorder Discord kilidi'
$EndMarker = '# END Discorder Discord kilidi'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-NetSecurityCmdlet {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Gerekli NetSecurity komutu bulunamadi: $Name"
    }
}

function Assert-RuleState {
    param(
        [string]$ExpectedEnabled,
        [string]$Phase
    )

    $rule = Get-NetFirewallRule -Name $RuleName -ErrorAction Stop
    if ([string]$rule.Enabled -ne $ExpectedEnabled) {
        throw "$Phase beklenen Enabled=$ExpectedEnabled, bulunan Enabled=$($rule.Enabled)"
    }

    if ([string]$rule.Direction -ne 'Outbound') {
        throw "$Phase beklenen Direction=Outbound, bulunan Direction=$($rule.Direction)"
    }

    if ([string]$rule.Action -ne 'Block') {
        throw "$Phase beklenen Action=Block, bulunan Action=$($rule.Action)"
    }

    return $rule
}

function Invoke-DiscorderRetry {
    param([scriptblock]$Action)

    for ($attempt = 1; $attempt -le 8; $attempt++) {
        try {
            & $Action
            return
        }
        catch {
            if ($attempt -eq 8) {
                throw
            }

            Start-Sleep -Milliseconds (120 * $attempt)
        }
    }
}

function Remove-DiscorderHostsLock {
    if (-not (Test-Path -LiteralPath $HostsPath)) {
        return
    }

    $content = [string](Get-Content -Raw -LiteralPath $HostsPath)
    $pattern = '(?ms)^' +
        [regex]::Escape($BeginMarker) +
        '\r?\n.*?^' +
        [regex]::Escape($EndMarker) +
        '\r?\n?'
    $updated = [regex]::Replace($content, $pattern, '')
    if ($updated -ne $content) {
        Invoke-DiscorderRetry {
            Set-Content `
                -LiteralPath $HostsPath `
                -Value $updated `
                -NoNewline `
                -Encoding ASCII
        }
    }
}

function Test-DiscorderHostsLock {
    if (-not (Test-Path -LiteralPath $HostsPath)) {
        return $false
    }

    $content = [string](Get-Content -Raw -LiteralPath $HostsPath)
    return $content.Contains($BeginMarker) -and
        $content.Contains($EndMarker) -and
        $content.Contains('0.0.0.0 discord.com') -and
        $content.Contains('::1 discord.com')
}

function Enable-DiscorderHostsLock {
    Remove-DiscorderHostsLock

    $entries = foreach ($domain in $Domains) {
        '0.0.0.0 ' + $domain
        '::1 ' + $domain
    }
    $block = @($BeginMarker) + $entries + @($EndMarker)
    Invoke-DiscorderRetry {
        Add-Content `
            -LiteralPath $HostsPath `
            -Value ($block -join [Environment]::NewLine) `
            -Encoding ASCII
    }
    ipconfig /flushdns | Out-Null
}

function Resolve-DiscordAddresses {
    $resolvedAddresses = foreach ($domain in $Domains) {
        try {
            Resolve-DnsName -Name $domain -ErrorAction Stop |
                Where-Object {
                    -not [string]::IsNullOrWhiteSpace($_.IPAddress) -and
                    $_.IPAddress -ne '0.0.0.0' -and
                    $_.IPAddress -ne '::1' -and
                    $_.IPAddress -notlike '127.*'
                } |
                ForEach-Object { $_.IPAddress }
        }
        catch {
        }
    }

    return @($resolvedAddresses | Sort-Object -Unique)
}

function Enable-DiscorderFirewallLock {
    Remove-DiscorderHostsLock
    ipconfig /flushdns | Out-Null

    $addressList = Resolve-DiscordAddresses

    if ($addressList.Count -gt 0) {
        $rule = Get-NetFirewallRule -Name $RuleName -ErrorAction SilentlyContinue
        if ($null -eq $rule) {
            New-NetFirewallRule `
                -Name $RuleName `
                -DisplayName $DisplayName `
                -Direction Outbound `
                -Action Block `
                -RemoteAddress $addressList `
                -Enabled True | Out-Null
        }
        else {
            Set-NetFirewallRule `
                -Name $RuleName `
                -NewDisplayName $DisplayName `
                -Direction Outbound `
                -Action Block `
                -RemoteAddress $addressList `
                -Enabled True | Out-Null
        }
    }

    Enable-DiscorderHostsLock
    $ruleState = Assert-RuleState -ExpectedEnabled 'True' -Phase 'Kilidi acma'
    if (-not (Test-DiscorderHostsLock)) {
        throw 'Hosts kilidi yazilamadi.'
    }

    return $ruleState
}

function Disable-DiscorderFirewallLock {
    Remove-DiscorderHostsLock
    ipconfig /flushdns | Out-Null

    $rule = Get-NetFirewallRule -Name $RuleName -ErrorAction SilentlyContinue
    if ($null -ne $rule) {
        Set-NetFirewallRule -Name $RuleName -Enabled False | Out-Null
        return Assert-RuleState -ExpectedEnabled 'False' -Phase 'Kilidi kapatma'
    }

    return $null
}

function Test-Tcp443 {
    param([string]$HostName)

    $client = [Net.Sockets.TcpClient]::new()
    try {
        $task = $client.ConnectAsync($HostName, 443)
        if (-not $task.Wait($ProbeTimeoutMs)) {
            return $false
        }

        return $client.Connected
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

if (-not (Test-IsAdministrator)) {
    throw "Bu script Windows yoneticisi olarak calistirilmalidir. PowerShell'i Yonetici olarak acip tekrar calistirin."
}

Assert-NetSecurityCmdlet 'Get-NetFirewallRule'
Assert-NetSecurityCmdlet 'New-NetFirewallRule'
Assert-NetSecurityCmdlet 'Set-NetFirewallRule'
Assert-NetSecurityCmdlet 'Resolve-DnsName'

$enabledState = Enable-DiscorderFirewallLock
$disabledState = Disable-DiscorderFirewallLock
$networkProbe = 'atlanildi'

if ($ProbeNetwork) {
    $openWithoutLock = Test-Tcp443 'discord.com'
    $enabledState = Enable-DiscorderFirewallLock
    Start-Sleep -Seconds 2
    $openWithLock = Test-Tcp443 'discord.com'

    if (-not $openWithoutLock) {
        $networkProbe = 'ag-discorda-ulasamadi'
        Write-Warning 'Kilit kapaliyken discord.com:443 acilamadi; ag/ISS durumu nedeniyle ag probu kanit sayilmadi.'
    }
    elseif ($openWithLock) {
        throw 'Kilit aktifken discord.com:443 baglantisi hala aciliyor.'
    }
    else {
        $networkProbe = 'gecti'
    }
}

$finalState = Enable-DiscorderFirewallLock

[pscustomobject]@{
    RuleName = $finalState.Name
    EnabledCheck = [string]$enabledState.Enabled
    DisabledCheck = [string]$disabledState.Enabled
    FinalCheck = [string]$finalState.Enabled
    Direction = [string]$finalState.Direction
    Action = [string]$finalState.Action
    HostsLock = [string](Test-DiscorderHostsLock)
    NetworkProbe = $networkProbe
}
