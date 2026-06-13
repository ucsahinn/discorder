[CmdletBinding()]
param(
    [string]$ExePath,
    [int]$TimeoutSeconds = 90,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $root 'artifacts\publish\win-x64\Discorder.exe'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'artifacts\app-live-connect-smoke.txt'
}

$settingsPath = Join-Path $env:LOCALAPPDATA 'Discorder\settings.json'
$profilePath = Join-Path $env:ProgramData 'Discorder\profiles\discord.conf'
$logPath = Join-Path $env:LOCALAPPDATA 'Discorder\logs\tunnel.log'
$hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$beginMarker = '# BEGIN Discorder Discord kilidi'
$ruleName = 'Discorder.BlockDiscordDomains'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Wait-Until {
    param(
        [scriptblock]$Condition,
        [int]$Seconds
    )

    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        if (& $Condition) {
            return $true
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    return $false
}

function Test-DiscorderHostsLock {
    if (-not (Test-Path -LiteralPath $hostsPath)) {
        return $false
    }

    $content = Get-Content -Raw -LiteralPath $hostsPath
    if ($null -eq $content) {
        return $false
    }

    $content = [string]$content
    return $content.Contains($beginMarker) -and
        $content.Contains('0.0.0.0 discord.com') -and
        $content.Contains('::1 discord.com')
}

function Get-DiscorderRuleEnabled {
    $rule = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
    if ($null -eq $rule) {
        return $null
    }

    return [string]$rule.Enabled
}

function Test-Tcp443 {
    param([string]$HostName)

    $client = [Net.Sockets.TcpClient]::new()
    try {
        $task = $client.ConnectAsync($HostName, 443)
        if (-not $task.Wait(3000)) {
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

function Set-BrowserAccessEnabled {
    param([bool]$Enabled)

    Assert-Condition `
        (Test-Path -LiteralPath $settingsPath) `
        'Discorder ayar dosyasi bulunamadi; once uygulamayi bir kez acin.'

    $settings = Get-Content -Raw -LiteralPath $settingsPath |
        ConvertFrom-Json
    Assert-Condition `
        ($settings.AcceptedWireSockVersion -eq '1.4.7.1') `
        'WireSock onayi yok; canli smoke testi onay ekranini gecemez.'
    Assert-Condition `
        ([bool]$settings.AcceptedCloudflareWarpTerms) `
        'Cloudflare WARP kosul onayi yok; canli smoke testi onay ekranini gecemez.'

    if ($settings.PSObject.Properties.Name -contains 'BrowserAccessEnabled') {
        $settings.BrowserAccessEnabled = $Enabled
    }
    else {
        $settings | Add-Member `
            -NotePropertyName BrowserAccessEnabled `
            -NotePropertyValue $Enabled
    }

    $settings |
        ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $settingsPath -Encoding UTF8
}

function Get-NewLogText {
    param([long]$Offset)

    if (-not (Test-Path -LiteralPath $logPath)) {
        return ''
    }

    $stream = [IO.File]::Open(
        $logPath,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite)
    try {
        if ($stream.Length -le $Offset) {
            return ''
        }

        [void]$stream.Seek($Offset, [IO.SeekOrigin]::Begin)
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-WireSockProcess {
    param([datetime]$StartedAfter)

    @(Get-Process wiresock-client -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                $_.StartTime -ge $StartedAfter.AddSeconds(-5)
            }
            catch {
                $true
            }
        })
}

function Find-DiscorderWindow {
    param([int]$ProcessId)

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children,
        $condition)
}

function Find-DiscorderToggle {
    param([System.Windows.Automation.AutomationElement]$Window)

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        'TunnelToggleButton')
    $button = $Window.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)

    if ($null -ne $button) {
        return $button
    }

    return $null
}

function Invoke-PrimaryWindowClick {
    param([System.Windows.Automation.AutomationElement]$Window)

    $rect = $Window.Current.BoundingRectangle
    Assert-Condition `
        ($rect.Width -gt 500 -and $rect.Height -gt 470) `
        'Pencere boyutu ana buton tiklamasi icin gecersiz.'

    [void][NativeWindow]::SetForegroundWindow($Window.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 250

    $x = [int]($rect.Left + ($rect.Width * 0.30))
    $y = [int]($rect.Top + ($rect.Height * 0.49))
    [void][NativeWindow]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 100
    [NativeWindow]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [NativeWindow]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

function Invoke-DiscorderToggle {
    param([System.Windows.Automation.AutomationElement]$Window)

    $button = $null
    [void](Wait-Until {
        $script:toggleButton = Find-DiscorderToggle -Window $Window
        $null -ne $script:toggleButton
    } 20)
    $button = $script:toggleButton

    if ($null -eq $button) {
        Invoke-PrimaryWindowClick -Window $Window
        return
    }

    $pattern = $button.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

if (-not (Test-IsAdministrator)) {
    throw 'Bu script Windows yoneticisi olarak calistirilmalidir.'
}

Assert-Condition (Test-Path -LiteralPath $ExePath) "Exe bulunamadi: $ExePath"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NativeWindow
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(
        int flags,
        int dx,
        int dy,
        int data,
        UIntPtr extraInfo);
}
"@

$originalSettings = $null
if (Test-Path -LiteralPath $settingsPath) {
    $originalSettings = Get-Content -Raw -LiteralPath $settingsPath
}

$logOffset = 0
if (Test-Path -LiteralPath $logPath) {
    $logOffset = (Get-Item -LiteralPath $logPath).Length
}

$appProcess = $null
$startedAt = Get-Date
$result = [ordered]@{
    WindowFound = $false
    BrowserAccessTemporarilyEnabled = $false
    ConnectClicked = $false
    WireSockProcessSeen = $false
    WireSockStayedRunningAfterGrace = $false
    ConnectionEstablishedLog = $false
    HostsLockRemovedWhileConnected = $false
    FirewallRuleDisabledWhileConnected = $false
    DirectDiscordTcp443WhileConnected = $false
    ProfileHasDiscord = $false
    ProfileHasDiscordFullPath = $false
    ProfileHasChrome = $false
    ProfileHasEdge = $false
    ProfileHasFirefox = $false
    ProfileHasBrowserFullPath = $false
    DisconnectClicked = $false
    WireSockProcessStoppedAfterDisconnect = $false
    FinalHostsLock = $false
    FinalFirewallRuleEnabled = $false
    BrowserAccessRestored = $false
    ErrorMessage = ''
    ErrorStackTrace = ''
    SmokePassed = $false
}

$smokeError = $null

try {
    try {
        Set-BrowserAccessEnabled -Enabled $true
        $result.BrowserAccessTemporarilyEnabled = $true

        $appProcess = Start-Process `
            -FilePath $ExePath `
            -WorkingDirectory (Split-Path -Parent $ExePath) `
            -PassThru

        $window = $null
        $result.WindowFound = Wait-Until {
            $script:window = Find-DiscorderWindow -ProcessId $appProcess.Id
            $null -ne $script:window
        } 30
        Assert-Condition $result.WindowFound 'Discorder penceresi bulunamadi.'

        Invoke-DiscorderToggle -Window $window
        $result.ConnectClicked = $true

        $result.WireSockProcessSeen = Wait-Until {
            @(Get-WireSockProcess -StartedAfter $startedAt).Count -gt 0
        } 45

        Start-Sleep -Seconds 8
        $result.WireSockStayedRunningAfterGrace =
            @(Get-WireSockProcess -StartedAfter $startedAt).Count -gt 0

        $result.HostsLockRemovedWhileConnected = -not (Test-DiscorderHostsLock)
        $result.FirewallRuleDisabledWhileConnected =
            (Get-DiscorderRuleEnabled) -eq 'False'
        $result.DirectDiscordTcp443WhileConnected = Test-Tcp443 'discord.com'

        if (Test-Path -LiteralPath $profilePath) {
            $allowedAppsLine = Select-String `
                -Path $profilePath `
                -Pattern '^AllowedApps\s*=' |
                Select-Object -First 1
            $allowedAppsText = [string]$allowedAppsLine.Line
            $result.ProfileHasDiscord =
                $allowedAppsText -match 'Discord\.exe'
            $result.ProfileHasDiscordFullPath =
                $allowedAppsText -match '\\Discord(?:PTB|Canary|Development)?\\app-[^,\\]+\\Discord(?:PTB|Canary|Development)?\.exe'
            $result.ProfileHasChrome =
                $allowedAppsText -match 'chrome\.exe'
            $result.ProfileHasEdge =
                $allowedAppsText -match 'msedge\.exe'
            $result.ProfileHasFirefox =
                $allowedAppsText -match 'firefox\.exe'
            $result.ProfileHasBrowserFullPath =
                $allowedAppsText -match '\\(?:BraveSoftware\\Brave-Browser\\Application\\brave|Google\\Chrome\\Application\\chrome|Microsoft\\Edge\\Application\\msedge|Mozilla Firefox\\firefox|Opera\\opera|Programs\\Opera\\opera|Programs\\Opera GX\\opera|Vivaldi\\Application\\vivaldi)\.exe'
        }

        $result.ConnectionEstablishedLog =
            (Get-NewLogText -Offset $logOffset) -match 'Connection established'

        Invoke-DiscorderToggle -Window $window
        $result.DisconnectClicked = $true
        $result.WireSockProcessStoppedAfterDisconnect = Wait-Until {
            @(Get-WireSockProcess -StartedAfter $startedAt).Count -eq 0
        } 45

        if (-not $appProcess.HasExited) {
            [void]$appProcess.CloseMainWindow()
            [void]$appProcess.WaitForExit(15000)
        }

        $result.FinalHostsLock = Test-DiscorderHostsLock
        $result.FinalFirewallRuleEnabled = (Get-DiscorderRuleEnabled) -eq 'True'
    }
    finally {
        if ($null -ne $originalSettings) {
            Set-Content `
                -LiteralPath $settingsPath `
                -Value $originalSettings `
                -NoNewline `
                -Encoding UTF8
            $result.BrowserAccessRestored = $true
        }

        if ($null -ne $appProcess -and -not $appProcess.HasExited) {
            [void]$appProcess.CloseMainWindow()
            [void]$appProcess.WaitForExit(15000)
        }

        $result.FinalHostsLock = Test-DiscorderHostsLock
        $result.FinalFirewallRuleEnabled = (Get-DiscorderRuleEnabled) -eq 'True'
    }
}
catch {
    $smokeError = $_
    $result.ErrorMessage = $_.Exception.Message
    $result.ErrorStackTrace = $_.ScriptStackTrace
}

$criticalChecks = @(
    'WindowFound',
    'BrowserAccessTemporarilyEnabled',
    'ConnectClicked',
    'WireSockProcessSeen',
    'WireSockStayedRunningAfterGrace',
    'HostsLockRemovedWhileConnected',
    'FirewallRuleDisabledWhileConnected',
    'DirectDiscordTcp443WhileConnected',
    'ProfileHasDiscord',
    'ProfileHasDiscordFullPath',
    'ProfileHasChrome',
    'ProfileHasEdge',
    'ProfileHasFirefox',
    'ProfileHasBrowserFullPath',
    'DisconnectClicked',
    'WireSockProcessStoppedAfterDisconnect',
    'FinalHostsLock',
    'FinalFirewallRuleEnabled',
    'BrowserAccessRestored'
)

$result.SmokePassed =
    (($criticalChecks | Where-Object { -not [bool]$result[$_] }).Count -eq 0) -and
    [string]::IsNullOrWhiteSpace([string]$result.ErrorMessage)

$outputObject = [pscustomobject]$result
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) |
    Out-Null
$outputObject | Format-List | Out-String |
    Set-Content -LiteralPath $OutputPath -Encoding UTF8
$outputObject

if ($null -ne $smokeError) {
    throw $smokeError
}

foreach ($check in $criticalChecks) {
    Assert-Condition ([bool]$result[$check]) "Canli smoke testi basarisiz: $check"
}
