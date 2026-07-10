; Photon installer script (Inno Setup 6).
; CI passes /DAppVersion=<tag> and /DPublishDir=<workspace>\publish;
; the defaults below make a local build work out of the box.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
; Fixed AppId so upgrades replace previous installs — never regenerate this GUID.
AppId={{F33431AB-B20C-451A-A281-2FA48D9D9D74}
AppName=Photon
AppVersion={#AppVersion}
AppPublisher=The-Berin
DefaultDirName={autopf}\Photon
DefaultGroupName=Photon
DisableProgramGroupPage=yes
; Per-user install, no UAC prompt ({autopf} resolves to %LocalAppData%\Programs).
PrivilegesRequired=lowest
OutputBaseFilename=PhotonSetup-{#AppVersion}
OutputDir=Output
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Photon.exe
SetupIconFile=..\src\Photon\Assets\photon.ico

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\Photon.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Photon"; Filename: "{app}\Photon.exe"
Name: "{autodesktop}\Photon"; Filename: "{app}\Photon.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Photon.exe"; Description: "{cm:LaunchProgram,Photon}"; Flags: nowait postinstall skipifsilent
