<#
.SYNOPSIS
    Собирает значки приложения из исходной картинки.

.DESCRIPTION
    Из одного растрового источника делает:
      * src/ScriSelfStudio.App/Assets/scriself.ico — многоразмерный значок для exe,
        ярлыков и установщика (подключается как ApplicationIcon);
      * src/ScriSelfStudio.App/Assets/scriself.png — значок окна для Avalonia
        (ресурс avares, потому что ICO она не декодирует).

    Размеры ICO: 16, 24, 32, 48, 64, 128, 256. Кадры кладутся в формате PNG —
    так Windows Vista и новее читает значок без дополнительной упаковки BMP.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Source,

    [string] $IcoPath = (Join-Path $PSScriptRoot '..\src\ScriSelfStudio.App\Assets\scriself.ico'),

    [string] $PngPath = (Join-Path $PSScriptRoot '..\src\ScriSelfStudio.App\Assets\scriself.png')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256

# Имя локальной переменной не должно совпадать с параметром $Source:
# у параметра есть ограничение типа, и картинка превратилась бы в строку.
$image = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Source).Path)
try {
    $frames = foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.DrawImage($image, 0, 0, $size, $size)
        }
        finally {
            $graphics.Dispose()
        }

        $stream = New-Object System.IO.MemoryStream
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
        , $stream.ToArray()
    }
}
finally {
    $image.Dispose()
}

# --- PNG для окна: старший кадр отдельным файлом ---
$pngDirectory = Split-Path -Parent $PngPath
if (-not (Test-Path -LiteralPath $pngDirectory)) {
    New-Item -ItemType Directory -Force -Path $pngDirectory | Out-Null
}

[System.IO.File]::WriteAllBytes((Join-Path (Resolve-Path -LiteralPath $pngDirectory).Path (Split-Path -Leaf $PngPath)), $frames[-1])

# --- Многоразмерный ICO: заголовок, таблица кадров, данные кадров ---
$icoDirectory = Split-Path -Parent $IcoPath
if (-not (Test-Path -LiteralPath $icoDirectory)) {
    New-Item -ItemType Directory -Force -Path $icoDirectory | Out-Null
}

$iconStream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($iconStream)
try {
    $writer.Write([uint16]0)                 # reserved
    $writer.Write([uint16]1)                 # type: icon
    $writer.Write([uint16]$sizes.Count)      # image count

    $offset = 6 + (16 * $sizes.Count)
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
        $payload = $frames[$index]

        # 256 в ICO кодируется нулём: поле ширины/высоты однобайтовое.
        $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
        $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
        $writer.Write([byte]0)               # палитра не используется
        $writer.Write([byte]0)               # reserved
        $writer.Write([uint16]1)             # цветовых плоскостей
        $writer.Write([uint16]32)            # бит на пиксель
        $writer.Write([uint32]$payload.Length)
        $writer.Write([uint32]$offset)

        $offset += $payload.Length
    }

    foreach ($payload in $frames) {
        $writer.Write($payload)
    }
}
finally {
    $writer.Dispose()
}

[System.IO.File]::WriteAllBytes((Join-Path (Resolve-Path -LiteralPath $icoDirectory).Path (Split-Path -Leaf $IcoPath)), $iconStream.ToArray())
$iconStream.Dispose()

Write-Host "ICO: $IcoPath ($([System.IO.File]::ReadAllBytes($IcoPath).Length) байт)"
Write-Host "PNG: $PngPath ($([System.IO.File]::ReadAllBytes($PngPath).Length) байт)"
