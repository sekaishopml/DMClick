# Genera dist\DM-CLICK-Setup (carpeta) y dist\DM-CLICK-Setup.zip para llevar a otra PC.
param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out  = Join-Path $root 'dist\DM-CLICK-Setup'
$pub  = Join-Path $root 'obj\publish'
$ic   = Join-Path $root 'third_party\Interception'

Remove-Item (Join-Path $root 'dist') -Recurse -Force -ErrorAction SilentlyContinue
& $Dotnet publish (Join-Path $root 'DualMouse.csproj') -c Paquete -o $pub
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallo" }

New-Item -ItemType Directory -Force "$out\app", "$out\driver" | Out-Null
Copy-Item "$pub\DM-CLICK.exe", "$pub\interception.dll" "$out\app"
Copy-Item "$ic\command line installer\install-interception.exe" "$out\driver"
Copy-Item "$ic\licenses\non-commercial-usage\LGPL 3.0.txt" "$out\driver\LICENCIA-Interception-LGPL3.txt"
Copy-Item "$root\installer\*" $out

Compress-Archive -Path $out -DestinationPath (Join-Path $root 'dist\DM-CLICK-Setup.zip') -Force
Write-Host "Paquete listo: $(Join-Path $root 'dist\DM-CLICK-Setup.zip')"
