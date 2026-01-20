; MultiFlash TOOL 精简版安装脚本 (不含资源包)
; 使用 Inno Setup 编译: https://jrsoftware.org/isinfo.php

#define MyAppName "MultiFlash TOOL"
#define MyAppVersion "2.2.0"
#define MyAppPublisher "xiri"
#define MyAppURL "https://github.com/xiriovo/edltool"
#define MyAppExeName "MultiFlash.exe"

[Setup]
AppId={{A578AA98-3A86-41DC-8F5B-E9B54EBE1AE6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion} Lite
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; 输出设置
OutputDir=installer
OutputBaseFilename=MultiFlash_Setup_v{#MyAppVersion}_Lite
SetupIconFile=MultiFlash TOOL.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; 最大压缩
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMANumBlockThreads=4

PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
DisableWelcomePage=no

VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Lite 安装程序
VersionInfoCopyright=Copyright © xiri 2025-2026

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; 主程序
Source: "bin\Release\MultiFlash.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\MultiFlash.exe.config"; DestDir: "{app}"; Flags: ignoreversion

; UI 库
Source: "bin\Release\AntdUI.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\SunnyUI.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\SunnyUI.Common.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\HandyControl.dll"; DestDir: "{app}"; Flags: ignoreversion

; 图标
Source: "MultiFlash TOOL.ico"; DestDir: "{app}"; Flags: ignoreversion

; 注意: 精简版不包含 edl_loaders.pak 和 firehose.pak

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\MultiFlash TOOL.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\MultiFlash TOOL.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNetInstalled(): Boolean;
var
  Release: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
    Result := (Release >= 528040);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNetInstalled() then
  begin
    if MsgBox('此程序需要 .NET Framework 4.8' + #13#10 + '是否继续安装？', mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"
Type: files; Name: "{app}\*.log"
