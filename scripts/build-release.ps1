[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "artifacts\publish\$Runtime"
$archive = Join-Path $root "artifacts\Discorder-2.0.0-$Runtime.zip"

Push-Location $root

try {
    & "$PSScriptRoot\verify.ps1"

    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Recurse -Force
    }

    dotnet publish src\Discorder.App\Discorder.App.csproj `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        --output $output `
        -p:DebugType=None `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish hata kodu $LASTEXITCODE ile başarısız oldu"
    }

    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }

    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive
    Get-FileHash -Algorithm SHA256 -LiteralPath $archive
}
finally {
    Pop-Location
}
