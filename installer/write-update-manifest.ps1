<#
.SYNOPSIS
    Создаёт манифест выпуска с SHA-256 и подписью для безопасного обновления.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $InstallerPath,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $OutputPath,
    [string] $Notes = '',
    [string] $SigningKeyPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (!$SigningKeyPath) { $SigningKeyPath = $env:SCRISELF_UPDATE_SIGNING_KEY_PATH }
if (!$SigningKeyPath -and $env:LOCALAPPDATA) {
    $SigningKeyPath = Join-Path $env:LOCALAPPDATA 'ScriSelfStudioReleaseKeys\updates-rsa.pem'
}
if (!$SigningKeyPath -or !(Test-Path -LiteralPath $SigningKeyPath -PathType Leaf)) {
    throw 'Задайте -SigningKeyPath или SCRISELF_UPDATE_SIGNING_KEY_PATH: нужен сохранённый приватный ключ выпуска.'
}

$installer = Get-Item -LiteralPath $InstallerPath
if ($installer.PSIsContainer -or $installer.Length -eq 0) { throw 'Установщик должен быть непустым файлом.' }
$versionMatch = [regex]::Match($Version, '(\d+(?:\.\d+)+)')
if (!$versionMatch.Success) { throw 'В версии выпуска должны быть числовые части через точку.' }
$numericVersion = (($versionMatch.Value.Split('.') | ForEach-Object {
    ([int] $_).ToString([System.Globalization.CultureInfo]::InvariantCulture)
}) -join '.')
$digest = (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$notesBase64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($Notes))
# Этот порядок совпадает с SignedUpdateManifest.Payload; новые строки всегда LF.
$payload = [System.Text.Encoding]::UTF8.GetBytes("scriself-update-v1`n$numericVersion`n$($installer.Name)`n$digest`n$notesBase64")

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = [System.IO.File]::ReadAllText((Join-Path $repoRoot 'src\ScriSelfStudio.Core\Updates\SignedUpdateManifest.cs'))
$publicKey = [regex]::Match($source, '-----BEGIN PUBLIC KEY-----[\s\S]*?-----END PUBLIC KEY-----').Value
if (!$publicKey) { throw 'В приложении не найден публичный ключ проверки выпуска.' }
$privateRsa = [System.Security.Cryptography.RSA]::Create()
$publicRsa = [System.Security.Cryptography.RSA]::Create()
$destination = [System.IO.Path]::GetFullPath($OutputPath)
$temporary = "$destination.tmp-$([Guid]::NewGuid().ToString('N'))"
try {
    # Приватный ключ читается только в память и никогда не попадает в манифест/журнал.
    $privateRsa.ImportFromPem([System.IO.File]::ReadAllText($SigningKeyPath))
    $publicRsa.ImportFromPem($publicKey)
    $signature = $privateRsa.SignData($payload, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pss)
    if (!$publicRsa.VerifyData($payload, $signature, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pss)) {
        throw 'Ключ выпуска не соответствует публичному ключу приложения. Манифест не создан.'
    }
    $manifest = [ordered]@{
        version = $Version; file = $installer.Name; notes = $Notes
        sha256 = $digest; signature = [Convert]::ToBase64String($signature)
    }
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
    [System.IO.File]::WriteAllText($temporary, ($manifest | ConvertTo-Json) + "`n", [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::Move($temporary, $destination, $true)
    Write-Host "Манифест создан и подпись проверена: $destination"
}
finally {
    $privateRsa.Dispose()
    $publicRsa.Dispose()
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
