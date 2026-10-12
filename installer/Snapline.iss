#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{BBFEDE84-A60E-4F11-9CC5-1EE29BA57425}
AppName=Snapline
AppVersion={#AppVersion}
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
OutputBaseFilename=Snapline-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Snapline.exe
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\dist\Snapline.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\Snapline.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Snapline"; Filename: "{app}\Snapline.exe"
Name: "{autodesktop}\Snapline"; Filename: "{app}\Snapline.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Snapline.exe"; Description: "Launch Snapline"; Flags: nowait postinstall skipifsilent

[Code]
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
