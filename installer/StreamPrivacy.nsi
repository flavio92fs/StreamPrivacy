; Offline, current-user installation. No downloader, telemetry or update check.
Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetDatablockOptimize on

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"

!ifndef APP_VERSION
  !define APP_VERSION "0.2.5"
!endif
!ifndef APP_DIR
  !error "Pass /DAPP_DIR=<built application directory>"
!endif
!ifndef REPO_DIR
  !define REPO_DIR ".."
!endif
!ifndef OUTPUT_FILE
  !define OUTPUT_FILE "StreamPrivacy-${APP_VERSION}-win-x64-setup.exe"
!endif

Name "Stream Privacy"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\StreamPrivacy"
!ifndef TEST_BUILD
  InstallDirRegKey HKCU "Software\StreamPrivacy" "InstallDir"
!endif
BrandingText "Stream Privacy - offline, no telemetry"
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "Stream Privacy"
VIAddVersionKey /LANG=1033 "FileDescription" "Stream Privacy offline installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright (c) 2026 Flavio"
!ifdef TEST_BUILD
  VIAddVersionKey /LANG=1033 "Comments" "Isolated installer test build"
!endif

!define MUI_ICON "${REPO_DIR}\assets\StreamPrivacy.ico"
!define MUI_UNICON "${REPO_DIR}\assets\StreamPrivacy.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "Install Stream Privacy for the current Windows user.$\r$\n$\r$\nThis installer is fully offline. It does not send data, check for updates, or download components.$\r$\n$\r$\nClose any running copy of Stream Privacy before continuing."
!define MUI_FINISHPAGE_RUN "$INSTDIR\StreamPrivacy.exe"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!define MUI_FINISHPAGE_RUN_TEXT "Open Stream Privacy"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${REPO_DIR}\LICENSE"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Italian"

LangString CloseApp ${LANG_ENGLISH} "Please close Stream Privacy from its tray icon (Exit and restore), then try again. No process will be terminated."
LangString CloseApp ${LANG_ITALIAN} "Chiudi Stream Privacy dal menu dell'icona (Esci e ripristina), poi riprova. Nessun processo verrà terminato."
LangString BadArchitecture ${LANG_ENGLISH} "This release supports x64 Windows only."
LangString BadArchitecture ${LANG_ITALIAN} "Questa versione supporta soltanto Windows x64."
LangString BadWindows ${LANG_ENGLISH} "Windows 10 version 2004 (build 19041) or newer is required."
LangString BadWindows ${LANG_ITALIAN} "È richiesto Windows 10 versione 2004 (build 19041) o successivo."
LangString PortableFolder ${LANG_ENGLISH} "This folder contains a portable copy. Choose a separate folder so its local settings remain untouched."
LangString PortableFolder ${LANG_ITALIAN} "Questa cartella contiene una copia portable. Scegli una cartella diversa per conservarne le impostazioni locali."
LangString MissingMetadata ${LANG_ENGLISH} "The installation metadata is missing or does not match this folder. Uninstallation was stopped to avoid removing unrelated files."
LangString MissingMetadata ${LANG_ITALIAN} "I dati di installazione sono mancanti o non corrispondono a questa cartella. La disinstallazione è stata fermata per non rimuovere file estranei."

Function .onInit
  SetShellVarContext current
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "$(BadArchitecture)" /SD IDOK
    SetErrorLevel 1633
    Abort
  ${EndIf}
  SetRegView 64
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_OK|MB_ICONSTOP "$(BadWindows)" /SD IDOK
    SetErrorLevel 1633
    Abort
  ${EndIf}
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows NT\CurrentVersion" "CurrentBuildNumber"
  ${If} $0 < 19041
    MessageBox MB_OK|MB_ICONSTOP "$(BadWindows)" /SD IDOK
    SetErrorLevel 1633
    Abort
  ${EndIf}
  !insertmacro MUI_LANGDLL_DISPLAY
FunctionEnd

; Opening with write access and no sharing is a non-mutating file-lock probe.
; OPEN_EXISTING means this never creates, truncates, deletes or modifies a file.
!macro CheckFileAvailable PATH
  ${If} ${FileExists} "${PATH}"
    System::Call 'kernel32::CreateFileW(w "${PATH}", i 0x40000000, i 0, p 0, i 3, i 0, p 0) p.r0'
    ${If} $0 == -1
      MessageBox MB_OK|MB_ICONEXCLAMATION "$(CloseApp)" /SD IDOK
      SetErrorLevel 1618
      Abort
    ${EndIf}
    System::Call 'kernel32::CloseHandle(p r0)'
  ${EndIf}
!macroend

Section "Stream Privacy (required)" MainSection
  SectionIn RO
  SetShellVarContext current
  ${If} ${FileExists} "$INSTDIR\portable.flag"
    MessageBox MB_OK|MB_ICONSTOP "$(PortableFolder)" /SD IDOK
    SetErrorLevel 1639
    Abort
  ${EndIf}
  !insertmacro CheckFileAvailable "$INSTDIR\StreamPrivacy.exe"
  !insertmacro CheckFileAvailable "$INSTDIR\PrivacyHook64.dll"
  SetOutPath "$INSTDIR"
  SetOverwrite on
  File "${APP_DIR}\StreamPrivacy.exe"
  File "${APP_DIR}\PrivacyHook64.dll"
  File "${REPO_DIR}\LICENSE"
  File "${REPO_DIR}\README.md"
  File "${REPO_DIR}\README.it.md"
  File "${REPO_DIR}\PRIVACY.md"
  File "${REPO_DIR}\THIRD_PARTY_NOTICES.txt"
  SetOutPath "$INSTDIR\assets"
  File "${REPO_DIR}\assets\StreamPrivacy.ico"
  File "${REPO_DIR}\assets\StreamPrivacy.png"
  SetOutPath "$INSTDIR"
  WriteINIStr "$INSTDIR\StreamPrivacy.install.ini" "StreamPrivacy" "Product" "StreamPrivacy"
  WriteINIStr "$INSTDIR\StreamPrivacy.install.ini" "StreamPrivacy" "Version" "${APP_VERSION}"
  WriteINIStr "$INSTDIR\StreamPrivacy.install.ini" "StreamPrivacy" "InstallPath" "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  !ifdef TEST_BUILD
    ; Test builds isolate shortcuts and do not read/write registry registration.
    CreateDirectory "$INSTDIR\TestShortcuts"
    CreateShortcut "$INSTDIR\TestShortcuts\Stream Privacy.lnk" "$INSTDIR\StreamPrivacy.exe"
  !else
    WriteRegStr HKCU "Software\StreamPrivacy" "InstallDir" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamPrivacy" "DisplayName" "Stream Privacy"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamPrivacy" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamPrivacy" "Publisher" "Flavio"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamPrivacy" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamPrivacy" "DisplayIcon" "$INSTDIR\StreamPrivacy.exe,0"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamPrivacy" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamPrivacy" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamPrivacy" "NoModify" 1
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamPrivacy" "NoRepair" 1
    CreateDirectory "$SMPROGRAMS\Stream Privacy"
    CreateShortcut "$SMPROGRAMS\Stream Privacy\Stream Privacy.lnk" "$INSTDIR\StreamPrivacy.exe"
    CreateShortcut "$SMPROGRAMS\Stream Privacy\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  !endif
SectionEnd

Section /o "Desktop shortcut" DesktopSection
  !ifndef TEST_BUILD
    CreateShortcut "$DESKTOP\Stream Privacy.lnk" "$INSTDIR\StreamPrivacy.exe"
  !else
    CreateShortcut "$INSTDIR\TestShortcuts\Desktop - Stream Privacy.lnk" "$INSTDIR\StreamPrivacy.exe"
  !endif
SectionEnd

Function un.onInit
  SetShellVarContext current
  SetRegView 64
  ReadINIStr $0 "$INSTDIR\StreamPrivacy.install.ini" "StreamPrivacy" "Product"
  ReadINIStr $1 "$INSTDIR\StreamPrivacy.install.ini" "StreamPrivacy" "InstallPath"
  ${If} $0 != "StreamPrivacy"
  ${OrIf} $1 != $INSTDIR
    MessageBox MB_OK|MB_ICONSTOP "$(MissingMetadata)" /SD IDOK
    SetErrorLevel 1605
    Abort
  ${EndIf}
  !insertmacro CheckFileAvailable "$INSTDIR\StreamPrivacy.exe"
  !insertmacro CheckFileAvailable "$INSTDIR\PrivacyHook64.dll"
FunctionEnd

Section "Uninstall"
  SetShellVarContext current
  !ifndef TEST_BUILD
    ; Only remove the startup registration when it points to this exact copy.
    ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "StreamPrivacy"
    StrCmp $0 '$\"$INSTDIR\StreamPrivacy.exe$\" --tray' 0 +2
      DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "StreamPrivacy"
    ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamPrivacy" "InstallLocation"
    ${If} $0 == $INSTDIR
      DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\StreamPrivacy"
      Delete "$SMPROGRAMS\Stream Privacy\Stream Privacy.lnk"
      Delete "$SMPROGRAMS\Stream Privacy\Uninstall.lnk"
      RMDir "$SMPROGRAMS\Stream Privacy"
      Delete "$DESKTOP\Stream Privacy.lnk"
    ${EndIf}
    ReadRegStr $0 HKCU "Software\StreamPrivacy" "InstallDir"
    ${If} $0 == $INSTDIR
      DeleteRegValue HKCU "Software\StreamPrivacy" "InstallDir"
      DeleteRegKey /ifempty HKCU "Software\StreamPrivacy"
    ${EndIf}
  !else
    Delete "$INSTDIR\TestShortcuts\Stream Privacy.lnk"
    Delete "$INSTDIR\TestShortcuts\Desktop - Stream Privacy.lnk"
    RMDir "$INSTDIR\TestShortcuts"
  !endif
  ; No recursive removal: unknown files and all user settings are preserved.
  Delete "$INSTDIR\StreamPrivacy.exe"
  Delete "$INSTDIR\PrivacyHook64.dll"
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\README.it.md"
  Delete "$INSTDIR\PRIVACY.md"
  Delete "$INSTDIR\THIRD_PARTY_NOTICES.txt"
  Delete "$INSTDIR\assets\StreamPrivacy.ico"
  Delete "$INSTDIR\assets\StreamPrivacy.png"
  RMDir "$INSTDIR\assets"
  Delete "$INSTDIR\StreamPrivacy.install.ini"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
