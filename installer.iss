[Setup]
AppName=My WinUI3 Application
AppVersion=1.0.0
DefaultDirName={autopf}\MyWinUI3Application
DefaultGroupName=My WinUI3 Application
OutputBaseFilename=MyWinUI3AppSetup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
; Elevate to administrative privileges to allow system-wide Windows App SDK installation
PrivilegesRequired=admin

[Files]
; 1. Copy everything inside your staging folder (Main EXE, Settings EXE, DLLs, and .NET files)
Source: "C:\DeployStaging\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; 2. Embed the Windows App SDK Bootstrapper installer into the setup's temporary folder
Source: "C:\Dependencies\WindowsAppRuntimeInstall.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
; Create a desktop and start menu shortcut for your primary application only
Name: "{autoprograms}\My WinUI3 Application"; Filename: "{app}\MainApplication.exe"
Name: "{autodesktop}\My WinUI3 Application"; Filename: "{app}\MainApplication.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
; Run the Windows App SDK dependency wrapper silently BEFORE the main app is launched or the setup finishes
Filename: "{tmp}\WindowsAppRuntimeInstall.exe"; Parameters: "--quiet"; Flags: runhidden waituntilterminated

; Optional: Prompt the user to launch the main app upon completion
Filename: "{app}\MainApplication.exe"; Description: "{cm:LaunchProgram,My WinUI3 Application}"; Flags: nowait postinstall skipifsilent
