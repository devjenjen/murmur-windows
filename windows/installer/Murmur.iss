#define MyAppName "Murmur"
#define MyAppPublisher "devjenjen"
#define MyAppExeName "Murmur.App.exe"

#ifndef MyAppVersion
  #define MyAppVersion GetFileVersion("..\\artifacts\\release\\publish\\win-x64\\Murmur.App.exe")
#endif

[Setup]
AppId={{2A68B7DE-0A17-4A48-BE67-5A3B7B93AB2E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Murmur
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\artifacts\release\installer
OutputBaseFilename=Murmur-Setup-v{#MyAppVersion}
SetupIconFile=..\src\Murmur.App\Assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartIfNeededByRun=no
Uninstallable=yes
AppMutex=Local\Murmur.App.2A68B7DE-0A17-4A48-BE67-5A3B7B93AB2E

[Files]
Source: "..\artifacts\release\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Murmur"; Flags: nowait postinstall skipifsilent

[Code]
const
  StartupRunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  StartupValueName = 'Murmur';

function NormalizeStartupCommand(const Value: string): string;
begin
  Result := Trim(Value);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StoredCommand: string;
  ExpectedCommand: string;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  ExpectedCommand := '"' + ExpandConstant('{app}\{#MyAppExeName}') + '"';
  if RegQueryStringValue(HKEY_CURRENT_USER, StartupRunKey, StartupValueName, StoredCommand) and
     (CompareText(NormalizeStartupCommand(StoredCommand), ExpectedCommand) = 0) then
  begin
    RegDeleteValue(HKEY_CURRENT_USER, StartupRunKey, StartupValueName);
  end;
end;
