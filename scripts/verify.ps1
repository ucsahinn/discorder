[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    dotnet build Discorder.sln --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build hata kodu $LASTEXITCODE ile başarısız oldu"
    }

    dotnet run `
        --project tests\Discorder.Core.Tests\Discorder.Core.Tests.csproj `
        --configuration Release `
        --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "çekirdek testleri hata kodu $LASTEXITCODE ile başarısız oldu"
    }

    dotnet run `
        --project tests\Discorder.Windows.Tests\Discorder.Windows.Tests.csproj `
        --configuration Release `
        --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "Windows güvenlik testleri hata kodu $LASTEXITCODE ile başarısız oldu"
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
            throw "Üretim kaynaklarında yasaklı desen bulundu: '$pattern'`n$matches"
        }

        if ($LASTEXITCODE -ne 1) {
            throw "'$pattern' kontrol edilirken rg başarısız oldu"
        }
    }

    $manifest = Get-Content -Raw -LiteralPath 'src\Discorder.App\app.manifest'
    if ($manifest -notmatch 'requestedExecutionLevel level="requireAdministrator"') {
        throw "Discorder WireSock VPN Client sürecini yönetmek için yönetici manifest'iyle derlenmelidir"
    }

    Write-Host 'Doğrulama başarıyla tamamlandı.'
}
finally {
    Pop-Location
}
