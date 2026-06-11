[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

$ErrorActionPreference = 'Stop'

$videoUrl = 'https://d8j0ntlcm91z4.cloudfront.net/user_38xzZboKViGWJOttwIXH07lWA1P/hf_20260606_154941_df1a96e1-a06f-450c-bd02-d863414cc1a0.mp4'
$expectedSha256 = '8DDCC4D001F91F43447103601299BAD902F761818B7DAF36B797134DFEF50ACC'
$assetDirectory = Join-Path $PublishDirectory 'Assets'
$videoPath = Join-Path $assetDirectory 'background.mp4'
$temporaryPath = $videoPath + '.download'

New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null

if (Test-Path -LiteralPath $videoPath) {
    $existingHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $videoPath).Hash
    if ($existingHash -eq $expectedSha256) {
        Write-Host 'Arka plan videosu zaten paket icinde ve dogrulanmis.'
        return
    }

    Remove-Item -LiteralPath $videoPath -Force
}

if (Test-Path -LiteralPath $temporaryPath) {
    Remove-Item -LiteralPath $temporaryPath -Force
}

try {
    Invoke-WebRequest `
        -Uri $videoUrl `
        -OutFile $temporaryPath `
        -UseBasicParsing

    $downloadedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $temporaryPath).Hash
    if ($downloadedHash -ne $expectedSha256) {
        throw "Arka plan videosu SHA-256 dogrulamasi basarisiz oldu: $downloadedHash"
    }

    Move-Item -LiteralPath $temporaryPath -Destination $videoPath -Force
    Write-Host 'Arka plan videosu release paketine eklendi.'
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}
