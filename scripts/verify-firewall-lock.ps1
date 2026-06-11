[CmdletBinding()]
param(
    [switch]$ProbeNetwork,
    [int]$ProbeTimeoutMs = 3000
)

$ErrorActionPreference = 'Stop'

$KeywordId = '{4f8219da-70d9-4c14-a8d4-0ed28af940d0}'
$Keyword = 'DiscorderDiscordDomains'
$Addresses = 'discord.com,*.discord.com,discordapp.com,*.discordapp.com,discordapp.net,*.discordapp.net,discord.gg,*.discord.gg,discordcdn.com,*.discordcdn.com,media.discordapp.net,*.media.discordapp.net'
$RuleName = 'Discorder.BlockDiscordDomains'
$DisplayName = 'Discorder VPN kilidi - Discord alan adlari'

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

function Enable-DiscorderFirewallLock {
    $keywordObject = Get-NetFirewallDynamicKeywordAddress `
        -Id $KeywordId `
        -ErrorAction SilentlyContinue

    if ($null -eq $keywordObject) {
        New-NetFirewallDynamicKeywordAddress `
            -Id $KeywordId `
            -Keyword $Keyword `
            -Addresses $Addresses `
            -AutoResolve $true | Out-Null
    }
    else {
        Update-NetFirewallDynamicKeywordAddress `
            -Id $KeywordId `
            -Addresses $Addresses | Out-Null
    }

    $rule = Get-NetFirewallRule -Name $RuleName -ErrorAction SilentlyContinue
    if ($null -eq $rule) {
        New-NetFirewallRule `
            -Name $RuleName `
            -DisplayName $DisplayName `
            -Direction Outbound `
            -Action Block `
            -RemoteDynamicKeywordAddresses $KeywordId `
            -Enabled True | Out-Null
    }
    else {
        Set-NetFirewallRule `
            -Name $RuleName `
            -NewDisplayName $DisplayName `
            -Direction Outbound `
            -Action Block `
            -RemoteDynamicKeywordAddresses $KeywordId `
            -Enabled True | Out-Null
    }

    Assert-RuleState -ExpectedEnabled 'True' -Phase 'Kilidi acma'
}

function Disable-DiscorderFirewallLock {
    Set-NetFirewallRule -Name $RuleName -Enabled False | Out-Null
    Assert-RuleState -ExpectedEnabled 'False' -Phase 'Kilidi kapatma'
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

Assert-NetSecurityCmdlet 'Get-NetFirewallDynamicKeywordAddress'
Assert-NetSecurityCmdlet 'New-NetFirewallDynamicKeywordAddress'
Assert-NetSecurityCmdlet 'Update-NetFirewallDynamicKeywordAddress'
Assert-NetSecurityCmdlet 'Get-NetFirewallRule'
Assert-NetSecurityCmdlet 'New-NetFirewallRule'
Assert-NetSecurityCmdlet 'Set-NetFirewallRule'

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
    KeywordId = $KeywordId
    NetworkProbe = $networkProbe
}
