# Instala DM-CLICK a partir del codigo clonado de GitHub.
# 1) Verifica/instala .NET 8 SDK  2) Compila el paquete  3) Ejecuta el instalador normal.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Ok($t)   { Write-Host "  [oki] $t" -ForegroundColor Green }
function Info($t) { Write-Host "  [..] $t" -ForegroundColor Cyan }
function Bad($t)  { Write-Host "  [X]  $t" -ForegroundColor Red; Read-Host "Presiona Enter para cerrar" | Out-Null; exit 1 }

function Find-Dotnet {
    $candidates = @(
        (Get-Command dotnet -ErrorAction SilentlyContinue).Source,
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
    ) | Where-Object { $_ -and (Test-Path $_) }
    foreach ($d in $candidates) {
        if (& $d --list-sdks 2>$null | Select-String '^8\.') { return $d }
    }
    return $null
}

Write-Host ""
Write-Host "DM-CLICK: compilando..." -ForegroundColor White
Write-Host ""

# --- .NET 8 SDK (solo para compilar) ---
$dotnet = Find-Dotnet
if ($dotnet) {
    Ok ".NET 8 SDK instalado..."
} else {
    Info "Instalando .NET 8 SDK...."
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        winget install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements --accept-source-agreements --silent | Out-Host
        $dotnet = Find-Dotnet
    }
    if (-not $dotnet) {
        Info "Probando con el instalador oficial"
        $tmp = Join-Path $env:TEMP 'dotnet-install.ps1'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $tmp -UseBasicParsing
        & $tmp -Channel 8.0 -InstallDir (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet') | Out-Host
        $dotnet = Find-Dotnet
    }
    if (-not $dotnet) { Bad "No se pudo instalar .NET 8 SDK. Revisa tu conexion a internet." }
    Ok ".NET 8 SDK instalado"
}

# En instalaciones nuevas a veces no viene configurado nuget.org
$sources = & $dotnet nuget list source | Out-String
if ($sources -notmatch 'nuget\.org') {
    & $dotnet nuget add source 'https://api.nuget.org/v3/index.json' -n nuget.org | Out-Null
    Ok "Fuente de paquetes agregada"
}

# --- Compilar ---
Info "Compilando mas cosas..."
& (Join-Path $root 'CREAR-PAQUETE.ps1') -Dotnet $dotnet | Out-Null
$setup = Join-Path $root 'dist\DM-CLICK-Setup\instalar.ps1'
if (-not (Test-Path $setup)) { Bad "La compilacion fallo, ya fue." }
Ok "Compilado =)"

# --- Instalar (pide administrador en otra ventana) ---
Info "Abriendo el instalador (aceptar el aviso de administrador)..."
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $setup
