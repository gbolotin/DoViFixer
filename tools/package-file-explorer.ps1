<#
.SYNOPSIS
Adds the Windows 11 File Explorer commands to a published DoViFixer.App folder.

.DESCRIPTION
Publishes DoViFixer.ShellExtension with Native AOT into the folder and packs the unsigned sparse package
DoViFixer.FileExplorer.msix next to it. DoViFixer.App registers that package for the current user when it starts.
Needs the Windows SDK (makeappx.exe) and the C++ build tools that Native AOT links with.

.EXAMPLE
pwsh tools/package-file-explorer.ps1 -AppFolder out/DoViFixer.App -Version 0.1.52-ci.123
#>
param(
    [Parameter(Mandatory)] [string] $AppFolder,
    # The app's version: X.Y.Z, or X.Y.Z-ci.N for a CI build.
    [Parameter(Mandatory)] [string] $Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/DoViFixer.ShellExtension'
$AppFolder = (Resolve-Path $AppFolder).Path

# A package version has four numbers; a CI build's run number is the fourth, so each CI build replaces the last.
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)(?:-ci\.(\d+))?') { throw "Version $Version is not X.Y.Z[-ci.N]." }
$packageVersion = '{0}.{1}.{2}.{3}' -f $Matches[1], $Matches[2], $Matches[3], ($Matches[4] ?? 0)

$work = Join-Path ([IO.Path]::GetTempPath()) ("dovifixer-package-" + [Guid]::NewGuid().ToString('N'))
try {
    dotnet publish $project -c Release -r win-x64 -o (Join-Path $work 'native') -p:UseLocalWpfFoundation=false
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Copy-Item (Join-Path $work 'native/DoViFixer.ShellExtension.dll') $AppFolder

    # The images are read from the package and, as for any sparse package, from the app's folder.
    $layout = Join-Path $work 'layout'
    New-Item -ItemType Directory $layout | Out-Null
    Copy-Item (Join-Path $project 'Package/Assets') $layout -Recurse
    Copy-Item (Join-Path $project 'Package/Assets') $AppFolder -Recurse -Force
    $manifest = [xml](Get-Content (Join-Path $project 'Package/AppxManifest.xml') -Raw)
    $manifest.Package.Identity.Version = $packageVersion
    $manifest.Save((Join-Path $layout 'AppxManifest.xml'))

    $makeAppx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" |
        Sort-Object { [version]$_.Directory.Parent.Name } | Select-Object -Last 1
    if (-not $makeAppx) { throw 'makeappx.exe was not found; install the Windows SDK.' }
    & $makeAppx.FullName pack /d $layout /p (Join-Path $AppFolder 'DoViFixer.FileExplorer.msix') /nv /o
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "Packed DoViFixer.FileExplorer.msix $packageVersion into $AppFolder"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
