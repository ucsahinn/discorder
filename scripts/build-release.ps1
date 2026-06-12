[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$CodeSigningCertificatePath,
    [string]$CodeSigningCertificatePassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$RequireCodeSigning
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "artifacts\publish\$Runtime"
$projectPath = Join-Path $root 'src\Discorder.App\Discorder.App.csproj'
$project = [xml](Get-Content -Raw -LiteralPath $projectPath)
$version = $project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Discorder surumu proje dosyasindan okunamadi"
}
if ([string]::IsNullOrWhiteSpace($CodeSigningCertificatePassword) -and
    -not [string]::IsNullOrWhiteSpace($env:DISCORDER_CODESIGN_PFX_PASSWORD)) {
    $CodeSigningCertificatePassword = $env:DISCORDER_CODESIGN_PFX_PASSWORD
}

$archive = Join-Path $root "artifacts\Discorder-$version-$Runtime.zip"
$shaPath = Join-Path $root "artifacts\Discorder-$version-$Runtime.sha256.txt"
$signingStatusPath = Join-Path $root 'artifacts\signing-status.txt'
$updateManifestPath = Join-Path $output 'discorder.update-manifest.json'
$buildArtifactsPath = Join-Path ([IO.Path]::GetTempPath()) (
    'discorder-release-' + [guid]::NewGuid().ToString('N'))

Push-Location $root

try {
    & "$PSScriptRoot\verify.ps1" `
        -ArtifactsPath (Join-Path $buildArtifactsPath 'verify')

    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Recurse -Force
    }

    dotnet publish src\Discorder.App\Discorder.App.csproj `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        --output $output `
        --artifacts-path (Join-Path $buildArtifactsPath 'publish') `
        --disable-build-servers `
        -p:DebugType=None `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish hata kodu $LASTEXITCODE ile basarisiz oldu"
    }

    & "$PSScriptRoot\prepare-background-video.ps1" -PublishDirectory $output

    $signed = $false
    if (-not [string]::IsNullOrWhiteSpace($CodeSigningCertificatePath) -or $RequireCodeSigning) {
        & "$PSScriptRoot\sign-release.ps1" `
            -PublishDirectory $output `
            -CertificatePath $CodeSigningCertificatePath `
            -CertificatePassword $CodeSigningCertificatePassword `
            -TimestampUrl $TimestampUrl
        $signed = $true
    }
    else {
        Write-Host 'Kod imzalama atlandi: sertifika yapilandirilmadi.'
    }

    if ($signed) {
        Set-Content -LiteralPath $signingStatusPath -Value 'signed' -Encoding ASCII
    }
    else {
        Set-Content -LiteralPath $signingStatusPath -Value 'unsigned' -Encoding ASCII
    }

    if (Test-Path -LiteralPath $updateManifestPath) {
        Remove-Item -LiteralPath $updateManifestPath -Force
    }

    $publishRoot = [IO.Path]::GetFullPath($output).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $manifestFiles = Get-ChildItem -LiteralPath $output -File -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = [IO.Path]::GetFullPath($_.FullName).Substring($publishRoot.Length).TrimStart('\', '/')
            $relativePath = $relativePath -replace '\\', '/'
            $fileHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
            [pscustomobject]@{
                path = $relativePath
                length = $_.Length
                sha256 = $fileHash
            }
        }
    $manifest = [pscustomobject]@{
        version = $version
        files = $manifestFiles
    }
    $manifest |
        ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $updateManifestPath -Encoding UTF8

    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }

    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $archive
    Set-Content `
        -LiteralPath $shaPath `
        -Value "$($hash.Hash)  $(Split-Path -Leaf $archive)" `
        -Encoding ASCII
    $hash
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $buildArtifactsPath) {
        Remove-Item -LiteralPath $buildArtifactsPath -Recurse -Force
    }
}
