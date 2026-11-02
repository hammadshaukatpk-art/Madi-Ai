Unicode True
!include "MUI2.nsh"
Name "Madi AI"
OutFile "Madi-AI-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\MadiAI"
RequestExecutionLevel user
!define MUI_BGCOLOR "050B16"
!define MUI_TEXTCOLOR "DFF7FF"
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
Section "Madi AI"
 SetOutPath "$INSTDIR"
 File /r "..\publish\*"
 WriteUninstaller "$INSTDIR\Uninstall Madi AI.exe"
 CreateShortCut "$DESKTOP\Madi AI.lnk" "$INSTDIR\MadiAI.exe"
 CreateDirectory "$SMPROGRAMS\Madi AI"
 CreateShortCut "$SMPROGRAMS\Madi AI\Madi AI.lnk" "$INSTDIR\MadiAI.exe"
 CreateShortCut "$SMPROGRAMS\Madi AI\Uninstall.lnk" "$INSTDIR\Uninstall Madi AI.exe"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MadiAI" "DisplayName" "Madi AI"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MadiAI" "UninstallString" '"$INSTDIR\Uninstall Madi AI.exe"'
SectionEnd
Section "Uninstall"
 Delete "$DESKTOP\Madi AI.lnk"
 RMDir /r "$SMPROGRAMS\Madi AI"
 RMDir /r "$INSTDIR"
 DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MadiAI"
SectionEnd
