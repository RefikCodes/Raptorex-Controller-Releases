; Inno Setup Script for Raptorex Controller
#define MyAppName "Raptorex Controller"
#define MyAppVersion "5.0.0"
#define MyAppPublisher "Raptorex CNC"
#define MyAppExeName "Rptx01.exe"
#define MyAppId "{{2A68D3EC-7122-4F42-83D0-3F2BA6B7A9E2}}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={commonpf}\Raptorex Controller
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\dist
OutputBaseFilename=RaptorexController-{#MyAppVersion}-Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
SetupIconFile=..\Images\RaptorexIcon.ico
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\bin\Release\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[InstallDelete]
Type: files; Name: "{commondesktop}\{#MyAppName}.lnk"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: 

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
