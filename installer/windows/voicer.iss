; Voicer Installer — Inno Setup Script
; Requires Inno Setup 6+ (https://jrsoftware.org/isinfo.php)

#define MyAppName "Voicer"
#define MyAppVersion GetFileVersion("..\..\publish\Voicer.exe")
#define MyAppPublisher "Voicer"
#define MyAppExeName "Voicer.exe"

[Setup]
AppId={{1756c38e-3ea1-4443-98d2-c20a9f610984}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\..\output
OutputBaseFilename=VoicerSetup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\icons\voicer.ico
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Запускать при старте Windows"; GroupDescription: "Дополнительно:"

[Files]
; Application files (self-contained publish)
Source: "..\..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Speech recognition model (GigaAM-v3 e2e with built-in punctuation)
Source: "..\..\models\v3_e2e_ctc.int8.onnx"; DestDir: "{app}\models"; Flags: ignoreversion
Source: "..\..\models\v3_e2e_ctc_vocab.txt"; DestDir: "{app}\models"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Удалить {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Voicer"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить Voicer"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\settings.json"
