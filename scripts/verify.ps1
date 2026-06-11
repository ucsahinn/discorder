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

    $productionSourceFiles = @(
        Get-ChildItem -LiteralPath 'src' -Recurse -File |
            Where-Object {
                $_.Extension -in '.cs', '.xaml' -and
                $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
            }
    )

    if ($productionSourceFiles.Count -eq 0) {
        throw "Uretim kaynak taramasi icin .cs veya .xaml dosyasi bulunamadi"
    }

    foreach ($pattern in $forbidden) {
        $matches = @(
            $productionSourceFiles |
                Select-String -Pattern $pattern -SimpleMatch
        )

        if ($matches.Count -gt 0) {
            $formattedMatches = $matches |
                ForEach-Object {
                    $relativePath = Resolve-Path -LiteralPath $_.Path -Relative
                    "${relativePath}:$($_.LineNumber): $($_.Line.Trim())"
                }
            throw "Uretim kaynaklarinda yasakli desen bulundu: '$pattern'`n$($formattedMatches -join "`n")"
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
