<#
.SYNOPSIS
Builds the Microsoft Store package of DoViFixer.App.

.DESCRIPTION
Publishes DoViFixer.App self-contained, since the Store cannot install the .NET Desktop Runtime, together with the
File Explorer commands' native DLL and the logos, and packs them with installer/Store/AppxManifest.xml into
out/DoViFixer-Store-<version>.msix. The package is not signed: Partner Center signs it when you upload it.
The Store requires the fourth version number to be 0, so a CI build X.Y.Z-ci.N packs as X.Y.Z.0.
Needs the Windows SDK (makeappx.exe, makepri.exe) and the C++ build tools that Native AOT links with.

.EXAMPLE
pwsh tools/package-store.ps1 -Version 0.1.59 -OutputFolder out
#>
param(
    # The app's version: X.Y.Z, or X.Y.Z-ci.N for a CI build.
    [Parameter(Mandatory)] [string] $Version,
    [string] $OutputFolder = 'out'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)') { throw "Version $Version is not X.Y.Z[-ci.N]." }
$packageVersion = '{0}.{1}.{2}.0' -f $Matches[1], $Matches[2], $Matches[3]
$versionProperty = if ($Version -match '-ci\.(\d+)$') { "-p:VersionSuffix=ci.$($Matches[1])" } else { "-p:Version=$Version" }

$sdk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" |
    Sort-Object { [version]$_.Directory.Parent.Name } | Select-Object -Last 1
if (-not $sdk) { throw 'makeappx.exe was not found; install the Windows SDK.' }
$makePri = Join-Path $sdk.DirectoryName 'makepri.exe'

New-Item -ItemType Directory $OutputFolder -Force | Out-Null
$OutputFolder = (Resolve-Path $OutputFolder).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ("dovifixer-store-" + [Guid]::NewGuid().ToString('N'))
try {
    $layout = Join-Path $work 'layout'
    dotnet publish (Join-Path $root 'src/DoViFixer.App/DoViFixer.App.csproj') -c Release -r win-x64 --self-contained -p:UseLocalWpfFoundation=false $versionProperty -o $layout
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet publish (Join-Path $root 'src/DoViFixer.ShellExtension') -c Release -r win-x64 -o (Join-Path $work 'native')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Copy-Item (Join-Path $work 'native/DoViFixer.ShellExtension.dll') $layout
    Copy-Item (Join-Path $root 'src/DoViFixer.ShellExtension/Package/Assets') $layout -Recurse

    $manifest = [xml](Get-Content (Join-Path $root 'installer/Store/AppxManifest.xml') -Raw)
    $manifest.Package.Identity.Version = $packageVersion
    $manifest.Save((Join-Path $layout 'AppxManifest.xml'))

    # The resource index lets Windows pick the logo drawn for each size (tools/package-icons.py).
    $priConfig = Join-Path $work 'priconfig.xml'
    & $makePri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $makePri new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $package = Join-Path $OutputFolder "DoViFixer-Store-$packageVersion.msix"
    & $sdk.FullName pack /d $layout /p $package /o
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "Packed $package for the Microsoft Store"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
