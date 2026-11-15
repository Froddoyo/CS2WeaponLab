# Packs a release build: publish\CS2WeaponLab-<version>-<runtime>\ and a .zip of it.
#
# The folder holds CS2WeaponLab.exe (the app), weaponlab.exe (the command-line
# tool the Blender add-on runs to export clips) and blender\weaponlab_bridge\.
# Both exes are self-contained single files: nothing else to install.
#
# Usage: powershell -File build\publish.ps1 [-Runtime win-x64]
param([string]$Runtime = "win-x64")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version
$name = "CS2WeaponLab-$version-$Runtime"
$publish = Join-Path $root "publish"
$staging = Join-Path $publish "staging"
$out = Join-Path $publish $name
$zip = "$out.zip"

$single = @(
    "-c", "Release", "-r", $Runtime, "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=none"
)

foreach ($path in @($staging, $out, $zip)) {
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

# Each exe is published on its own: the app's publish would otherwise drop a
# weaponlab.exe that expects its dll beside it, and that dll is inside the app.
Write-Host "Publishing the app ($name)..."
& dotnet publish (Join-Path $root "src\WeaponLab.App\WeaponLab.App.csproj") @single -o (Join-Path $staging "app")
if ($LASTEXITCODE -ne 0) { throw "Publishing the app failed." }

Write-Host "Publishing the command-line tool..."
& dotnet publish (Join-Path $root "src\WeaponLab.Cli\WeaponLab.Cli.csproj") @single -o (Join-Path $staging "cli")
if ($LASTEXITCODE -ne 0) { throw "Publishing the command-line tool failed." }

New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $staging "app\CS2WeaponLab.exe") $out
Copy-Item (Join-Path $staging "cli\weaponlab.exe") $out
Copy-Item (Join-Path $staging "app\blender") $out -Recurse
Copy-Item (Join-Path $root "LICENSE") $out
Remove-Item $staging -Recurse -Force

Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
$size = "{0:N0} MB" -f ((Get-Item $zip).Length / 1MB)
Write-Host "Done: $out"
Write-Host "      $zip ($size)"
