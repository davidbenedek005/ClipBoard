; ClipBoard installer. Compile with Inno Setup 6 after publish-release.ps1.
; Per-user install. Invite links use clipboardsync://, registered below for this user.

#define MyAppName "ClipBoard"
#define MyAppVersion "2.0.0"
#define MyAppExeName "ClipBoard.exe"
#define PublishDir "..\ClipboardSync.App\bin\Release\net10.0-windows\win-x64\publish"

[Setup]
AppId={{B7E4C1A8-2F6D-4A15-9C33-8D0E5F1A6B42}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=ClipBoard
DefaultDirName={localappdata}\Programs\ClipBoard
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename=ClipBoard-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\ClipboardSync.App\Resources\clipboard.ico
AppMutex=Local\ClipBoard.Sync.SingleInstance

[Tasks]
Name: startup; Description: "Start ClipBoard when I sign in"; GroupDescription: "Startup:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Registry]
; Same keys ProtocolRegistration writes at startup. HKCU needs no administrator.
; Uninstall removes the protocol so leftover invite links do not start a deleted exe.
Root: HKCU; Subkey: "Software\Classes\clipboardsync"; ValueType: string; ValueName: ""; ValueData: "URL:ClipBoard"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\clipboardsync"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\clipboardsync\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ClipBoard"; Flags: nowait postinstall skipifsilent
