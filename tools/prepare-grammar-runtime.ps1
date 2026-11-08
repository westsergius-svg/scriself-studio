param(
    [string]$Destination = (Join-Path $PSScriptRoot '../artifacts/grammar-runtime/win-x64'),
    [string]$CacheDirectory = (Join-Path $PSScriptRoot '../artifacts/grammar-downloads')
)
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
$CacheDirectory = [IO.Path]::GetFullPath($CacheDirectory)
$packages = @(
    @{ Name='LanguageTool-6.6.zip'; Url='https://languagetool.org/download/LanguageTool-6.6.zip'; Hash='53600506B399BB5FFE1E4C8DEC794FD378212F14AAF38CCEF9B6F89314D11631'; Folder='LanguageTool-6.6' },
    @{ Name='temurin-jre.zip'; Url='https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.13%2B9/OpenJDK21U-jre_x64_windows_hotspot_21.0.13_9.zip'; Hash='2332C0B4CFF6BED722DA3567C91D5E47568BCBA863FA4945D0A8D6FC76158628'; Folder='jdk-21.0.13+9-jre' }
)
if (Test-Path -LiteralPath $Destination) { throw "Destination already exists; use a new folder to preserve the existing runtime: $Destination" }
New-Item -ItemType Directory -Force -Path $CacheDirectory | Out-Null
foreach ($package in $packages) {
    $archive = Join-Path $CacheDirectory $package.Name
    if (-not (Test-Path -LiteralPath $archive)) {
        Invoke-WebRequest -Uri $package.Url -OutFile $archive
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $package.Hash) {
        throw "Checksum mismatch: $($package.Name). The existing file was preserved."
    }
}
New-Item -ItemType Directory -Path $Destination | Out-Null
foreach ($package in $packages) {
    Expand-Archive -LiteralPath (Join-Path $CacheDirectory $package.Name) -DestinationPath $Destination
}
@'
maxTextLength=16000
maxTextHardLength=16000
maxCheckTimeMillis=10000
maxCheckThreads=1
maxWorkQueueSize=2
cacheSize=128
pipelineCaching=true
maxPipelinePoolSize=2
'@ | Set-Content -LiteralPath (Join-Path $Destination 'server.properties') -Encoding ascii
$packages | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Destination 'packages.json') -Encoding utf8
@'
Unmodified optional local components for ScriSelf Studio (Windows x64).
LanguageTool 6.6: LGPL 2.1 or later. COPYING.txt and third-party-licenses/ are preserved.
Source: https://github.com/languagetool-org/languagetool/tree/v6.6
Archive: https://languagetool.org/download/LanguageTool-6.6.zip
Temurin JRE 21.0.13+9: GPLv2 with Classpath Exception; legal/ and NOTICE are preserved.
Source and source archive: https://github.com/adoptium/temurin21-binaries/releases/tag/jdk-21.0.13%2B9
The original component licenses also apply to their dictionaries and bundled libraries.
No manuscript, user configuration or generated log belongs in this runtime folder.
'@ | Set-Content -LiteralPath (Join-Path $Destination 'NOTICE.txt') -Encoding utf8
& (Join-Path $Destination 'jdk-21.0.13+9-jre/bin/java.exe') -version
if ($LASTEXITCODE -ne 0) { throw 'Prepared Java runtime did not start' }
Write-Output "Prepared runtime: $Destination"
