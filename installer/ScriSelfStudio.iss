; Установщик ScriSelf Studio (Inno Setup 6).
;
; Собирает один Setup.exe: кладёт self-contained публикацию в Program Files,
; создаёт ярлыки, регистрирует ассоциацию файлов .scri и значок для них.
;
; Собирается одной командой: powershell -File installer\build-installer.ps1
; Она сама читает версию из Directory.Build.props, публикует приложение и передаёт
; сюда /DAppVersion и /DPublishDir. Значения ниже — запасные, для ручного запуска ISCC.

#define AppName "ScriSelf Studio"

#ifndef AppVersion
  #define AppVersion "1.1.9"
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64-1.1.9-installer"
#endif

#define AppPublisher "ScriSelf Studio"
#define AppExeName "ScriSelfStudio.exe"
#define ProjectExt ".scri"
#define ProgId "ScriSelfStudio.Project"
#define IconFile "..\src\ScriSelfStudio.App\Assets\scriself.ico"

[Setup]
AppId={{8E4E1B54-6C1E-4C4B-9E9C-2A7F3B0D5A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=ScriSelfStudio-Setup-{#AppVersion}
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Ассоциация файлов требует записи в HKCR: сообщаем оболочке об изменениях.
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Значки:"; Flags: unchecked
Name: "associate"; Description: "Открывать файлы {#ProjectExt} в {#AppName}"; GroupDescription: "Ассоциация файлов:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Значок ассоциации ставим рядом с программой: путь к нему не должен меняться.
Source: "{#IconFile}"; DestDir: "{app}"; DestName: "scriself.ico"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Удаление {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Программа в «Приложениях по умолчанию» и в App Paths: путь к exe известен оболочке.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#AppExeName}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#AppExeName}"; ValueType: string; ValueName: "Path"; ValueData: "{app}"
Root: HKLM; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#AppName}"; ValueData: "Software\{#AppName}\Capabilities"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\{#AppName}\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\{#AppName}\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Редактор книг и сценариев ScriSelf Studio"
Root: HKLM; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: "{#ProjectExt}"; ValueData: "{#ProgId}"

; Ассоциация .scri: тип файла, значок и команда открытия.
Root: HKCR; Subkey: "{#ProjectExt}"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue; Tasks: associate
Root: HKCR; Subkey: "{#ProjectExt}\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate
Root: HKCR; Subkey: "{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "Проект ScriSelf Studio"; Flags: uninsdeletekey; Tasks: associate
Root: HKCR; Subkey: "{#ProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\scriself.ico"; Tasks: associate
Root: HKCR; Subkey: "{#ProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: associate

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent
