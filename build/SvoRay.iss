; Inno Setup script for the single SvoRay installer.
;
; Build it with build\BuildInstaller.ps1, which publishes a clean tree first and
; then calls:
;   ISCC.exe /DAppVersion=0.4.0 /DSourceDir=<publish dir> build\SvoRay.iss
;
; The application requires administrator rights at runtime because TUN does, so the
; installer is elevated too and installs per machine into Program Files.

#ifndef AppVersion
  #define AppVersion "0.4.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\SvoRay-" + AppVersion + "-win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "SvoRay"
#define AppExe "SvoRay.exe"
#define AppPublisher "SvoRay"

[Setup]
; Keep this GUID stable forever: it is what lets a new build upgrade an old one in place.
AppId={{2F7C43A9-56D1-4B08-B3E7-1C9A5D40E2B6}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\LICENSE
; Ask Windows to shut the running client down before files are replaced.
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start menu only. No desktop shortcut is created by default.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Run]
; runascurrentuser is required, not optional. A postinstall entry runs as the user who
; started Setup, without elevation, and Inno launches it with CreateProcess - which cannot
; raise a UAC prompt. The client manifest asks for requireAdministrator, so without this
; flag the launch fails with error 740. The flag reuses the token Setup already has, so the
; user is not asked to confirm UAC a second time.
Filename: "{app}\{#AppExe}"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
; Anything the app generated inside its own program folder.
Type: filesandordirs; Name: "{app}\bin"
Type: dirifempty; Name: "{app}"

[Messages]
russian.WelcomeLabel2=Будет установлен [name/ver].%n%nПриложению нужны права администратора: режим TUN без них не работает.%n%nСборка не подписана коммерческим сертификатом, поэтому Windows SmartScreen может показать предупреждение «Неизвестный издатель».

[Code]
const
  UserDataFolder = 'SvoRay';

function ProcessRunning(const ExeName: string): Boolean;
var
  ResultCode: Integer;
begin
  // tasklist filters are cheap and do not need extra tooling on the target machine.
  Result := Exec(ExpandConstant('{cmd}'),
    '/C tasklist /FI "IMAGENAME eq ' + ExeName + '" | find /I "' + ExeName + '" > nul',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure StopProcess(const ExeName: string);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM ' + ExeName + ' > nul 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure StopSvoRay();
begin
  // The client holds a single-instance handle named after its own path, so Inno's
  // AppMutex cannot be used. Stop the GUI first, then any core it left behind.
  if ProcessRunning('{#AppExe}') then
    StopProcess('{#AppExe}');
  StopProcess('xray.exe');
  StopProcess('sing-box.exe');
  Sleep(1200);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopSvoRay();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopSvoRay();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataPath: string;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  DataPath := ExpandConstant('{localappdata}\' + UserDataFolder);
  if not DirExists(DataPath) then
    Exit;

  // Settings, subscriptions and profiles are only removed on an explicit choice.
  if MsgBox('Удалить настройки, подписки и профили SvoRay?' + #13#10 +
            'Нажмите «Нет», чтобы сохранить их для повторной установки.' + #13#10#13#10 +
            DataPath,
            mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(DataPath, True, True, True);
end;
