; Installeur A3Print - compile par build.ps1 (ISCC /DAppVersion=x.y.z)
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId=A3Print
AppName=A3Print
AppVersion={#AppVersion}
AppVerName=A3Print {#AppVersion}
AppPublisher=SimplementJohn
AppPublisherURL=https://github.com/SimplementJohn/A3Print
VersionInfoVersion={#AppVersion}
; Installation par utilisateur : aucun droit admin, ni a l'installation ni aux mises a jour.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\A3Print
DefaultGroupName=A3Print
DisableDirPage=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=A3Print-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\app.ico
UninstallDisplayName=A3Print
UninstallDisplayIcon={app}\A3Print.exe
CloseApplications=force
RestartApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\build\A3Print.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\vendor\SumatraPDF.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\A3Print"; Filename: "{app}\A3Print.exe"
Name: "{autodesktop}\A3Print"; Filename: "{app}\A3Print.exe"; Tasks: desktopicon

[Run]
; Installation normale : case "Lancer A3Print" en fin d'assistant.
Filename: "{app}\A3Print.exe"; Description: "{cm:LaunchProgram,A3Print}"; Flags: nowait postinstall skipifsilent
; Mise a jour silencieuse lancee par l'application (/RELAUNCH=1) : relance automatique.
Filename: "{app}\A3Print.exe"; Flags: nowait; Check: ShouldRelaunch

[UninstallDelete]
Type: files; Name: "{app}\SumatraPDF-settings.txt"
Type: filesandordirs; Name: "{localappdata}\A3Print"

[Code]
function ShouldRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;
