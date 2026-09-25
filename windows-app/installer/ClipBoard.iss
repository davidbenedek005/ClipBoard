; ClipBoard installer. Compile with Inno Setup 6 after publish-release.ps1.
; The app is a tray program. Closing the installer does not leave a taskbar window.

#define MyAppName "ClipBoard"
#define MyAppVersion "0.4.0"
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
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\ClipboardSync.App\Resources\clipboard.ico

[Tasks]
Name: startup; Description: "Start ClipBoard when I sign in"; GroupDescription: "Startup:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ClipBoard"; Flags: nowait postinstall skipifsilent
