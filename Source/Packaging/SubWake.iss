// Inputs supplied by Tools/Build.ps1: AppVersion, PayloadRoot, OutputRoot, AppIcon.
[Setup]
AppId=SubWake
AppName=SubWake
AppVersion={#AppVersion}
AppVerName=SubWake {#AppVersion}
AppPublisher=Rumenito
AppCopyright=(c) 2026 Rumenito
AppComments=Keeps subwoofers with auto-standby awake with short, quiet low-frequency bursts.
VersionInfoVersion={#AppVersion}
VersionInfoProductName=SubWake
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription=SubWake Setup
DefaultDirName={localappdata}\Programs\SubWake
DefaultGroupName=SubWake
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19045
OutputDir={#OutputRoot}
OutputBaseFilename=SubWake-Setup-{#AppVersion}-x64
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\SubWake.ico
UninstallDisplayName=SubWake
WizardStyle=modern dark
Compression=lzma2/fast
LZMANumBlockThreads=1
LZMAUseSeparateProcess=yes
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes
SetupLogging=yes
; Shown before installing: what the app does, what it changes, and its one network request (the update check).
InfoBeforeFile=Privacy.txt

[Tasks]
; On by default because the app's job is to run in the background. Upgrades keep the previous choice.
Name: startup; Description: "Start SubWake when I sign in to Windows"
Name: updates; Description: "Check for updates once a day (asks GitHub for the latest version, sends no personal data)"
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PayloadRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\SubWake"; Filename: "{app}\SubWake.exe"; WorkingDir: "{app}"; IconFilename: "{app}\SubWake.ico"
Name: "{group}\Uninstall SubWake"; Filename: "{uninstallexe}"
Name: "{autodesktop}\SubWake"; Filename: "{app}\SubWake.exe"; WorkingDir: "{app}"; IconFilename: "{app}\SubWake.ico"; Tasks: desktopicon

[Registry]
; Same Run value format as StartupRegistration.Set.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SubWake"; ValueData: """{app}\SubWake.exe"" --startup"; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "SubWake"; Check: not WizardIsTaskSelected('startup'); Flags: deletevalue
; The update-check choice, picked up by the app (Settings.ApplyInstallerChoice).
Root: HKCU; Subkey: "Software\SubWake"; ValueType: dword; ValueName: "CheckForUpdates"; ValueData: 1; Tasks: updates; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\SubWake"; ValueType: dword; ValueName: "CheckForUpdates"; ValueData: 0; Check: not WizardIsTaskSelected('updates'); Flags: uninsdeletekey

[Run]
Filename: "{app}\SubWake.exe"; Description: "Open SubWake"; Flags: postinstall nowait skipifsilent
Filename: "{app}\SubWake.exe"; Parameters: "--startup"; Flags: nowait skipifnotsilent

[UninstallRun]
Filename: "{app}\SubWake.exe"; Parameters: "--stop"; Flags: runhidden waituntilterminated; RunOnceId: "Stop"
Filename: "{app}\SubWake.exe"; Parameters: "--configure-startup=off"; Flags: runhidden waituntilterminated; RunOnceId: "Startup"

[Code]
function WebViewInstalled: Boolean;
var Version: String;
begin
  Result := (RegQueryStringValue(HKLM32, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
  if not Result then
    Result := (RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  Result := '';
  // Windows 11 includes WebView2. Without it the bursts still work, but the window can't open.
  if not WebViewInstalled then
    MsgBox('Microsoft WebView2 was not found. SubWake will still run from the tray, but its window needs WebView2 (free from Microsoft).', mbInformation, MB_OK);
  if FileExists(ExpandConstant('{app}\SubWake.exe')) then
    Exec(ExpandConstant('{app}\SubWake.exe'), '--stop', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Settings and the log in %LOCALAPPDATA%\SubWake are kept on purpose.
  if CurUninstallStep = usUninstall then Sleep(1000);
end;
