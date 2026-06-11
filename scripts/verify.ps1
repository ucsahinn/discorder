[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    dotnet build Discorder.sln --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build hata kodu $LASTEXITCODE ile basarisiz oldu"
    }

    dotnet run `
        --project tests\Discorder.Core.Tests\Discorder.Core.Tests.csproj `
        --configuration Release `
        --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "cekirdek testleri hata kodu $LASTEXITCODE ile basarisiz oldu"
    }

    dotnet run `
        --project tests\Discorder.Windows.Tests\Discorder.Windows.Tests.csproj `
        --configuration Release `
        --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "Windows guvenlik testleri hata kodu $LASTEXITCODE ile basarisiz oldu"
    }

    $forbidden = @(
        'ServerCertificateCustomValidationCallback',
        'RemoteCertificateValidationCallback',
        'GoodbyeDPI',
        'ByeDPI',
        'Zapret',
        'ProxiFyre',
        'drover',
        'WinDivert',
        'WebCord',
        'AdvancedSplitWire',
        'Advanced SplitWire',
        'SplitWireTurkey',
        'RobloxPlayer',
        '"Update.exe"'
    )

    foreach ($pattern in $forbidden) {
        $matches = rg --line-number --ignore-case --glob '*.cs' --glob '*.xaml' $pattern src
        if ($LASTEXITCODE -eq 0) {
            throw "Uretim kaynaklarinda yasakli desen bulundu: '$pattern'`n$matches"
        }

        if ($LASTEXITCODE -ne 1) {
            throw "'$pattern' kontrol edilirken rg basarisiz oldu"
        }
    }

    foreach ($scriptPath in Get-ChildItem -LiteralPath 'scripts' -Filter '*.ps1') {
        $tokens = $null
        $parseErrors = $null
        [System.Management.Automation.Language.Parser]::ParseFile(
            $scriptPath.FullName,
            [ref]$tokens,
            [ref]$parseErrors) | Out-Null

        if ($parseErrors.Count -gt 0) {
            $messages = $parseErrors | ForEach-Object { $_.Message }
            throw "PowerShell script parse hatasi: $($scriptPath.Name)`n$($messages -join "`n")"
        }
    }

    $manifest = Get-Content -Raw -LiteralPath 'src\Discorder.App\app.manifest'
    if ($manifest -notmatch 'requestedExecutionLevel level="requireAdministrator"') {
        throw "Discorder WireSock VPN Client surecini yonetmek icin yonetici manifest'iyle derlenmelidir"
    }

    Write-Host 'Dogrulama basariyla tamamlandi.'
}
finally {
    Pop-Location
}
