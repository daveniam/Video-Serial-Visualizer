# Genera el instalador clasico (Inno Setup) de Video Serial Visualizer, con selector de carpeta.
#
# Uso:
#   .\build-installer.ps1                 -> usa la <Version> del .csproj
#   .\build-installer.ps1 -Version 1.2.0  -> fuerza una version puntual
#
# Produce en .\Releases\inno\VideoSerialVisualizer-Setup.exe
#
# A diferencia de build-release.ps1 (Velopack, auto-actualizable, carpeta fija en %LocalAppData%),
# este instalador DEJA ELEGIR la carpeta de instalacion pero NO se auto-actualiza: para actualizar
# se descarga y se corre el nuevo Setup, que reinstala en el lugar.
#
# Requiere Inno Setup 6 (el compilador ISCC.exe). Si no esta, instalalo con:
#   winget install --id JRSoftware.InnoSetup -e

param(
    [string]$Version
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "VideoSerialVisualizer\VideoSerialVisualizer.csproj"
$publishDir  = Join-Path $PSScriptRoot "VideoSerialVisualizer\bin\Release\net8.0-windows\win-x64\publish"
$issPath     = Join-Path $PSScriptRoot "installer\VideoSerialVisualizer.iss"
$outputDir   = Join-Path $PSScriptRoot "Releases\inno"

# Version: del .csproj si no se paso -Version, para no duplicarla en dos lugares.
if (-not $Version) {
    [xml]$csproj = Get-Content $projectPath
    $Version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
    if (-not $Version) {
        throw "No se encontro <Version> en el .csproj. Pasala con -Version 1.2.0"
    }
}

# Ubicar el compilador de Inno Setup (ISCC.exe): PATH o las rutas de instalacion tipicas.
$iscc = $null
$fromPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($fromPath) {
    $iscc = $fromPath.Source
} else {
    foreach ($c in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $c) { $iscc = $c; break }
    }
}
if (-not $iscc) {
    throw "No se encontro ISCC.exe (Inno Setup 6). Instalalo con: winget install --id JRSoftware.InnoSetup -e"
}

Write-Host "Compilando instalador de Video Serial Visualizer $Version" -ForegroundColor Cyan

# Publicado limpio: si quedaran archivos de un build anterior, terminarian dentro del instalador.
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

Write-Host "`n[1/2] dotnet publish..." -ForegroundColor Cyan
dotnet publish $projectPath -c Release
if ($LASTEXITCODE -ne 0) { throw "Fallo dotnet publish" }

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

Write-Host "`n[2/2] Inno Setup (ISCC)..." -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" $issPath
if ($LASTEXITCODE -ne 0) { throw "Fallo la compilacion de Inno Setup" }

$setup = Join-Path $outputDir "VideoSerialVisualizer-Setup.exe"
if (-not (Test-Path $setup)) { throw "No se genero el instalador esperado: $setup" }

$sizeMb = [math]::Round((Get-Item $setup).Length / 1MB, 0)
Write-Host "`nListo." -ForegroundColor Green
Write-Host ("  Instalador: {0} ({1} MB)" -f $setup, $sizeMb) -ForegroundColor Green
Write-Host "  Deja elegir la carpeta de instalacion. No se auto-actualiza." -ForegroundColor DarkGray
