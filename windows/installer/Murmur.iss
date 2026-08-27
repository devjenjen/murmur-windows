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

[Files]
Source: "..\artifacts\release\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Murmur"; Flags: nowait postinstall skipifsilent
