$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
[xml]$versionProps = Get-Content (Join-Path $repo 'Directory.Build.props') -Raw
$version = $versionProps.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$deps = Get-Content (Join-Path $repo 'src/ScriSelfStudio.App/bin/Release/net10.0/ScriSelfStudio.deps.json') -Raw | ConvertFrom-Json
$nl = [Environment]::NewLine
$lines = [Collections.Generic.List[string]]::new()
$lines.Add("Компоненты ScriSelf Studio $version$nl$nl Инвентаризация Release-зависимостей. Тестовые и отладочные инструменты не входят в список. Пакеты без собственных библиотек могут связывать другие компоненты. Нативные файлы выбираются под платформу; установщик этой сборки — Windows x64.")
$map = @{
    'Avalonia*' = 'Avalonia_MIT.txt'
    'Avalonia.Angle.Windows.Natives' = 'ANGLE_BSD.txt'
    'Avalonia.Fonts.Inter' = 'Avalonia_MIT.txt; Inter_OFL.txt'
    'AvaloniaRichEditor' = 'AvaloniaRichEditor_MIT.txt; AvaloniaRichEditor_Notices.txt'
    'CommunityToolkit.Mvvm' = 'CommunityToolkit_MIT.txt; CommunityToolkit_Notices.txt'
    'DocumentFormat.OpenXml*' = 'OpenXML_MIT.txt'
    'HarfBuzzSharp*' = 'HarfBuzzSharp_MIT.txt; HarfBuzzSharp_Native_Notices.txt'
    'HtmlAgilityPack' = 'HtmlAgilityPack_MIT.txt'
    'MicroCom.Runtime' = 'MicroCom_MIT.txt'
    'Microsoft.Data.Sqlite*' = 'Microsoft_Data_Sqlite_MIT.txt'
    'Microsoft.Extensions.*' = 'Microsoft_Extensions_MIT.txt; Microsoft_Extensions_Notices.txt'
    'PDFsharp' = 'PDFsharp_MIT.txt'
    'SkiaSharp*' = 'SkiaSharp_MIT.txt; SkiaSharp_Native_Notices.txt'
    'SQLitePCLRaw.*' = 'SQLitePCLRaw_Apache_2.0.txt; SQLitePCLRaw_Notices.txt'
    'System.IO.Packaging' = 'NET_Runtime_MIT.txt; System_IO_Packaging_Notices.txt'
    'System.Security.Cryptography.Pkcs' = 'System_Security_Cryptography_Pkcs_MIT.txt; System_Security_Cryptography_Pkcs_Notices.txt'
    'Tmds.DBus.Protocol' = 'Tmds_DBus_MIT.txt'
    'WeCantSpell.Hunspell' = 'WeCantSpell_Hunspell_Notices.txt; MPL_1.1.txt'
}
foreach ($entry in $deps.libraries.PSObject.Properties | Sort-Object Name) {
    if ($entry.Value.type -ne 'package') { continue }
    $name = $entry.Name.Split('/')[0]
    if ($name -in @('Avalonia.BuildServices', 'Microsoft.NET.ILLink.Tasks')) { continue }
    $key = if ($map.ContainsKey($name)) { $name } else { $map.Keys | Where-Object { $name -like $_ } | Select-Object -First 1 }
    if (!$key) { throw "Missing license mapping: $name" }
    foreach ($file in $map[$key].Split(';').Trim()) {
        if (!(Test-Path -LiteralPath (Join-Path $repo "licenses/$file"))) { throw "Missing notice: $file" }
    }
    $lines.Add("$($entry.Name)$nl  $($map[$key])")
}
$lines.Add("Дополнительно в поставке:$nl.NET Runtime 10.0.12 — NET_Runtime_MIT.txt; NET_Runtime_Notices.txt$($nl)Inter — Inter_OFL.txt (OFL 1.1, отдельно от MIT оболочки Avalonia.Fonts.Inter)$($nl)ScriSelf Symbols — подмножество Fluent System Icons; Fluent_System_Icons_MIT.txt. Исходная ревизия 463d58b8186f874ab2f51cf0a08b4ff5097c912f; шрифт переименован, коды символов изменены.")
$lines.Add("SQLite: отказ от авторских прав на ядро воспроизведён в SQLitePCLRaw_Notices.txt. Обёртка SQLitePCLRaw распространяется по Apache-2.0.")
$lines.Add("AvaloniaRichEditor 1.2.1 (локальное расширение ScriSelf): AvaloniaRichEditor_MIT.txt; AvaloniaRichEditor_Notices.txt. Исходники и описание изменений находятся в vendor/AvaloniaRichEditor.")
$lines.Add("Словари русского и английского языка: Russian_spelling_BSD.txt; English_spelling_Notices.txt. Синонимы wiki-ru-wordnet: Wiki_ru_wordnet_Notices.txt; CC_BY_SA_4.0.txt.")
$lines.Add("Локальный модуль грамматики (включается при сборке с -IncludeGrammarRuntime, Windows x64): LanguageTool 6.6 — LanguageTool.txt (LGPL 2.1+); Eclipse Temurin JRE 21.0.13+9 — Temurin_Java.txt (GPLv2 с Classpath Exception). Исходные COPYING, third-party-licenses, JRE legal/ и NOTICE сохранены внутри Grammar/. Источники и SHA256 архивов — tools/prepare-grammar-runtime.ps1 и Grammar/packages.json.")
$lines.Add("Первичные источники и контрольные суммы: sources.json. Полные тексты уведомлений поставляются без сокращений.")
$lines -join "$nl$nl" | Set-Content (Join-Path $repo 'licenses/01_Component_inventory.txt') -Encoding utf8

