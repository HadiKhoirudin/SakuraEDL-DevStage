; MultiFlash TOOL 安装脚本
; 使用 Inno Setup 编译: https://jrsoftware.org/isinfo.php

#define MyAppName "MultiFlash TOOL"
#define MyAppVersion "2.2.0"
#define MyAppPublisher "xiri"
#define MyAppURL "https://github.com/xiriovo/edltool"
#define MyAppExeName "MultiFlash.exe"

[Setup]
; 应用程序标识 (生成新的 GUID)
AppId={{A578AA98-3A86-41DC-8F5B-E9B54EBE1AE6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; 安装目录
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; 输出设置
OutputDir=installer
OutputBaseFilename=MultiFlash_Setup_v{#MyAppVersion}
SetupIconFile=MultiFlash TOOL.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; 压缩设置
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; 权限设置
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

; 界面设置
WizardStyle=modern
WizardSizePercent=100
DisableWelcomePage=no

; 版本信息
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 安装程序
VersionInfoCopyright=Copyright © xiri 2025-2026

; 语言设置
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
; 主程序
Source: "bin\Release\MultiFlash.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\MultiFlash.exe.config"; DestDir: "{app}"; Flags: ignoreversion

; UI 库
Source: "bin\Release\AntdUI.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\SunnyUI.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\SunnyUI.Common.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\HandyControl.dll"; DestDir: "{app}"; Flags: ignoreversion

; 资源包 (可选，如果存在则打包)
Source: "bin\Release\edl_loaders.pak"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "bin\Release\firehose.pak"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; 图标
Source: "MultiFlash TOOL.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\MultiFlash TOOL.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\MultiFlash TOOL.ico"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// 检查 .NET Framework 4.8 是否已安装
function IsDotNetInstalled(): Boolean;
var
  Release: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
  begin
    // .NET Framework 4.8 的 Release 值为 528040 或更高
    Result := (Release >= 528040);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  
  if not IsDotNetInstalled() then
  begin
    if MsgBox('此程序需要 .NET Framework 4.8 运行环境。' + #13#10 + #13#10 +
              '是否继续安装？（安装后请确保已安装 .NET Framework 4.8）', 
              mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
    end;
  end;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"
Type: files; Name: "{app}\*.log"
