; Echo — TC-80 installer script
; Run from repo root:
;   dotnet publish Echo.csproj -c Release -r win-x64 --self-contained -o installer/staging
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/echo-setup.iss
; Produces: installer/output/Echo-Setup-<version>.exe

#define MyAppName "Echo"
#define MyAppNameLong "Echo — Speech to Text"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Echo"
#define MyAppURL "https://digvijay208.github.io/Echo-Speech-to-Text"
#define MyAppExeName "Echo.exe"
#define MyAppCopyright "© 2026 Echo"

[Setup]
; App identity
AppId={{B3F1C2A4-7E5D-4F8A-9C1D-2E6F8A4B3C5D}
AppName={#MyAppNameLong}
AppVersion={#MyAppVersion}
AppVerName={#MyAppNameLong} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppCopyright={#MyAppCopyright}

; Output
OutputDir=output
OutputBaseFilename=Echo-Setup-{#MyAppVersion}

; Install location
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppNameLong}
DisableProgramGroupPage=yes

; Privileges
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

; Compression + visuals
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
; SetupIconFile=installer\assets\echo-icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppNameLong}
ArchitecturesInstallIn64BitMode=x64compatible

; UX
WizardStyle=modern
WizardSizePercent=120
WindowVisible=no
DisableWelcomePage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: checkablealone
Name: "startmenu";   Description: "Create a Start Menu shortcut"; GroupDescription: "Additional icons:"; Flags: checkablealone
Name: "addpath";     Description: "Add Echo install folder to PATH (advanced)"; GroupDescription: "Advanced:"; Flags: unchecked

[Files]
; Bundle the whole self-contained publish output.
Source: "staging\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; License + readme (optional, present only if they exist)
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist; Check: FileExists(ExpandConstant('{src}\LICENSE.txt'))
Source: "..\README.md";   DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist isreadme; Check: FileExists(ExpandConstant('{src}\README.md'))

[Icons]
; Start menu
Name: "{group}\{#MyAppNameLong}";      Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppNameLong}}"; Filename: "{uninstallexe}"
; Desktop (if task checked)
Name: "{autodesktop}\{#MyAppNameLong}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Dirs]
; First-run data dirs (matches %LOCALAPPDATA%\Echo layout the app uses)
Name: "{userappdata}\Echo";          Permissions: users-modify
Name: "{userappdata}\Echo\logs";      Permissions: users-modify
Name: "{userappdata}\Echo\models";    Permissions: users-modify
Name: "{userappdata}\Echo\recordings";Permissions: users-modify

[Registry]
; HKCU\Software\Echo — version stamp + hotkey hint; app reads/writes here
Root: HKCU; Subkey: "Software\Echo"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Echo"; ValueType: string; ValueName: "Version";     ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey

[Run]
; Optional: launch on finish
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppNameLong}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Keep user data — never wipe %LOCALAPPDATA%\Echo on uninstall.
; Only remove the install dir's transient cache.
Type: filesandordirs; Name: "{app}\*.log"
