[Setup]
AppName=Label Print Client
AppVersion=1.0.0
AppPublisher=GoBinary
DefaultDirName={localappdata}\Programs\LabelPrintClient
DefaultGroupName=Label Print Client
UninstallDisplayIcon={app}\LabelPrintClient.exe
OutputDir=..\dist
OutputBaseFilename=LabelPrintClient-Setup-1.0.0
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
SetupIconFile=..\src\LabelPrintClient\Resources\app.ico

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\Label Print Client"; Filename: "{app}\LabelPrintClient.exe"
Name: "{group}\Uninstall Label Print Client"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LabelPrintClient"; ValueData: """{app}\LabelPrintClient.exe"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\LabelPrintClient.exe"; Description: "Launch Label Print Client"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\LabelPrintClient"
