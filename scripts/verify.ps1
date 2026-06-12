[CmdletBinding()]
param(
    [string]$ArtifactsPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$createdArtifactsPath = $false
if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    $ArtifactsPath = Join-Path ([IO.Path]::GetTempPath()) (
        'discorder-verify-' + [guid]::NewGuid().ToString('N'))
    $createdArtifactsPath = $true
}

Push-Location $root

try {
    dotnet build Discorder.sln `
        --configuration Release `
        --artifacts-path $ArtifactsPath `
        --disable-build-servers
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build hata kodu $LASTEXITCODE ile basarisiz oldu"
    }

    $coreTests = Join-Path $ArtifactsPath 'bin\Discorder.Core.Tests\release\Discorder.Core.Tests.dll'
    if (-not (Test-Path -LiteralPath $coreTests)) {
        throw "Core test cikisi bulunamadi: $coreTests"
    }
    dotnet $coreTests
    if ($LASTEXITCODE -ne 0) {
        throw "cekirdek testleri hata kodu $LASTEXITCODE ile basarisiz oldu"
    }

    $windowsTests = Join-Path $ArtifactsPath 'bin\Discorder.Windows.Tests\release\Discorder.Windows.Tests.dll'
    if (-not (Test-Path -LiteralPath $windowsTests)) {
        throw "Windows test cikisi bulunamadi: $windowsTests"
    }
    dotnet $windowsTests
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
    if ($createdArtifactsPath -and (Test-Path -LiteralPath $ArtifactsPath)) {
        Remove-Item -LiteralPath $ArtifactsPath -Recurse -Force
    }
}
