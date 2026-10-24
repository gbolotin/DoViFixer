; Setup for DoViFixer.App, built with Inno Setup 6 from the published app folder:
;   ISCC.exe /DAppVersion=0.1.55 /DNumericVersion=0.1.55.0 installer\DoViFixer.iss
; It installs for all users, so it runs as administrator once. That also trusts the certificate of the
; Windows 11 File Explorer package, so the app's commands appear in Windows 11's first right-click menu on first start.
; The app creates its own Start menu shortcut at start, so notifications show its name and icon; setup adds none.
; After copying the files, setup asks the new app which settings and media library of a previous installation it can read.
; It deletes those the new version cannot read and asks whether to keep the others. Uninstall asks whether to delete the library.
; The MCP server and the command-line version are optional components in their own subfolders. Setup adds the MCP server to
; the AI assistants the user ticks, preselecting those it finds, and can add the command-line version to the system PATH.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\DoViFixer.App"
#endif
#ifndef McpDir
  #define McpDir "..\out\DoViFixer.Mcp"
#endif
#ifndef ConsoleDir
  #define ConsoleDir "..\out\DoViFixer.Console"
#endif
#define AppName "DoViFixer"
#define AppExe "DoViFixer.App.exe"
#define McpExe "DoViFixer.Mcp.exe"
#define EnvironmentKey "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"
#define Certificate "DoViFixer.FileExplorer.cer"

[Setup]
; Never change the AppId: Windows recognizes upgrades and the uninstall entry by it.
AppId={{9EF67CDE-C0C1-481F-BDC0-B51A53D07816}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#NumericVersion}
AppPublisher=gbolotin
AppPublisherURL=https://github.com/gbolotin/DoViFixer
AppSupportURL=https://gbolotin.github.io/DoViFixer/
AppUpdatesURL=https://github.com/gbolotin/DoViFixer/releases/latest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
LicenseFile=..\LICENSE
SetupIconFile=..\src\DoViFixer.App\dovifixer.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\out
OutputBaseFilename=DoViFixer-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Setup and uninstall close a running DoViFixer first.
CloseApplications=yes
; The command-line version can go on the system PATH.
ChangesEnvironment=yes

[Types]
Name: "full"; Description: "Desktop app, MCP server and command line"
Name: "app"; Description: "Desktop app only"
Name: "custom"; Description: "Custom"; Flags: iscustom

[Components]
Name: "app"; Description: "Desktop app"; Types: full app custom; Flags: fixed
Name: "mcp"; Description: "MCP server for AI assistants"; Types: full
Name: "console"; Description: "Command-line version"; Types: full

[Tasks]
Name: "claudedesktop"; Description: "Claude Desktop"; GroupDescription: "Add the MCP server to these AI assistants:"; Components: mcp; Flags: unchecked
Name: "claudecode"; Description: "Claude Code"; GroupDescription: "Add the MCP server to these AI assistants:"; Components: mcp; Flags: unchecked
Name: "codex"; Description: "Codex (app and CLI)"; GroupDescription: "Add the MCP server to these AI assistants:"; Components: mcp; Flags: unchecked
Name: "hermes"; Description: "Hermes Agent"; GroupDescription: "Add the MCP server to these AI assistants:"; Components: mcp; Flags: unchecked
Name: "path"; Description: "Add the command-line version to PATH"; GroupDescription: "Command line:"; Components: console

[InstallDelete]
; A component the user no longer wants goes; Inno Setup would otherwise leave its old files.
Type: filesandordirs; Name: "{app}\Mcp"; Components: not mcp
Type: filesandordirs; Name: "{app}\Console"; Components: not console

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Components: app; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#McpDir}\*"; DestDir: "{app}\Mcp"; Components: mcp; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ConsoleDir}\*"; DestDir: "{app}\Console"; Components: console; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKLM; Subkey: "{#EnvironmentKey}"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}\Console"; Tasks: path; Check: NeedsConsoleOnPath

[Run]
Filename: "{sys}\certutil.exe"; Parameters: "-f -addstore TrustedPeople ""{app}\{#Certificate}"""; StatusMsg: "Trusting the File Explorer package certificate..."; Flags: runhidden; Check: HasCertificate
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
; Removes the MCP server from every AI assistant's configuration; the rest of each configuration stays.
Filename: "{app}\Mcp\{#McpExe}"; Parameters: "--unregister"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveMcpServer"; Check: HasMcpServer
; Removes the File Explorer commands, the Windows 11 package and the Start menu shortcut the app added for the user.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveAppRegistrations"
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2('{app}\{#Certificate}'); Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $c.Thumbprint | Remove-Item"""; Flags: runhidden waituntilterminated; RunOnceId: "UntrustCertificate"; Check: HasCertificate

[Code]
// The parts of the user's data the app reports and removes; see App.UserDataAsync.
const
  UserDataSettings = 1;
  UserDataLibrary = 2;

function HasMcpServer: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\Mcp\{#McpExe}'));
end;

function HasCertificate: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\{#Certificate}'));
end;

// The .NET installers list each runtime version under this key.
function IsDesktopRuntimeInstalled: Boolean;
var
  Versions: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Versions) then
    for I := 0 to GetArrayLength(Versions) - 1 do
      if Copy(Versions[I], 1, 3) = '10.' then
        Result := True;
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDesktopRuntimeInstalled then
  begin
    if SuppressibleMsgBox('{#AppName} needs the .NET 10 Desktop Runtime (x64), which is not installed.' + #13#10#13#10 +
      'Open the download page now? Setup continues; install the runtime before you start {#AppName}.',
      mbConfirmation, MB_YESNO, IDNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;

function DescribeUserData(Parts: Integer): String;
begin
  Result := '';
  if Parts and UserDataSettings <> 0 then
    Result := 'settings and presets';
  if Parts and UserDataLibrary <> 0 then
  begin
    if Result <> '' then
      Result := Result + ' and ';
    Result := Result + 'media library (scanned files, analysis and history)';
  end;
end;

function UserDataArguments(Parts: Integer): String;
begin
  Result := '--remove-user-data';
  if Parts and UserDataSettings <> 0 then
    Result := Result + ' settings';
  if Parts and UserDataLibrary <> 0 then
    Result := Result + ' library';
end;

// The data is in the user's profile, so setup runs the app as the user who started setup, not as administrator.
procedure RemoveUserData(Parts: Integer);
var
  ResultCode: Integer;
begin
  if not ExecAsOriginalUser(ExpandConstant('{app}\{#AppExe}'), UserDataArguments(Parts), '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    SuppressibleMsgBox('Setup could not delete your previous ' + DescribeUserData(Parts) + '.' + #13#10#13#10 +
      'Delete them from %LOCALAPPDATA%\{#AppName} before you start {#AppName}.', mbError, MB_OK, IDOK);
end;

procedure CheckPreviousUserData;
var
  ResultCode, Compatible, Incompatible: Integer;
begin
  // The new app answers with the parts it can read in bits 0-1 and those it cannot in bits 2-3. Without an answer, for
  // example while the .NET runtime is missing, setup keeps everything.
  if not ExecAsOriginalUser(ExpandConstant('{app}\{#AppExe}'), '--check-user-data', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or
    (ResultCode < 0) or (ResultCode > 15) then
  begin
    Log(Format('Could not check the data of a previous installation (exit code %d); keeping it.', [ResultCode]));
    Exit;
  end;

  Compatible := ResultCode and 3;
  Incompatible := (ResultCode shr 2) and 3;
  if Incompatible <> 0 then
  begin
    SuppressibleMsgBox('Your ' + DescribeUserData(Incompatible) + ' from a previous installation cannot be used by {#AppName} {#AppVersion}.' + #13#10#13#10 +
      'Setup deletes them.', mbInformation, MB_OK, IDOK);
    RemoveUserData(Incompatible);
  end;

  if Compatible <> 0 then
    if SuppressibleMsgBox('Setup found your ' + DescribeUserData(Compatible) + ' from a previous installation. {#AppName} {#AppVersion} can use them.' + #13#10#13#10 +
      'Keep them? Click No to start with new ones.', mbConfirmation, MB_YESNO, IDYES) = IDNO then
      RemoveUserData(Compatible);
end;

// The system PATH as a list with a semicolon at both ends, so each entry can be found as ';entry;'.
function SystemPath: String;
begin
  if not RegQueryStringValue(HKLM, '{#EnvironmentKey}', 'Path', Result) then
    Result := '';
  Result := ';' + Result + ';';
end;

function NeedsConsoleOnPath: Boolean;
begin
  Result := Pos(';' + Uppercase(ExpandConstant('{app}\Console')) + ';', Uppercase(SystemPath)) = 0;
end;

procedure RemoveConsoleFromPath;
var
  Value, Entry: String;
  At: Integer;
begin
  Value := SystemPath;
  Entry := ';' + ExpandConstant('{app}\Console') + ';';
  At := Pos(Uppercase(Entry), Uppercase(Value));
  if At = 0 then
    Exit;
  Delete(Value, At, Length(Entry) - 1);
  RegWriteExpandStringValue(HKLM, '{#EnvironmentKey}', 'Path', Copy(Value, 2, Length(Value) - 2));
end;

// The assistants' names as the MCP server's --register and --unregister take them, for the ticked or unticked tasks.
function Assistants(Ticked: Boolean): String;
begin
  Result := '';
  if WizardIsTaskSelected('claudedesktop') = Ticked then
    Result := Result + ' claude-desktop';
  if WizardIsTaskSelected('claudecode') = Ticked then
    Result := Result + ' claude-code';
  if WizardIsTaskSelected('codex') = Ticked then
    Result := Result + ' codex';
  if WizardIsTaskSelected('hermes') = Ticked then
    Result := Result + ' hermes';
end;

// The configurations are in the user's profile, so the MCP server runs as the user who started setup.
procedure UpdateAssistants;
var
  ResultCode: Integer;
  Ticked, Unticked: String;
begin
  Unticked := Assistants(False);
  if Unticked <> '' then
    ExecAsOriginalUser(ExpandConstant('{app}\Mcp\{#McpExe}'), '--unregister' + Unticked, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Ticked := Assistants(True);
  if (Ticked <> '') and (not ExecAsOriginalUser(ExpandConstant('{app}\Mcp\{#McpExe}'), '--register' + Ticked, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0)) then
    SuppressibleMsgBox('Setup could not add the MCP server to every AI assistant you chose.' + #13#10#13#10 +
      'Add ' + ExpandConstant('{app}\Mcp\{#McpExe}') + ' to the assistant yourself; the {#AppName} documentation shows how.', mbError, MB_OK, IDOK);
end;

function AssistantFound(const Folder: String): Boolean;
begin
  Result := (Folder <> '') and DirExists(Folder);
end;

function ClaudeStorePackageFound: Boolean;
var
  Found: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{localappdata}\Packages\Claude_*'), Found);
  if Result then
    FindClose(Found);
end;

function HomeFolder(const Variable, Fallback: String): String;
begin
  Result := GetEnv(Variable);
  if Result = '' then
    Result := AddBackslash(GetEnv('USERPROFILE')) + Fallback;
end;

var
  AssistantsPreselected: Boolean;

// Ticks the assistants installed on this PC the first time setup offers them; later runs keep the user's choice.
procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID <> wpSelectTasks) or AssistantsPreselected or (GetPreviousData('AssistantsOffered', '') <> '') then
    Exit;
  AssistantsPreselected := True;
  if AssistantFound(ExpandConstant('{userappdata}\Claude')) or ClaudeStorePackageFound then
    WizardSelectTasks('claudedesktop');
  if FileExists(AddBackslash(GetEnv('USERPROFILE')) + '.claude.json') or AssistantFound(AddBackslash(GetEnv('USERPROFILE')) + '.claude') then
    WizardSelectTasks('claudecode');
  if AssistantFound(HomeFolder('CODEX_HOME', '.codex')) then
    WizardSelectTasks('codex');
  if AssistantFound(HomeFolder('HERMES_HOME', '.hermes')) then
    WizardSelectTasks('hermes');
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  if WizardIsComponentSelected('mcp') then
    SetPreviousData(PreviousDataKey, 'AssistantsOffered', 'yes');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  // Before [InstallDelete] removes an MCP server the user no longer wants, it leaves the assistants it was added to.
  if (CurStep = ssInstall) and not WizardIsComponentSelected('mcp') and HasMcpServer then
    ExecAsOriginalUser(ExpandConstant('{app}\Mcp\{#McpExe}'), '--unregister', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if CurStep = ssPostInstall then
  begin
    CheckPreviousUserData;
    if WizardIsComponentSelected('mcp') then
      UpdateAssistants;
    if not WizardIsTaskSelected('path') then
      RemoveConsoleFromPath;
  end;
end;

// Before uninstall removes the app, which deletes the library for the user. Settings and downloaded tools stay.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usPostUninstall then
    RemoveConsoleFromPath;
  if CurUninstallStep <> usUninstall then
    Exit;
  if SuppressibleMsgBox('Also delete your {#AppName} media library?' + #13#10#13#10 +
    'It holds the files you scanned, their analysis and history. Click No to keep it for a later installation.',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
    if not Exec(ExpandConstant('{app}\{#AppExe}'), UserDataArguments(UserDataLibrary), '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      SuppressibleMsgBox('The media library could not be deleted. Delete library.db and the cache folder from %LOCALAPPDATA%\{#AppName} yourself.',
        mbError, MB_OK, IDOK);
end;
