!include "MUI2.nsh"

!define APP_NAME "Scriborium"
!define APP_VERSION "1.1.3.3S"
!define APP_VERSION_LABEL "Beta 1.1.3.3S"
!define APP_PUBLISHER "Scriborium Team"
!define APP_EXE "Scriborium.exe"
!define APP_ICON "${BUILD_ROOT}\\src\\scriborium\\resources\\icons\\app.ico"

!ifndef BUILD_ROOT
  !define BUILD_ROOT "."
!endif

Name "${APP_NAME} ${APP_VERSION_LABEL}"
OutFile "${BUILD_ROOT}\\dist\\Scriborium-Setup-${APP_VERSION_LABEL}.exe"
; Установка в профиль пользователя — без прав администратора
InstallDir "$LOCALAPPDATA\\Programs\\${APP_NAME}"
InstallDirRegKey HKCU "Software\\${APP_NAME}" "InstallDir"
RequestExecutionLevel user
Icon "${APP_ICON}"
UninstallIcon "${APP_ICON}"
!define MUI_ICON "${APP_ICON}"
!define MUI_UNICON "${APP_ICON}"

!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "Install" SEC_MAIN
  SetOutPath "$INSTDIR"
  File /r "${BUILD_ROOT}\\dist\\Scriborium\\*.*"

  WriteUninstaller "$INSTDIR\\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\\${APP_NAME}"
  CreateShortCut "$SMPROGRAMS\\${APP_NAME}\\${APP_NAME}.lnk" "$INSTDIR\\${APP_EXE}"
  CreateShortCut "$SMPROGRAMS\\${APP_NAME}\\Uninstall.lnk" "$INSTDIR\\Uninstall.exe"
  CreateShortCut "$DESKTOP\\${APP_NAME}.lnk" "$INSTDIR\\${APP_EXE}"

  WriteRegStr HKCU "Software\\${APP_NAME}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "DisplayName" "${APP_NAME} ${APP_VERSION_LABEL}"
  WriteRegStr HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "UninstallString" "$\"$INSTDIR\\Uninstall.exe$\""
  WriteRegDWORD HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "NoModify" 1
  WriteRegDWORD HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\\${APP_NAME}\\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\\${APP_NAME}\\Uninstall.lnk"
  RMDir "$SMPROGRAMS\\${APP_NAME}"

  Delete "$INSTDIR\\Uninstall.exe"
  RMDir /r "$INSTDIR"

  DeleteRegKey HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}"
  DeleteRegKey HKCU "Software\\${APP_NAME}"
SectionEnd
