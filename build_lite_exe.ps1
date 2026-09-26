$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

Write-Host '[build_lite_exe] Cleaning previous Lite build artifacts...'
if (Test-Path '.\build\Scriborium-Lite') { Remove-Item -Recurse -Force '.\build\Scriborium-Lite' -ErrorAction SilentlyContinue }
if (Test-Path '.\dist\Scriborium-Lite') { Remove-Item -Recurse -Force '.\dist\Scriborium-Lite' -ErrorAction SilentlyContinue }
if (Test-Path '.\Scriborium-Lite.spec') { Remove-Item -Force '.\Scriborium-Lite.spec' -ErrorAction SilentlyContinue }

Write-Host '[build_lite_exe] Building Scriborium Lite with PyInstaller...'
python -m PyInstaller `
  --noconfirm `
  --clean `
  --windowed `
  --name Scriborium-Lite `
  --icon "src\scriborium\resources\icons\app.ico" `
  --paths src `
  --add-data "src\scriborium\resources;scriborium\resources" `
  --collect-data spellchecker `
  --hidden-import spylls.hunspell `
  src\scriborium\main_lite.py

if (-not (Test-Path '.\dist\Scriborium-Lite\Scriborium-Lite.exe')) {
  throw 'Build failed: dist\\Scriborium-Lite\\Scriborium-Lite.exe not found.'
}

$exe = Get-Item '.\dist\Scriborium-Lite\Scriborium-Lite.exe'
Write-Host "[build_lite_exe] OK: $($exe.FullName) ($([math]::Round($exe.Length / 1MB, 2)) MB)"
