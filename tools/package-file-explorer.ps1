<#
.SYNOPSIS
Adds the Windows 11 File Explorer commands to a published DoViFixer.App folder.

.DESCRIPTION
Publishes DoViFixer.ShellExtension with Native AOT into the folder, packs the sparse package DoViFixer.FileExplorer.msix,
signs it with the certificate in -CertificatePath and adds the certificate's public part as DoViFixer.FileExplorer.cer.
DoViFixer.App registers the package for the current user when it starts, once the PC trusts that certificate.
Without a certificate it adds nothing, and the app keeps its classic File Explorer commands.
Needs the Windows SDK (makeappx.exe, signtool.exe) and the C++ build tools that Native AOT links with.

.EXAMPLE
pwsh tools/package-file-explorer.ps1 -AppFolder out/DoViFixer.App -Version 0.1.54-ci.123 -CertificatePath cert.pfx -CertificatePassword $password
#>
param(
    [Parameter(Mandatory)] [string] $AppFolder,
    # The app's version: X.Y.Z, or X.Y.Z-ci.N for a CI build.
    [Parameter(Mandatory)] [string] $Version,
    # The PFX file tools/new-file-explorer-certificate.ps1 creates. Its subject must equal the manifest's publisher.
    [string] $CertificatePath,
    [string] $CertificatePassword
)

$ErrorActionPreference = 'Stop'
if (-not $CertificatePath -or -not (Test-Path $CertificatePath)) {
    Write-Host 'No File Explorer package certificate; the app uses its classic File Explorer commands.'
    return
}

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/DoViFixer.ShellExtension'
$AppFolder = (Resolve-Path $AppFolder).Path

# A package version has four numbers; a CI build's run number is the fourth, so each CI build replaces the last.
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)(?:-ci\.(\d+))?') { throw "Version $Version is not X.Y.Z[-ci.N]." }
$packageVersion = '{0}.{1}.{2}.{3}' -f $Matches[1], $Matches[2], $Matches[3], ($Matches[4] ?? 0)

# The X509Certificate2 constructor works in every PowerShell 7; X509CertificateLoader needs .NET 9.
$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path $CertificatePath).Path, $CertificatePassword)
$manifest = [xml](Get-Content (Join-Path $project 'Package/AppxManifest.xml') -Raw)
if ($certificate.Subject -ne $manifest.Package.Identity.Publisher) {
    throw "The certificate's subject '$($certificate.Subject)' must equal the manifest's publisher '$($manifest.Package.Identity.Publisher)'."
}

$sdk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" |
    Sort-Object { [version]$_.Directory.Parent.Name } | Select-Object -Last 1
if (-not $sdk) { throw 'makeappx.exe was not found; install the Windows SDK.' }
$signTool = Join-Path $sdk.DirectoryName 'signtool.exe'
$makePri = Join-Path $sdk.DirectoryName 'makepri.exe'

$work = Join-Path ([IO.Path]::GetTempPath()) ("dovifixer-package-" + [Guid]::NewGuid().ToString('N'))
try {
    dotnet publish $project -c Release -r win-x64 -o (Join-Path $work 'native')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Copy-Item (Join-Path $work 'native/DoViFixer.ShellExtension.dll') $AppFolder

    # The images are read from the package and, as for any sparse package, from the app's folder.
    $layout = Join-Path $work 'layout'
    New-Item -ItemType Directory $layout | Out-Null
    Copy-Item (Join-Path $project 'Package/Assets') $layout -Recurse
    Copy-Item (Join-Path $project 'Package/Assets') $AppFolder -Recurse -Force
    $manifest.Package.Identity.Version = $packageVersion
    $manifest.Save((Join-Path $layout 'AppxManifest.xml'))

    # The resource index lets Windows pick the logo drawn for each size (tools/package-icons.py), such as the
    # single-line knot File Explorer's menu shows at 16 to 32 pixels, instead of scaling one image.
    $priConfig = Join-Path $work 'priconfig.xml'
    & $makePri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $makePri new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Copy-Item (Join-Path $layout 'resources.pri') $AppFolder -Force

    $package = Join-Path $AppFolder 'DoViFixer.FileExplorer.msix'
    & $sdk.FullName pack /d $layout /p $package /nv /o
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $signTool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword $package
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # The app asks Windows to trust this public certificate before it registers the package.
    [IO.File]::WriteAllBytes((Join-Path $AppFolder 'DoViFixer.FileExplorer.cer'), $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    Write-Host "Packed and signed DoViFixer.FileExplorer.msix $packageVersion into $AppFolder"
}
finally {
    $certificate.Dispose()
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
