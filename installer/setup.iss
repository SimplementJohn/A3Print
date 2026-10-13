; Installeur ImprimLot - compile par build.ps1 (ISCC /DAppVersion=x.y.z)
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId=ImprimLot
AppName=ImprimLot
AppVersion={#AppVersion}
AppVerName=ImprimLot {#AppVersion}
AppPublisher=SimplementJohn
AppPublisherURL=https://github.com/SimplementJohn/imprimlot
VersionInfoVersion={#AppVersion}
; Installation par utilisateur : aucun droit admin, ni a l'installation ni aux mises a jour.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\ImprimLot
DefaultGroupName=ImprimLot
DisableDirPage=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=ImprimLot-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\app.ico
UninstallDisplayName=ImprimLot
UninstallDisplayIcon={app}\ImprimLot.exe
CloseApplications=force
RestartApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\build\ImprimLot.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\vendor\SumatraPDF.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ImprimLot"; Filename: "{app}\ImprimLot.exe"
Name: "{autodesktop}\ImprimLot"; Filename: "{app}\ImprimLot.exe"; Tasks: desktopicon

[Run]
; Installation normale : case "Lancer ImprimLot" en fin d'assistant.
Filename: "{app}\ImprimLot.exe"; Description: "{cm:LaunchProgram,ImprimLot}"; Flags: nowait postinstall skipifsilent
; Mise a jour silencieuse lancee par l'application (/RELAUNCH=1) : relance automatique.
Filename: "{app}\ImprimLot.exe"; Flags: nowait; Check: ShouldRelaunch

[UninstallDelete]
Type: files; Name: "{app}\SumatraPDF-settings.txt"
Type: filesandordirs; Name: "{localappdata}\ImprimLot"

[Code]
function ShouldRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;
