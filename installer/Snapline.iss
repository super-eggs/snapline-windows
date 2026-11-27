#ifndef AppVersion
  #error Build Snapline and use package.ps1 to supply AppVersion.
#endif
#ifndef SnaplineBuildDir
  #define SnaplineBuildDir "..\dist"
#endif
#define SnaplineAppId "{BBFEDE84-A60E-4F11-9CC5-1EE29BA57425}"
#ifdef SnaplineOcr
  #define SnaplineName "Snapline OCR"
  #define SetupName "Snapline-OCR-Setup"
#else
  #define SnaplineName "Snapline"
  #define SetupName "Snapline-Setup"
#endif

[Setup]
AppId={{#SnaplineAppId}
AppName={#SnaplineName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher=super-eggs
AppPublisherURL=https://github.com/super-eggs/snapline-windows
AppSupportURL=https://github.com/super-eggs/snapline-windows/issues
AppUpdatesURL=https://github.com/super-eggs/snapline-windows/releases/latest
DefaultDirName={localappdata}\Programs\Snapline
DefaultGroupName=Snapline
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\artifacts\release
OutputBaseFilename={#SetupName}-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\snapline.ico
UninstallDisplayIcon={app}\Snapline.exe
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SnaplineBuildDir}\Snapline.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SnaplineBuildDir}\Snapline.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
#ifdef SnaplineOcr
Source: "{#SnaplineBuildDir}\recognition\*"; DestDir: "{app}\recognition"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

[Icons]
Name: "{group}\Snapline"; Filename: "{app}\Snapline.exe"
Name: "{autodesktop}\Snapline"; Filename: "{app}\Snapline.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Snapline.exe"; Description: "Launch Snapline"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstalledPath: String;
  Attempt: Integer;
begin
  Result := '';
  if ExpandConstant('{param:SNAPLINEUPDATE|0}') <> '1' then
    Exit;

  if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SnaplineAppId}_is1', 'Inno Setup: App Path', InstalledPath) then
    InstalledPath := '';
  if (InstalledPath = '') or not FileExists(AddBackslash(InstalledPath) + 'Snapline.exe') or
      (CompareText(RemoveBackslashUnlessRoot(ExpandConstant('{app}')), RemoveBackslashUnlessRoot(InstalledPath)) <> 0) then
  begin
    Result := 'This update must be installed in the current Snapline installation folder.';
    Exit;
  end;

  { Snapline releases this mutex after saving queued screenshots and shutting down. }
  for Attempt := 1 to 100 do
  begin
    if not CheckForMutexes('Local\Snapline.Windows') then
      Exit;
    Sleep(100);
  end;
  Result := 'Snapline is still running. Exit it from the system tray, then try again.';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Snapline', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\Snapline.exe') + '" --startup') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Snapline');
end;

function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release);
  if Result then
    Result := Release >= 528040;
  if not Result then
    MsgBox('Snapline requires .NET Framework 4.8 or later. Install it from Microsoft, then run Setup again.', mbError, MB_OK);
end;
