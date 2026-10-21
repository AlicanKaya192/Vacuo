; Vacuo setup (Inno Setup 6), adapted from Odyssey's installer.
;
; Build: tools/build-installer.ps1 publishes the app and the tray into one folder and passes
; /DAppVersion, /DSourceDir and /DOutputDir here.
;
; Rules:
; - AppId never changes; otherwise Windows treats the next version as a second program.
; - Per-user install (%LOCALAPPDATA%\Programs\Vacuo) without administrator rights, so the app can
;   update itself silently. The Windows clean-up helper still asks for rights only when it runs (ADR-003).
; - User data (%LOCALAPPDATA%\Vacuo: settings, Journal) is never touched by setup or uninstall.
; - Starting with Windows (vacuo-kurallar 5): on by default at the first install only; an update never
;   turns it back on after the user turned it off. Uninstall removes Vacuo's own Run value only.
; - Parameters the app passes when it updates itself: /OLDPID=<panel> /OLDTRAYPID=<tray>; setup waits
;   for both to close, because they hold the files open.
; - Patch setup (K4, as in Odyssey): tools/build-installer.ps1 -PatchFrom compares an earlier release's
;   file list with this one and compiles this script again with /DPatchFrom=<earlier version> and
;   /DPatchList=<sections file>. The result, Vacuo-<new>-patch-<earlier>.exe, writes only new and changed
;   files and removes dropped ones. It runs only over <earlier>; on any other version it changes nothing.

#define AppName "Vacuo"
#define AppGuid "DF5C5D57-E519-49CE-91F5-63911A327D10"
#define TrayExe "Vacuo.Tray.exe"
#define PanelExe "Vacuo.exe"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{{#AppGuid}}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Alican Kaya
AppPublisherURL=https://github.com/AlicanKaya192/Vacuo
AppSupportURL=https://github.com/AlicanKaya192/Vacuo/issues
AppUpdatesURL=https://github.com/AlicanKaya192/Vacuo/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; The folder page only on the first install; updates go to the same place.
DisableDirPage=auto
UsePreviousAppDir=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
#ifdef PatchFrom
OutputBaseFilename={#AppName}-{#AppVersion}-patch-{#PatchFrom}
#else
OutputBaseFilename={#AppName}-{#AppVersion}-setup
#endif
SetupIconFile=..\src\Vacuo.App\Assets\vacuo.ico
UninstallDisplayIcon={app}\{#PanelExe}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; An update from the app: setup waits for the panel and the tray itself (/OLDPID, /OLDTRAYPID), so
; nothing of Vacuo is running any more when files are checked. Started by hand while Vacuo runs, setup
; lists Vacuo and closes it before writing (Alican, 20 October 2026: a patch run by hand stopped on a
; locked Vacuo.dll). Only programs holding files in Vacuo's own folder are closed; [Run] starts Vacuo again.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
turkish.StartWithWindows=Bilgisayar açılınca başlat
english.StartWithWindows=Start with Windows
turkish.PatchNeedsBase=Bu yama yalnızca Vacuo %1 kuruluyken çalışır. Bu bilgisayarda Vacuo yok ya da başka bir sürüm kurulu; hiçbir şey değiştirilmedi.%n%nTam kurulumu kullan: Vacuo-%2-setup.exe
english.PatchNeedsBase=This patch only works while Vacuo %1 is installed. Vacuo is not installed on this computer, or another version is; nothing was changed.%n%nUse the full setup: Vacuo-%2-setup.exe

[Tasks]
Name: "startup"; Description: "{cm:StartWithWindows}"; Check: IsFirstInstall
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

#ifdef PatchFrom
; Patch: the files to remove and to write come from the file lists ([InstallDelete] and [Files]).
#include PatchList
#else
[InstallDelete]
; The previous version's program files go first, so a library dropped in the new version cannot be
; loaded from an old copy. Only Vacuo's own install folder; user data is elsewhere.
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.exe"
Type: files; Name: "{app}\*.pri"
Type: files; Name: "{app}\*.winmd"
Type: files; Name: "{app}\*.json"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

[Icons]
; --open: started by hand, Vacuo shows its panel, so the user sees where it went (Alican, 20 October 2026).
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#TrayExe}"; Parameters: "--open"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#TrayExe}"; Parameters: "--open"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#TrayExe}"""; Tasks: startup

[Run]
; Also in silent mode (no skipifsilent): after an update Vacuo comes back by itself. With --open the
; panel shows as well, so after setup the user sees where Vacuo is, not only a tray icon.
Filename: "{app}\{#TrayExe}"; Parameters: "--open"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall

[Code]
const
  SYNCHRONIZE = $00100000;
  OLD_EXIT_WAIT_MS = 60000;
  UNINSTALL_KEY = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppGuid}}_is1';

function OpenProcess(dwDesiredAccess: Cardinal; bInheritHandle: Boolean; dwProcessId: Cardinal): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(hObject: THandle; dwMilliseconds: Cardinal): Cardinal;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

var
  FirstInstall: Boolean;

{ Waits for one process the update named; it holds Vacuo's files open. }
procedure WaitForProcess(const Param: String);
var
  Pid: Integer;
  Handle: THandle;
begin
  Pid := StrToIntDef(ExpandConstant('{param:' + Param + '|0}'), 0);
  if Pid <= 0 then
    Exit;
  Handle := OpenProcess(SYNCHRONIZE, False, Pid);
  if Handle = 0 then
    Exit;
  WaitForSingleObject(Handle, OLD_EXIT_WAIT_MS);
  CloseHandle(Handle);
end;

#ifdef PatchFrom
{ A patch writes only what changed since {#PatchFrom}; over any other version the files would mix. It then
  changes nothing, waits for the old Vacuo to close and starts it again; the app takes the full setup on its
  next try (InstallFollowUps.PatchFailedBefore). }
function IsPatchBaseInstalled(): Boolean;
var
  Version, Location: String;
  ResultCode: Integer;
begin
  Result := RegQueryStringValue(HKCU, UNINSTALL_KEY, 'DisplayVersion', Version) and (Version = '{#PatchFrom}');
  if Result then
    Exit;
  { Started by hand, it says why it did nothing (Alican, 20 October 2026: it closed without a word). }
  if not WizardSilent() then
    MsgBox(FmtMessage(CustomMessage('PatchNeedsBase'), ['{#PatchFrom}', '{#AppVersion}']), mbInformation, MB_OK);
  WaitForProcess('OLDPID');
  WaitForProcess('OLDTRAYPID');
  if RegQueryStringValue(HKCU, UNINSTALL_KEY, 'InstallLocation', Location) and
     FileExists(AddBackslash(Location) + '{#TrayExe}') then
    Exec(AddBackslash(Location) + '{#TrayExe}', '--open', Location, SW_SHOWNORMAL, ewNoWait, ResultCode);
end;
#endif

function InitializeSetup(): Boolean;
begin
  FirstInstall := not RegKeyExists(HKCU, UNINSTALL_KEY);
#ifdef PatchFrom
  Result := IsPatchBaseInstalled();
#else
  Result := True;
#endif
end;

{ Starting with Windows is offered only the first time; an update keeps whatever the user chose. }
function IsFirstInstall(): Boolean;
begin
  Result := FirstInstall;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  WaitForProcess('OLDPID');
  WaitForProcess('OLDTRAYPID');
  { File handles can take a moment to close. }
  Sleep(1000);
  Result := '';
end;

{ Removes Vacuo's Run value only when it points into this install, whoever wrote it (setup or Settings). }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;
  if RegQueryStringValue(HKCU, '{#RunKey}', '{#AppName}', Value) and
     (Pos(Uppercase(ExpandConstant('{app}')), Uppercase(Value)) > 0) then
    RegDeleteValue(HKCU, '{#RunKey}', '{#AppName}');
end;
