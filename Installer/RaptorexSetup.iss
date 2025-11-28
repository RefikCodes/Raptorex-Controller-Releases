; Raptorex CNC Controller - Inno Setup Script
; Eski versiyonu kaldırır ve yeni versiyonu kurar

#define MyAppName "Raptorex Controller"
#define MyAppVersion "2.0.1"
#define MyAppPublisher "Raptorex CNC"
#define MyAppURL "https://github.com/RefikCodes/Raptorex-Controller-PC-3Axis-only"
#define MyAppExeName "Rptx01.exe"
#define MyAppId "{{7E8B9A6C-5D4F-3E2A-1B0C-9D8E7F6A5B4C}"

[Setup]
; Uygulama kimliği - eski versiyonu bulmak için kullanılır
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
; Masaüstü kısayolu oluştur
AllowNoIcons=yes
; Lisans dosyası (opsiyonel)
; LicenseFile=License.txt
; Çıktı ayarları
OutputDir=..\bin\Installer
OutputBaseFilename=RaptorexController_Setup_{#MyAppVersion}
; SetupIconFile=..\Images\RaptorexIcon.ico  ; Ikon dosyası eklendiğinde aktif et
Compression=lzma2/ultra64
SolidCompression=yes
; Windows Vista+ için admin hakları
PrivilegesRequired=admin
; Modern görünüm
WizardStyle=modern
; Upgrade modu - eski versiyonu otomatik kaldır
UsePreviousAppDir=yes
CloseApplications=force
RestartApplications=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; OnlyBelowVersion: 6.1

[Files]
; Ana uygulama dosyaları
Source: "..\bin\Release\Rptx01.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\Rptx01.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\Rptx01.pdb"; DestDir: "{app}"; Flags: ignoreversion
; Ek DLL veya dosyalar varsa buraya ekleyin
; Source: "..\bin\Release\*.dll"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Uygulama klasöründeki ayar dosyalarını sil
Type: filesandordirs; Name: "{app}\*.log"
Type: filesandordirs; Name: "{app}\settings"

[Code]
// Eski versiyon kontrolü ve kaldırma
function GetUninstallString(): String;
var
  sUnInstPath: String;
  sUnInstallString: String;
begin
  sUnInstPath := ExpandConstant('Software\Microsoft\Windows\CurrentVersion\Uninstall\{#emit SetupSetting("AppId")}_is1');
  sUnInstallString := '';
  if not RegQueryStringValue(HKLM, sUnInstPath, 'UninstallString', sUnInstallString) then
    RegQueryStringValue(HKCU, sUnInstPath, 'UninstallString', sUnInstallString);
  Result := sUnInstallString;
end;

function IsUpgrade(): Boolean;
begin
  Result := (GetUninstallString() <> '');
end;

function UnInstallOldVersion(): Integer;
var
  sUnInstallString: String;
  iResultCode: Integer;
begin
  // Varsayılan dönüş değeri
  Result := 0;

  sUnInstallString := GetUninstallString();
  if sUnInstallString <> '' then begin
    sUnInstallString := RemoveQuotes(sUnInstallString);
    if Exec(sUnInstallString, '/SILENT /NORESTART /SUPPRESSMSGBOXES', '', SW_HIDE, ewWaitUntilTerminated, iResultCode) then
      Result := 3
    else
      Result := 2;
  end else
    Result := 1;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep=ssInstall) then
  begin
    if (IsUpgrade()) then
    begin
      UnInstallOldVersion();
    end;
  end;
end;

// Uygulama çalışıyorsa uyar
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  
  // Uygulamanın çalışıp çalışmadığını kontrol et
  if Exec('tasklist', '/FI "IMAGENAME eq Rptx01.exe" /NH', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    // Uygulama çalışıyorsa, kapatmak için sor
    if MsgBox('Raptorex Controller şu anda çalışıyor. Kuruluma devam etmek için kapatılması gerekiyor. Devam edilsin mi?', 
              mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
    end
    else
    begin
      // Uygulamayı kapat
      Exec('taskkill', '/F /IM Rptx01.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Sleep(1000);
    end;
  end;
end;
