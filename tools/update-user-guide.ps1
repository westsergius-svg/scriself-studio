$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
[xml]$versionProps = Get-Content (Join-Path $repo 'Directory.Build.props') -Raw
$version = $versionProps.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$topics = Get-Content (Join-Path $repo 'src/ScriSelfStudio.Presentation/Help/guide.json') -Raw | ConvertFrom-Json
$parts = [Collections.Generic.List[string]]::new()
$parts.Add("# Руководство ScriSelf Studio $version")
$parts.Add('Справка доступна в программе через F1 и меню «Справка». Это та же редакция руководства, что встроена в приложение.')
foreach ($topic in $topics) {
    $parts.Add("## $($topic.Title)")
    $parts.Add($topic.Summary)
    foreach ($block in $topic.Blocks) {
        $parts.Add("### $($block.Heading)")
        $parts.Add($block.Text)
    }
}
$parts -join ([Environment]::NewLine + [Environment]::NewLine) | Set-Content (Join-Path $repo 'docs/USER-GUIDE.md') -Encoding utf8

