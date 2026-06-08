; Inno Setup script for the Datum Windows installer.
; Values are passed in via environment variables by the release workflow:
;   DATUM_VERSION       e.g. 26.1.0
;   DATUM_PUBLISH_DIR   absolute path to the self-contained publish output
;   DATUM_RID           e.g. win-x64 or win-arm64 (used in the output filename)
;   DATUM_ARCH          architecture identifier for Inno, e.g. x64compatible or arm64compatible

#define MyAppName "Datum"
#define MyAppPublisher "MacCoss Lab"
#define MyAppExeName "Datum.App.exe"
#define MyAppVersion GetEnv("DATUM_VERSION")
#define SourceDir GetEnv("DATUM_PUBLISH_DIR")
#define MyRid GetEnv("DATUM_RID")
#define MyArch GetEnv("DATUM_ARCH")

[Setup]
AppId={{2C7E9B14-5A3D-4F8E-9B2A-1E6D4C8F0A37}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Datum
DefaultGroupName=Datum
DisableProgramGroupPage=yes
OutputBaseFilename=datum-setup-{#MyAppVersion}-{#MyRid}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed={#MyArch}
ArchitecturesInstallIn64BitMode={#MyArch}
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\Datum"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall Datum"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Datum"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,Datum}"; Flags: nowait postinstall skipifsilent
