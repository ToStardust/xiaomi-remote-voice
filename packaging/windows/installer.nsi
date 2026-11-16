Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!define PRODUCT_NAME "小米遥控器语音助手（非官方）"
!define PRODUCT_VERSION "0.1.0"
!define PRODUCT_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\XiaomiRemoteVoice"
Name "${PRODUCT_NAME}"
!ifdef TEST_BUILD
  OutFile "..\构建\test-setup.exe"
  RequestExecutionLevel user
!else
  OutFile "..\..\dist\小米遥控器语音助手-Setup-0.1.0-x64.exe"
  RequestExecutionLevel admin
!endif
InstallDir "$PROGRAMFILES64\Xiaomi Remote Voice"
InstallDirRegKey HKLM "${PRODUCT_KEY}" "InstallLocation"
!ifdef TEST_BUILD
  SetCompressor /SOLID zlib
!else
  SetCompressor /SOLID lzma
!endif
ShowInstDetails show
ShowUninstDetails show
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "安装小米遥控器语音助手"
!define MUI_WELCOMEPAGE_TEXT "这是基于 VibeCoding Voice 的非官方适配版。$\r$\n$\r$\n适用于 Windows x64 和第一代小米蓝牙语音遥控器。首次安装需管理员权限；安装 USBPcap 后可能需要手动重启。$\r$\n$\r$\n请先关闭其他正在使用遥控器的程序。"
!define MUI_FINISHPAGE_TEXT "安装完成。请从桌面启动“小米遥控器语音助手”，并跟随首次配置引导完成配对和语音设置。$\r$\n$\r$\nUSBPcap 安装后如提示重启，请自行重启电脑。"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\构建\payload\app\LICENSE-VibeCoding-Voice.txt"
!insertmacro MUI_PAGE_COMPONENTS
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE DirectoryLeave
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "此安装包需要 64 位 Windows。" /SD IDOK
    Abort
  ${EndIf}
  SetRegView 64
FunctionEnd

Function DirectoryLeave
  Call ValidateInstallDirectory
FunctionEnd

Function ValidateInstallDirectory
    IfFileExists "$INSTDIR\.xiaomi-remote-voice" done 0
    ClearErrors
    FindFirst $0 $1 "$INSTDIR\*"
    IfErrors done
  checkEntry:
    StrCmp $1 "." nextEntry
    StrCmp $1 ".." nextEntry
    StrCmp $1 "" closeSearch
    FindClose $0
    MessageBox MB_ICONSTOP "所选目录不是空目录。请选择一个新的空安装目录，以免覆盖已有文件。" /SD IDOK
    Abort
  nextEntry:
    ClearErrors
    FindNext $0 $1
    IfErrors closeSearch
    Goto checkEntry
  closeSearch:
    FindClose $0
  done:
  FunctionEnd

Section "主程序和遥控器功能（必需）" SEC_APP
  SectionIn RO
  Call ValidateInstallDirectory
  SetOutPath "$INSTDIR"
  File /r "..\构建\payload\app\*"
!ifndef TEST_BUILD
  FileOpen $0 "$INSTDIR\.xiaomi-remote-voice" w
  FileWrite $0 "Xiaomi Remote Voice 0.1.0"
  FileClose $0
!endif
SectionEnd

Section "USBPcap 驱动（首次安装需要）" SEC_USB
!ifndef TEST_BUILD
  IfFileExists "$PROGRAMFILES64\USBPcap\USBPcapCMD.exe" doneUsb 0
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File /oname=USBPcapSetup.exe "..\构建\payload\drivers\USBPcapSetup-1.5.4.0.exe"
  DetailPrint "请在 USBPcap 官方安装向导中完成安装；如需重启，请手动重启。"
  ExecWait '"$PLUGINSDIR\USBPcapSetup.exe"' $0
  IfFileExists "$PROGRAMFILES64\USBPcap\USBPcapCMD.exe" doneUsb 0
  MessageBox MB_ICONEXCLAMATION "USBPcap 尚未安装完成。程序可以安装，但遥控器功能需要先安装 USBPcap，并按提示重启电脑。" /SD IDOK
doneUsb:
!endif
SectionEnd

Section /o "安装 VB-CABLE（已安装请勿勾选）" SEC_CABLE
!ifndef TEST_BUILD
  DetailPrint "请在 VB-Audio 的安装窗口中选择安装。VB-CABLE 为 Donationware，欢迎到 vb-audio.com 支持作者。"
  SetOutPath "$INSTDIR\dependencies\VB-CABLE"
  ExecWait '"$INSTDIR\dependencies\VB-CABLE\VBCABLE_Setup_x64.exe"' $0
!endif
SectionEnd

Section -Finalize
!ifndef TEST_BUILD
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\resources\install-share-broker.ps1" -InstallRoot "$INSTDIR"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "遥控器后台服务注册失败。请以管理员身份重新运行安装程序。$\r$\n$1" /SD IDOK
    Abort
  ${EndIf}
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "${PRODUCT_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKLM "${PRODUCT_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKLM "${PRODUCT_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${PRODUCT_KEY}" "Publisher" "非官方适配 · 基于 VibeCoding Voice"
  WriteRegStr HKLM "${PRODUCT_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKLM "${PRODUCT_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${PRODUCT_KEY}" "NoRepair" 1
  SetShellVarContext all
  CreateDirectory "$SMPROGRAMS\小米遥控器语音助手"
  CreateShortcut "$SMPROGRAMS\小米遥控器语音助手\小米遥控器语音助手.lnk" "$INSTDIR\VibeCoding Voice.exe"
  CreateShortcut "$SMPROGRAMS\小米遥控器语音助手\卸载.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\小米遥控器语音助手.lnk" "$INSTDIR\VibeCoding Voice.exe"
!endif
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_APP} "安装主程序、鼠标模式和输入框聚焦功能。"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_USB} "运行 USBPcap 官方安装程序；已安装时自动跳过。完成后可能需要重启。"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_CABLE} "可选：运行 VB-Audio 官方 VB-CABLE 安装程序。它是 Donationware，欢迎支持作者。"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\resources\uninstall-share-broker.ps1" -InstallRoot "$INSTDIR"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "无法安全移除本程序的后台服务。请关闭程序并以管理员身份重试。$\r$\n$1" /SD IDOK
    Abort
  ${EndIf}
  SetShellVarContext all
  Delete "$DESKTOP\小米遥控器语音助手.lnk"
  Delete "$SMPROGRAMS\小米遥控器语音助手\小米遥控器语音助手.lnk"
  Delete "$SMPROGRAMS\小米遥控器语音助手\卸载.lnk"
  RMDir "$SMPROGRAMS\小米遥控器语音助手"
  DeleteRegKey HKLM "${PRODUCT_KEY}"
  Delete "$INSTDIR\.xiaomi-remote-voice"
  !include "..\构建\uninstall-files.nsh"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
