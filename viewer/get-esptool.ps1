# Downloads Espressif's esptool for Windows into viewer\esptool, where the
# viewer's build picks it up for Flash firmware. Run it once from any folder:
#
#     powershell -ExecutionPolicy Bypass -File viewer\get-esptool.ps1
#
# esptool is GPL-2.0, from https://github.com/espressif/esptool. Nothing else
# in this repository is built from it; it's run as a separate program.
param([string]$Version = "v5.6.0")

$ErrorActionPreference = "Stop"
$dest = Join-Path $PSScriptRoot "esptool"
$zip = Join-Path ([IO.Path]::GetTempPath()) "esptool-$Version-windows-amd64.zip"
$unpack = Join-Path ([IO.Path]::GetTempPath()) "esptool-$Version-unpack"
$url = "https://github.com/espressif/esptool/releases/download/$Version/esptool-$Version-windows-amd64.zip"

Write-Host "Downloading $url"
Invoke-WebRequest -Uri $url -OutFile $zip
if (Test-Path $unpack) { Remove-Item -Recurse -Force $unpack }
Expand-Archive -Path $zip -DestinationPath $unpack
$exe = Get-ChildItem -Path $unpack -Recurse -Filter esptool.exe | Select-Object -First 1
if (-not $exe) { throw "esptool.exe wasn't in the zip" }

if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
New-Item -ItemType Directory -Path $dest | Out-Null
# Only esptool itself and its licence: the other tools (espefuse, espsecure)
# aren't needed, and espefuse can change the chip for good.
foreach ($name in "esptool.exe", "LICENSE", "README.md") {
    $file = Join-Path $exe.DirectoryName $name
    if (Test-Path $file) { Copy-Item -Path $file -Destination $dest }
}
Remove-Item -Force $zip
Remove-Item -Recurse -Force $unpack
Write-Host "esptool is in $dest"
& (Join-Path $dest "esptool.exe") version
