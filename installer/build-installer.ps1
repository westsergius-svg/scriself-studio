<#
.SYNOPSIS
    Публикует приложение и собирает установщик одним запуском.

.DESCRIPTION
    Шаги:
      1. читает версию из Directory.Build.props (единый источник истины);
      2. публикует self-contained single-file сборку в artifacts\publish\win-x64-<версия>-installer;
      3. удаляет из публикации отладочные символы нативных библиотек Skia и HarfBuzz
         (это ~105 МБ, в поставке они не нужны);
      4. компилирует installer\ScriSelfStudio.iss через ISCC.exe.

    Значки приложения собираются отдельно: installer\make-icon.ps1.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',

    # Включает заранее подготовленные LanguageTool и Java рядом с программой.
    [switch] $IncludeGrammarRuntime,

    # Путь к картинке: если задан, значки пересобираются перед публикацией.
    [string] $IconSource,

    [string] $IsccPath,

    [string] $UpdateSigningKeyPath,
    [string] $UpdateNotes = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-AppVersion {
    [xml] $props = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props')
    foreach ($group in $props.Project.PropertyGroup) {
        if ($group.Version) {
            return [string] $group.Version
        }
    }

    throw 'Не удалось прочитать <Version> из Directory.Build.props.'
}

function Get-IsccPath([string] $Explicit) {
    if ($Explicit) {
        return $Explicit
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    throw 'ISCC.exe не найден. Установите Inno Setup 6 или задайте -IsccPath.'
}

$version = Get-AppVersion
Write-Host "Версия: $version"

if ($IconSource) {
    & (Join-Path $PSScriptRoot 'make-icon.ps1') -Source $IconSource
}

$publishRelative = "artifacts\publish\$Runtime-$version-installer"
$publishDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $publishRelative))
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\publish')) + [System.IO.Path]::DirectorySeparatorChar
if (!$publishDir.StartsWith($publishRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Каталог публикации должен находиться внутри $publishRoot"
}

if (Test-Path -LiteralPath $publishDir) {
    $previousPublish = "$publishDir-before-$(Get-Date -Format 'yyyyMMdd-HHmmssfff')"
    if (!$previousPublish.StartsWith($publishRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Недопустимый каталог сохранения предыдущей публикации.'
    }
    Move-Item -LiteralPath $publishDir -Destination $previousPublish
}

Write-Host "Публикация в $publishRelative"
& dotnet publish (Join-Path $repoRoot 'src\ScriSelfStudio.App\ScriSelfStudio.App.csproj') `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    "-p:IncludeGrammarRuntime=$($IncludeGrammarRuntime.IsPresent.ToString().ToLowerInvariant())" `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish завершился с кодом $LASTEXITCODE."
}

# Символы нативных библиотек приходят из NuGet-пакетов и не относятся к нашему коду.
Get-ChildItem -LiteralPath $publishDir -Filter *.pdb -File | ForEach-Object {
    Write-Host "Убираю из поставки: $($_.Name)"
    $_.Delete()
}

$iscc = Get-IsccPath $IsccPath
Write-Host "Компиляция установщика: $iscc"

$setup = Join-Path $repoRoot "artifacts\installer\ScriSelfStudio-Setup-$version.exe"
if (Test-Path -LiteralPath $setup) {
    Move-Item -LiteralPath $setup -Destination "$setup-before-$(Get-Date -Format 'yyyyMMdd-HHmmssfff')"
}

# Версия и каталог публикации передаются в скрипт, чтобы не держать их в двух местах.
& $iscc `
    "/DAppVersion=$version" `
    "/DPublishDir=..\artifacts\publish\$Runtime-$version-installer" `
    (Join-Path $PSScriptRoot 'ScriSelfStudio.iss')
if ($LASTEXITCODE -ne 0) {
    throw "ISCC завершился с кодом $LASTEXITCODE."
}

if (!$UpdateSigningKeyPath) { $UpdateSigningKeyPath = $env:SCRISELF_UPDATE_SIGNING_KEY_PATH }
if (!$UpdateSigningKeyPath -and $env:LOCALAPPDATA) {
    $localReleaseKey = Join-Path $env:LOCALAPPDATA 'ScriSelfStudioReleaseKeys\updates-rsa.pem'
    if (Test-Path -LiteralPath $localReleaseKey) { $UpdateSigningKeyPath = $localReleaseKey }
}
if ($UpdateSigningKeyPath) {
    & (Join-Path $PSScriptRoot 'write-update-manifest.ps1') -InstallerPath $setup -Version $version `
        -OutputPath (Join-Path (Split-Path -Parent $setup) 'latest.json') `
        -Notes $UpdateNotes -SigningKeyPath $UpdateSigningKeyPath
}
else {
    Write-Host 'Установщик собран. Для канала обновлений отдельно создайте подписанный latest.json через write-update-manifest.ps1.'
}

Write-Host ''
Write-Host "Готово: $setup"
Write-Host ("Размер: {0:N1} МБ" -f ((Get-Item -LiteralPath $setup).Length / 1MB))
