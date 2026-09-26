$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

Write-Host '[build_exe] Cleaning previous build artifacts...'
if (Test-Path '.\build') { Remove-Item -Recurse -Force '.\build' }
if (Test-Path '.\dist\Scriborium') { Remove-Item -Recurse -Force '.\dist\Scriborium' -ErrorAction SilentlyContinue }
if (Test-Path '.\Scriborium.spec') { Remove-Item -Force '.\Scriborium.spec' }

Write-Host '[build_exe] Sanitizing PATH (removing Codex runtime native dirs that pollute PyInstaller DLL scan)...'
$env:Path = ($env:Path -split ';' | Where-Object { $_ -notmatch 'codex-runtimes' }) -join ';'

Write-Host '[build_exe] Locating reportlab fonts for PDF Cyrillic export...'
$ReportLabFonts = python -c "import reportlab, os; print(os.path.join(os.path.dirname(reportlab.__file__), 'fonts'))"
if (-not $ReportLabFonts -or -not (Test-Path $ReportLabFonts)) {
  Write-Host '[build_exe] WARNING: reportlab fonts not found, PDF Cyrillic may be limited.'
  $ReportLabFonts = 'nonesuch'
}

Write-Host '[build_exe] Building application with PyInstaller...'
python -m PyInstaller `
  --noconfirm `
  --clean `
  --windowed `
  --name Scriborium `
  --icon "src\scriborium\resources\icons\app.ico" `
  --paths src `
  --collect-data spellchecker `
  --hidden-import spylls.hunspell `
  --hidden-import reportlab `
  --add-data "src\scriborium\resources;scriborium\resources" `
  --add-data "$ReportLabFonts;reportlab\fonts" `
  src\scriborium\main.py

if (-not (Test-Path '.\dist\Scriborium\Scriborium.exe')) {
  throw 'Build failed: dist\Scriborium\Scriborium.exe not found.'
}

$exe = Get-Item '.\dist\Scriborium\Scriborium.exe'
Write-Host "[build_exe] OK: $($exe.FullName) ($([math]::Round($exe.Length / 1MB, 2)) MB)"
