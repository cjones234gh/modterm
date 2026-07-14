; =================================================================================
; modterm Inno Setup script
; Staging and dependency folders are produced/filled by build-pipeline.ps1
; =================================================================================

#define MyAppName "modterm"
#define MyAppVersion "0.8.0"
#define MyAppPublisher "modterm"
#define MyAppExeName "modterm.exe"
#define MyAppAssocName "modterm Terminal"
#define StagingDir "deploy\staging"
#define DepsDir "deploy\dependencies"
#define WinAppRuntimeInstaller "WindowsAppRuntimeInstall-x64.exe"
#ifndef DotNetDesktopRuntimeInstaller
  #define DotNetDesktopRuntimeInstaller "windowsdesktop-runtime-8.0.28-win-x64.exe"
#endif

[Setup]
AppId={{A6C3E1F2-8B4D-4E9A-9C2F-1D7E5A0B3C84}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/cjones234gh/modterm
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=deploy\output
OutputBaseFilename=modtermSetup-{#MyAppVersion}
SetupIconFile=modterm\Modterm.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Elevate so redistributable runtimes can install system-wide.
PrivilegesRequired=admin
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main app publish output (framework-dependent WinUI unpackaged binaries + assets)
Source: "{#StagingDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Redistributable runtimes (downloaded by build-pipeline.ps1)
Source: "{#DepsDir}\{#DotNetDesktopRuntimeInstaller}"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "{#DepsDir}\{#WinAppRuntimeInstaller}"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Install shared .NET 8 Desktop Runtime first (required by modterm + modtermTE).
Filename: "{tmp}\{#DotNetDesktopRuntimeInstaller}"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing .NET 8 Desktop Runtime..."; Flags: runhidden waituntilterminated

; Then install / update Windows App Runtime for unpackaged WinUI.
Filename: "{tmp}\{#WinAppRuntimeInstaller}"; Parameters: "--quiet"; StatusMsg: "Installing Windows App Runtime..."; Flags: runhidden waituntilterminated

Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
