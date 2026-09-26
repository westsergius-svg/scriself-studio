$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

$NsisExe = 'C:\Program Files (x86)\NSIS\makensis.exe'
if (-not (Test-Path $NsisExe)) {
  $NsisZip = Join-Path $Root 'tools\nsis\nsis-3.10.zip'
  if (-not (Test-Path $NsisZip)) {
    throw "NSIS not found at $NsisExe and zip not found at $NsisZip"
  }
  $NsisRuntime = Join-Path $env:TEMP 'scriborium-nsis-3.10'
  if (Test-Path $NsisRuntime) { Remove-Item -Recurse -Force $NsisRuntime }
  Expand-Archive -Path $NsisZip -DestinationPath $NsisRuntime -Force
  $NsisExe = Join-Path $NsisRuntime 'nsis-3.10\makensis.exe'
  if (-not (Test-Path $NsisExe)) {
    throw "NSIS compiler not found at $NsisExe"
  }
}

if (-not (Test-Path '.\dist\Scriborium\Scriborium.exe')) {
  throw 'Missing dist\\Scriborium\\Scriborium.exe. Run build_exe.ps1 first.'
}

$RootNsis = $Root.Replace('\\', '\\\\')
Write-Host '[build_installer] Compiling NSIS installer...'
& $NsisExe "/DBUILD_ROOT=$RootNsis" '.\installer\scriborium.nsi'

$setup = Get-ChildItem '.\dist\Scriborium-Setup-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -eq $setup) {
  throw 'Installer build failed: setup file not found in dist\\.'
}

Write-Host "[build_installer] OK: $($setup.FullName) ($([math]::Round($setup.Length / 1MB, 2)) MB)"
