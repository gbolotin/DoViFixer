; Setup for DoViFixer.App, built with Inno Setup 6 from the published app folder:
;   ISCC.exe /DAppVersion=0.1.55 /DNumericVersion=0.1.55.0 installer\DoViFixer.iss
; It installs for all users, so it runs as administrator once. That also trusts the certificate of the
; Windows 11 File Explorer package, so the app's commands appear in Windows 11's first right-click menu on first start.
; The app creates its own Start menu shortcut at start, so notifications show its name and icon; setup adds none.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\DoViFixer.App"
#endif
#define AppName "DoViFixer"
#define AppExe "DoViFixer.App.exe"
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

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Run]
Filename: "{sys}\certutil.exe"; Parameters: "-f -addstore TrustedPeople ""{app}\{#Certificate}"""; StatusMsg: "Trusting the File Explorer package certificate..."; Flags: runhidden; Check: HasCertificate
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
; Removes the File Explorer commands, the Windows 11 package and the Start menu shortcut the app added for the user.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveAppRegistrations"
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2('{app}\{#Certificate}'); Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $c.Thumbprint | Remove-Item"""; Flags: runhidden waituntilterminated; RunOnceId: "UntrustCertificate"; Check: HasCertificate

[Code]
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
