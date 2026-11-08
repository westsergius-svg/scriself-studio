$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $repo 'licenses'
New-Item -ItemType Directory -Path $dest -Force | Out-Null
$pkg = Join-Path $env:USERPROFILE '.nuget/packages'
$records = [Collections.Generic.List[object]]::new()
function Save-Notice([string]$name, [string]$source) {
    $target = Join-Path $dest "$name.txt"
    if ($source.StartsWith('https://')) {
        $response = Invoke-WebRequest -Uri $source -TimeoutSec 30
        $body = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
    } else {
        $body = Get-Content -LiteralPath (Join-Path $pkg $source) -Raw
    }
    if ($body.Length -lt 50 -or $body -match '<html') { throw "Invalid notice: $source" }
    [IO.File]::WriteAllText($target, $body, [Text.UTF8Encoding]::new($false))
    $records.Add([pscustomobject]@{ File="$name.txt"; Source=$source; SHA256=(Get-FileHash $target -Algorithm SHA256).Hash })
    Write-Output "$name ($($body.Length) characters)"
}
Save-Notice 'Avalonia_MIT' 'https://raw.githubusercontent.com/AvaloniaUI/Avalonia/2d79ba55fae1b1e71c69f93a668080c9029e6a31/licence.md'
Save-Notice 'AvaloniaRichEditor_MIT' 'https://raw.githubusercontent.com/centwon/AvaloniaRichEditor/837242c5769407dbabd3b6c201409fb586d05905/LICENSE'
Save-Notice 'AvaloniaRichEditor_Notices' 'avaloniaricheditor/1.2.1/THIRD-PARTY-NOTICES.md'
Save-Notice 'ANGLE_BSD' 'avalonia.angle.windows.natives/2.1.27548.20260419/LICENSE'
Save-Notice 'CommunityToolkit_MIT' 'communitytoolkit.mvvm/8.4.2/License.md'
Save-Notice 'CommunityToolkit_Notices' 'communitytoolkit.mvvm/8.4.2/ThirdPartyNotices.txt'
Save-Notice 'OpenXML_MIT' 'https://raw.githubusercontent.com/dotnet/Open-XML-SDK/3139fdfd27414548a41555f7848d5728f6e71a42/LICENSE'
Save-Notice 'HtmlAgilityPack_MIT' 'https://raw.githubusercontent.com/zzzprojects/html-agility-pack/v1.12.4/LICENSE'
Save-Notice 'MicroCom_MIT' 'https://raw.githubusercontent.com/kekekeks/MicroCom/76785efcafd91b5902fd19dd11145f6dd655b7b4/LICENSE'
Save-Notice 'PDFsharp_MIT' 'https://raw.githubusercontent.com/empira/PDFsharp/v6.2.4/LICENSE'
Save-Notice 'Tmds_DBus_MIT' 'https://raw.githubusercontent.com/tmds/Tmds.DBus/b4a7fed0b878f74cb54f7cca84d2889af4e596ba/COPYING'
Save-Notice 'SQLitePCLRaw_Apache_2.0' 'https://raw.githubusercontent.com/ericsink/SQLitePCL.raw/v2.1.12/LICENSE.TXT'
Save-Notice 'SQLitePCLRaw_Notices' 'https://raw.githubusercontent.com/ericsink/SQLitePCL.raw/v2.1.12/NOTICE.TXT'
Save-Notice 'Inter_OFL' 'https://raw.githubusercontent.com/rsms/inter/v3.19/LICENSE.txt'
Save-Notice 'Microsoft_Extensions_MIT' 'microsoft.extensions.logging/8.0.1/LICENSE.TXT'
Save-Notice 'Microsoft_Extensions_Notices' 'microsoft.extensions.logging/8.0.1/THIRD-PARTY-NOTICES.TXT'
Save-Notice 'Microsoft_Data_Sqlite_MIT' 'https://raw.githubusercontent.com/dotnet/efcore/4caf9eeca38ab8bbc8857b975a8f0ad29ad2ea7f/LICENSE.txt'
Save-Notice 'SkiaSharp_MIT' 'skiasharp/3.119.4/LICENSE.txt'
Save-Notice 'SkiaSharp_Native_Notices' 'skiasharp.nativeassets.win32/3.119.4/THIRD-PARTY-NOTICES.txt'
Save-Notice 'HarfBuzzSharp_MIT' 'harfbuzzsharp/8.3.1.3/LICENSE.txt'
Save-Notice 'HarfBuzzSharp_Native_Notices' 'harfbuzzsharp.nativeassets.win32/8.3.1.3/THIRD-PARTY-NOTICES.txt'
Save-Notice 'NET_Runtime_MIT' 'microsoft.netcore.app.runtime.win-x64/10.0.12/LICENSE.TXT'
Save-Notice 'NET_Runtime_Notices' 'microsoft.netcore.app.runtime.win-x64/10.0.12/THIRD-PARTY-NOTICES.TXT'
Save-Notice 'System_Security_Cryptography_Pkcs_MIT' 'system.security.cryptography.pkcs/8.0.2/LICENSE.TXT'
Save-Notice 'System_Security_Cryptography_Pkcs_Notices' 'system.security.cryptography.pkcs/8.0.2/THIRD-PARTY-NOTICES.TXT'
Save-Notice 'System_IO_Packaging_Notices' 'system.io.packaging/10.0.2/THIRD-PARTY-NOTICES.TXT'
Save-Notice 'WeCantSpell_Hunspell_Notices' 'wecantspell.hunspell/7.0.1/license.txt'
Save-Notice 'Russian_spelling_BSD' 'https://raw.githubusercontent.com/LibreOffice/dictionaries/4f1df64b2e8e14751aa7c2205d170ca016071315/ru_RU/README_ru_RU.txt'
Save-Notice 'English_spelling_Notices' 'https://raw.githubusercontent.com/LibreOffice/dictionaries/4f1df64b2e8e14751aa7c2205d170ca016071315/en/README_en_US.txt'
Save-Notice 'MPL_1.1' 'https://www.mozilla.org/media/MPL/1.1/index.txt'
Save-Notice 'CC_BY_SA_4.0' 'https://creativecommons.org/licenses/by-sa/4.0/legalcode.txt'
Copy-Item -LiteralPath (Join-Path $repo 'src/ScriSelfStudio.Presentation/Assets/Fonts/FluentSystemIcons-LICENSE.txt') -Destination (Join-Path $dest 'Fluent_System_Icons_MIT.txt')
$records | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $dest 'sources.json') -Encoding utf8

