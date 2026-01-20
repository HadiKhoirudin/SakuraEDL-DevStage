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
Name: "chinese"; MessagesFile: "compiler:Default.isl"

[Messages]
; 中文界面
SetupWindowTitle=安装 - %1
; 选择安装位置页面
WizardSelectDir=选择安装位置
SelectDirDesc=您想将 [name] 安装到哪里？
WelcomeLabel1=欢迎使用 [name] 安装向导
WelcomeLabel2=本向导将引导您完成 [name/ver] 的安装。%n%n建议您在继续之前关闭所有其他应用程序。
SelectDirLabel3=安装程序将把 [name] 安装到以下文件夹。
SelectDirBrowseLabel=若要继续，请单击"下一步"。若要选择其他文件夹，请单击"浏览"。
DiskSpaceGBLabel=至少需要 [gb] GB 的可用磁盘空间。
DiskSpaceMBLabel=至少需要 [mb] MB 的可用磁盘空间。
SelectTasksLabel2=选择安装程序要执行的附加任务，然后单击"下一步"继续。
ReadyLabel1=安装程序已准备好开始安装 [name]。
ReadyLabel2a=单击"安装"继续安装，或单击"上一步"检查或更改设置。
PreparingDesc=安装程序正在准备安装 [name]...
InstallingLabel=请稍候，安装程序正在安装 [name]...
FinishedHeadingLabel=完成 [name] 安装向导
FinishedLabel=安装程序已在您的计算机上安装了 [name]。您可以通过已创建的快捷方式启动该应用程序。
ClickFinish=单击"完成"退出安装向导。
ButtonNext=下一步(&N) >
ButtonInstall=安装(&I)
ButtonBack=< 上一步(&B)
ButtonCancel=取消
ButtonFinish=完成(&F)
ButtonBrowse=浏览(&R)...
StatusExtractFiles=正在解压缩文件...
StatusCreateIcons=正在创建快捷方式...
StatusCreateDir=正在创建文件夹...
StatusSavingUninstall=正在保存卸载信息...
ExitSetupTitle=退出安装
ExitSetupMessage=安装尚未完成。如果现在退出，程序将不会被安装。%n%n您确定要退出吗？
UninstallProgram=卸载 %1
LaunchProgram=运行 %1

[CustomMessages]
AdditionalIcons=附加图标:
CreateDesktopIcon=创建桌面快捷方式(&D)

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

; 资源包 (可选，如果存在则打包)
Source: "bin\Release\edl_loaders.pak"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "bin\Release\firehose.pak"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; 图标
Source: "MultiFlash TOOL.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\MultiFlash TOOL.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\MultiFlash TOOL.ico"; Tasks: desktopicon

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
