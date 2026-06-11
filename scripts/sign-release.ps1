[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [string]$CertificatePath,

    [string]$CertificatePassword,

    [string]$TimestampUrl = 'http://timestamp.digicert.com',

    [switch]$Optional
)

$ErrorActionPreference = 'Stop'

function Find-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kitsRoot) {
        $candidate = Get-ChildItem `
            -LiteralPath $kitsRoot `
            -Recurse `
            -Filter signtool.exe `
            -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($null -ne $candidate) {
            return $candidate.FullName
        }
    }

    return $null
}

if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
    if ($Optional) {
        Write-Host 'Kod imzalama atlandi: sertifika yolu verilmedi.'
        return
    }

    throw 'Kod imzalama sertifika yolu verilmedi.'
}

if (-not (Test-Path -LiteralPath $CertificatePath)) {
    if ($Optional) {
        Write-Host "Kod imzalama atlandi: sertifika bulunamadi: $CertificatePath"
        return
    }

    throw "Kod imzalama sertifikasi bulunamadi: $CertificatePath"
}

if (-not (Test-Path -LiteralPath $PublishDirectory)) {
    throw "Yayin klasoru bulunamadi: $PublishDirectory"
}

$signTool = Find-SignTool
if ([string]::IsNullOrWhiteSpace($signTool)) {
    if ($Optional) {
        Write-Host 'Kod imzalama atlandi: signtool.exe bulunamadi.'
        return
    }

    throw 'signtool.exe bulunamadi. Windows SDK imzalama aracini kurun.'
}

$files = @(
    Get-ChildItem -LiteralPath $PublishDirectory -File -Recurse |
        Where-Object { $_.Extension -in '.exe', '.dll' } |
        Sort-Object FullName
)

if ($files.Count -eq 0) {
    throw "Imzalanacak exe/dll bulunamadi: $PublishDirectory"
}

$passwordArgument = @()
if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
    $passwordArgument = @('/p', $CertificatePassword)
}

foreach ($file in $files) {
    $arguments = @(
        'sign',
        '/fd',
        'SHA256',
        '/tr',
        $TimestampUrl,
        '/td',
        'SHA256',
        '/f',
        $CertificatePath
    ) + $passwordArgument + @(
        '/d',
        'Discorder',
        $file.FullName
    )

    & $signTool @arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Kod imzalama basarisiz oldu: $($file.FullName)"
    }
}

$mainExecutable = Join-Path $PublishDirectory 'Discorder.exe'
if (Test-Path -LiteralPath $mainExecutable) {
    $signature = Get-AuthenticodeSignature -LiteralPath $mainExecutable
    if ($signature.Status -ne 'Valid') {
        throw "Discorder.exe imza dogrulamasi basarisiz: $($signature.Status)"
    }
}

Write-Host "Kod imzalama tamamlandi: $($files.Count) dosya."
