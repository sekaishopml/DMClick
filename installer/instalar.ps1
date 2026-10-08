# Instalador de DM-CLICK para Windows 10 / 11 (64 bits).
# Verifica lo que ya esta instalado y solo instala lo que falta.
param([switch]$SinInicioAutomatico)

$ErrorActionPreference = 'Stop'

# --- Autoelevar a administrador ---
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    $extra = if ($SinInicioAutomatico) { '-SinInicioAutomatico' } else { '' }
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" $extra"
    exit
}

function Ok($t)   { Write-Host "  [OK] $t" -ForegroundColor Green }
function Info($t) { Write-Host "  [..] $t" -ForegroundColor Cyan }
function Bad($t)  { Write-Host "  [X]  $t" -ForegroundColor Red }
function Finish($code) { Write-Host ""; Read-Host "Presiona Enter para cerrar" | Out-Null; exit $code }

$src  = $PSScriptRoot
$dst  = Join-Path $env:ProgramFiles 'DM-CLICK'
$exe  = Join-Path $dst 'DM-CLICK.exe'
$mouseClass = 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4D36E96F-E325-11CE-BFC1-08002BE10318}'

Write-Host ""
Write-Host "=== Instalando DM-CLICK ===" -ForegroundColor White
Write-Host ""

try {
    # --- 1. Requisitos del sistema ---
    $os = [Environment]::OSVersion.Version
    if ($os.Major -lt 10) { Bad "Se requiere Windows 10 u 11 (detectado $os)."; Finish 1 }
    if ($env:PROCESSOR_ARCHITECTURE -ne 'AMD64' -and $env:PROCESSOR_ARCHITEW6432 -ne 'AMD64') {
        Bad "Se requiere Windows de 64 bits x64 (detectado $env:PROCESSOR_ARCHITECTURE). ARM no es compatible con el driver."
        Finish 1
    }
    $build = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
    Ok "Windows compatible (build $build, x64)"

    # Archivos descargados de internet: quitar la marca de bloqueo
    Get-ChildItem $src -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

    # --- 2. Programa ---
    Get-Process DM-CLICK -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 300
    New-Item -ItemType Directory -Force $dst | Out-Null
    Copy-Item (Join-Path $src 'app\*') $dst -Recurse -Force
    New-Item -ItemType Directory -Force (Join-Path $dst 'driver') | Out-Null
    Copy-Item (Join-Path $src 'driver\*') (Join-Path $dst 'driver') -Recurse -Force
    Copy-Item (Join-Path $src 'desinstalar.ps1') $dst -Force
    Ok "Programa copiado en $dst (incluye .NET, no hace falta instalarlo)"

    # --- 3. Driver Interception ---
    $filters = (Get-ItemProperty $mouseClass -ErrorAction SilentlyContinue).UpperFilters
    $driverInstalled = $filters -contains 'mouse'
    $svc = Get-Service mouse -ErrorAction SilentlyContinue
    $driverRunning = $driverInstalled -and $svc -and $svc.Status -eq 'Running'
    $needReboot = $false

    if ($driverRunning) {
        Ok "Driver Interception ya instalado y activo"
    } elseif ($driverInstalled) {
        Info "Driver Interception instalado pero inactivo: falta reiniciar"
        $needReboot = $true
    } else {
        Info "Instalando driver Interception..."
        $out = & (Join-Path $src 'driver\install-interception.exe') /install 2>&1 | Out-String
        $filters = (Get-ItemProperty $mouseClass).UpperFilters
        if ($filters -contains 'mouse') {
            Ok "Driver Interception instalado"
            $needReboot = $true
        } else {
            Bad "No se pudo instalar el driver:"
            Write-Host $out
            Finish 1
        }
    }

    # --- 4. Accesos directos ---
    $shell = New-Object -ComObject WScript.Shell
    function New-Shortcut($path, $arguments) {
        $s = $shell.CreateShortcut($path)
        $s.TargetPath = $exe
        $s.Arguments = $arguments
        $s.WorkingDirectory = $dst
        $s.Description = 'DM-CLICK: dos mouse, dos punteros'
        $s.Save()
    }
    $commonPrograms = [Environment]::GetFolderPath('CommonPrograms')
    $commonDesktop  = [Environment]::GetFolderPath('CommonDesktopDirectory')
    $commonStartup  = [Environment]::GetFolderPath('CommonStartup')
    New-Shortcut (Join-Path $commonPrograms 'DM-CLICK.lnk') ''
    New-Shortcut (Join-Path $commonDesktop 'DM-CLICK.lnk') ''
    Ok "Accesos directos en Escritorio y menu Inicio"

    $startupLnk = Join-Path $commonStartup 'DM-CLICK.lnk'
    if ($SinInicioAutomatico) {
        Remove-Item $startupLnk -ErrorAction SilentlyContinue
    } else {
        New-Shortcut $startupLnk ''
        Ok "Se iniciara automaticamente al entrar a Windows"
    }

    # --- 5. Registrar en "Aplicaciones instaladas" para poder desinstalar desde Configuracion ---
    $un = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DM-CLICK'
    New-Item -Force $un | Out-Null
    Set-ItemProperty $un DisplayName 'DM-CLICK'
    Set-ItemProperty $un DisplayVersion '1.1.0'
    Set-ItemProperty $un Publisher 'DM-CLICK'
    Set-ItemProperty $un DisplayIcon $exe
    Set-ItemProperty $un InstallLocation $dst
    Set-ItemProperty $un UninstallString "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$dst\desinstalar.ps1`""
    Set-ItemProperty $un NoModify 1 -Type DWord
    Set-ItemProperty $un NoRepair 1 -Type DWord
    Ok "Registrado en Configuracion > Aplicaciones"

    Write-Host ""
    if ($needReboot) {
        Write-Host "Listo. Hay que REINICIAR Windows para activar el driver." -ForegroundColor Yellow
        Write-Host "Despues del reinicio DM-CLICK arranca solo (o abrelo desde el Escritorio)."
        $r = Read-Host "Reiniciar ahora? (S/N)"
        if ($r -match '^[sSyY]') { Restart-Computer -Force; exit 0 }
        Finish 0
    } else {
        # Abrir sin privilegios de administrador (como el usuario normal)
        Start-Process explorer.exe "`"$exe`""
        Write-Host "Listo. DM-CLICK ya esta corriendo (icono en la bandeja)." -ForegroundColor Green
        Write-Host "Mueve primero tu mouse PRINCIPAL; el otro tendra la flecha naranja."
        Finish 0
    }
}
catch {
    Bad $_.Exception.Message
    Finish 1
}
