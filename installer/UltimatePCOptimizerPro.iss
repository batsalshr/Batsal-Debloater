; Inno Setup 6 script for ULTIMATE PC OPTIMIZER PRO
; Build:  ISCC.exe installer\UltimatePCOptimizerPro.iss   (after running build.ps1, which publishes to publish\win-x64)

#define AppName "ULTIMATE PC OPTIMIZER PRO"
#define AppVersion "1.0.0"
#define AppExe "UltimatePCOptimizerPro.exe"

[Setup]
AppId={{6F0C2F3A-9C6B-4C51-9E0B-3B8E1F5A7D21}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=ULTIMATE PC OPTIMIZER PRO
DefaultDirName={autopf}\UltimatePCOptimizerPro
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\publish\installer
OutputBaseFilename=UltimatePCOptimizerPro-Setup-{#AppVersion}
SetupIconFile=..\src\UltimatePCOptimizerPro\Resources\Icons\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "..\publish\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\USER_GUIDE.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\User guide"; Filename: "{app}\USER_GUIDE.md"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

; Backups, operation history and logs live in %LOCALAPPDATA%\UltimatePCOptimizerPro and are intentionally
; NOT removed on uninstall, so changes made by the app can still be inspected and restored (or reverted with
; Windows System Restore) after uninstalling.
[Messages]
ConfirmUninstall=Uninstall %1?%n%nBackups of changed settings are kept in %LOCALAPPDATA%\UltimatePCOptimizerPro. Restore any settings you want reverted BEFORE uninstalling (Backup & Recovery › Undo), or use Windows System Restore afterwards.
