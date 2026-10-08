# Desinstalador de DM-CLICK.
$ErrorActionPreference = 'Continue'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}

$dst = Join-Path $env:ProgramFiles 'DM-CLICK'
Write-Host ""
Write-Host "=== Desinstalando DM-CLICK ===" -ForegroundColor White

Get-Process DM-CLICK -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

foreach ($f in 'CommonPrograms', 'CommonDesktopDirectory', 'CommonStartup') {
    Remove-Item (Join-Path ([Environment]::GetFolderPath($f)) 'DM-CLICK.lnk') -ErrorAction SilentlyContinue
}
Remove-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DM-CLICK' -Recurse -ErrorAction SilentlyContinue

$r = Read-Host "Quitar tambien el driver Interception? Solo di que si si ningun otro programa lo usa (S/N)"
$reboot = $false
if ($r -match '^[sSyY]') {
    # Copiar el instalador fuera de la carpeta que vamos a borrar
    $tmp = Join-Path $env:TEMP 'install-interception.exe'
    Copy-Item (Join-Path $dst 'driver\install-interception.exe') $tmp -Force
    & $tmp /uninstall
    $reboot = $true
}

Remove-Item $dst -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "DM-CLICK desinstalado." -ForegroundColor Green
if ($reboot) {
    $r = Read-Host "Reiniciar ahora para terminar de quitar el driver? (S/N)"
    if ($r -match '^[sSyY]') { Restart-Computer -Force }
}
Read-Host "Presiona Enter para cerrar" | Out-Null
