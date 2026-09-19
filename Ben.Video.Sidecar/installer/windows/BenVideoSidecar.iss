; Inno Setup script for the BenVideo sidecar - a double-click installer for Windows.
;
; WHY THIS EXISTS. The zip works, and it asks a lot: download 156 MB, extract it (actually extract
; it, not browse it in Explorer's zip viewer), then right-click install.ps1 and fight the execution
; policy. That is the same "three steps and a terminal" the macOS build stopped asking for when it
; moved to a disk image. This is the Windows equivalent.
;
; IT DELIBERATELY KEEPS THE PER-USER DESIGN of install.ps1. PrivilegesRequired=lowest means no UAC
; prompt: everything lands in %LOCALAPPDATA% and autostart is an HKCU key. Asking an unsigned
; binary for administrator rights is exactly the trade nobody should accept, and the sidecar has
; never needed them.
;
; MARK OF THE WEB. install.ps1 runs Unblock-File over every extracted file because a downloaded zip
; marks its contents, and a blocked DLL fails later and less clearly than a blocked launcher. That
; problem does not exist here: files written by an installer are not individually marked, so there
; is nothing to unblock. The installer .exe itself still carries the mark and SmartScreen will still
; warn about an unknown publisher - see the note in build-installer.ps1.
;
; Build it with installer\windows\build-installer.ps1, which supplies MyAppVersion.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#define MyAppName "BenVideo Sidecar"
#define MyAppExeName "Ben.Video.Sidecar.exe"
#define MyAppPublisher "IsHaunted.com"
#define MyAppURL "https://ishaunted.com/editors/video/downloads/"

[Setup]
; Stable across versions - this is what lets an upgrade replace an install rather than sit beside
; it, and what Add/Remove Programs keys off. Never regenerate it.
AppId={{7C4E8A26-9B31-4D5F-9E02-3A6C15D7B884}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}

; Per-user, so there is no UAC prompt for an unsigned build.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={localappdata}\BenVideoSidecar
DisableDirPage=yes
DisableProgramGroupPage=yes
DefaultGroupName={#MyAppName}

; The payload is a self-contained .NET publish for win-x64 and cannot run anywhere else.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir=..\dist
OutputBaseFilename=BenVideoSidecar-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
; ~150 MB of payload; without this the wizard's estimate is wrong and the disk-space check is too.
ExtraDiskSpaceRequired=0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; app\ is what installer\windows\build.sh stages: the self-contained sidecar, its ffmpeg pair and
; the manifest. Built separately because it cross-publishes from macOS.
; .pdb files are excluded: debug symbols are of no use on a tester's machine and they publish the
; build machine's source paths and internal structure to anyone who downloads the installer.
Source: "..\dist\BenVideoSidecar-win-x64\app\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "post-install.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{app}\logs"

[Registry]
; Same key and name install.ps1 used, so an existing install is upgraded rather than duplicated.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "BenVideoSidecar"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Pairing page"; Filename: "http://127.0.0.1:43117/pair"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
; Starts it, waits for a real health response, and opens the pairing page on whichever port it
; actually took - the sidecar walks upwards from 43117 when one is occupied, so the port cannot be
; assumed. -ExecutionPolicy Bypass is supplied here, so the user never meets that prompt.
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\post-install.ps1"" -InstallDir ""{app}"""; \
    Description: "Start the sidecar and open the pairing page"; \
    Flags: postinstall nowait skipifsilent runhidden

[UninstallRun]
; Stop it before the files go, or the directory delete fails on a locked executable.
Filename: "taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "StopSidecar"

[UninstallDelete]
; Logs are written after install, so Inno does not know about them and would leave the folder.
Type: filesandordirs; Name: "{app}\logs"

[Code]
// A running sidecar holds its own executable open, so an upgrade fails on a locked file with a
// message about the file being in use. Stopping it first turns that into a non-event.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  Exec('taskkill.exe', '/IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // A non-zero code here just means it was not running, which is the common case.
  Sleep(500);
end;
