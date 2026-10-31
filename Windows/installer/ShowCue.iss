; Installasjonsprogram for ShowCue (Inno Setup 6).
; Bygges av GitHub Actions:  iscc /DAppVersion=0.6.0 Windows\installer\ShowCue.iss
; Forventer ferdig program i Windows\publish\ShowCue.exe (fra dotnet publish).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{EDB71E33-0C57-4A68-91A6-884C6B18DC02}
AppName=ShowCue
AppVersion={#AppVersion}
AppVerName=ShowCue {#AppVersion}
AppPublisher=ShowCue
AppPublisherURL=https://github.com/trondmorset-lab/Showcue-
AppSupportURL=https://github.com/trondmorset-lab/Showcue-/issues
AppUpdatesURL=https://github.com/trondmorset-lab/Showcue-/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\ShowCue
DefaultGroupName=ShowCue
DisableProgramGroupPage=yes
; Installer bare for meg selv (krever ikke administrator), eller for alle brukere hvis man velger det.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\publish
OutputBaseFilename=ShowCue-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes
UninstallDisplayIcon={app}\ShowCue.exe
UninstallDisplayName=ShowCue {#AppVersion}

[Languages]
#if FileExists(CompilerPath + "Languages\Norwegian.isl")
Name: "nb"; MessagesFile: "compiler:Languages\Norwegian.isl"
#endif
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
en.AssocTask=Open ShowCue shows (.showcue) with ShowCue
en.ShowFile=ShowCue show
en.OtherTasks=Other
#if FileExists(CompilerPath + "Languages\Norwegian.isl")
nb.AssocTask=Åpne ShowCue-show (.showcue) med ShowCue
nb.ShowFile=ShowCue-show
nb.OtherTasks=Annet
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "fileassoc"; Description: "{cm:AssocTask}"; GroupDescription: "{cm:OtherTasks}"

[Files]
Source: "..\publish\ShowCue.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "Les meg.md"; Flags: ignoreversion
Source: "..\..\CHANGELOG.md"; DestDir: "{app}"; DestName: "Endringslogg.md"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ShowCue"; Filename: "{app}\ShowCue.exe"
Name: "{autodesktop}\ShowCue"; Filename: "{app}\ShowCue.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.showcue"; ValueType: string; ValueName: ""; ValueData: "ShowCue.Show"; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ShowCue.Show"; ValueType: string; ValueName: ""; ValueData: "{cm:ShowFile}"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ShowCue.Show\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\ShowCue.exe,0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ShowCue.Show\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ShowCue.exe"" ""%1"""; Tasks: fileassoc

[Run]
Filename: "{app}\ShowCue.exe"; Description: "{cm:LaunchProgram,ShowCue}"; Flags: nowait postinstall skipifsilent
