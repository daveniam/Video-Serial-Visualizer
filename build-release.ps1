# Genera una release instalable y actualizable de Video Serial Visualizer.
#
# Uso:
#   .\build-release.ps1                 -> usa la <Version> del .csproj
#   .\build-release.ps1 -Version 1.1.0  -> fuerza una version puntual
#
# Produce en .\Releases:
#   - VideoSerialVisualizer-win-Setup.exe  (instalador para distribuir la primera vez)
#   - *-full.nupkg / *-delta.nupkg         (paquetes que consume el auto-updater)
#   - RELEASES                             (indice del feed)
#
# Para publicar una actualizacion:
#   1. Subi la version en <Version> del .csproj y commiteala.
#   2. Corre este script.
#   3. Crea un Release en GitHub (repo: UpdateService.GithubRepoUrl) con tag "v<version>" y
#      adjunta como assets TODO el contenido de .\Releases.
#
# Los .nupkg delta se calculan contra las versiones anteriores, asi que NO borres los assets de
# releases viejas: Velopack los necesita para armar la actualizacion incremental.
#
# Los usuarios instalados la reciben solos al abrir la app (ver UpdateService).

param(
    [string]$Version
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "VideoSerialVisualizer\VideoSerialVisualizer.csproj"
$publishDir  = Join-Path $PSScriptRoot "VideoSerialVisualizer\bin\Release\net8.0-windows\win-x64\publish"
$releasesDir = Join-Path $PSScriptRoot "Releases"
$iconPath    = Join-Path $PSScriptRoot "VideoSerialVisualizer\Assets\AppIcon.ico"

# Si no se paso -Version, se lee la del .csproj para no tener la version duplicada en dos lugares.
if (-not $Version) {
    [xml]$csproj = Get-Content $projectPath
    $Version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
    if (-not $Version) {
        throw "No se encontro <Version> en el .csproj. Pasala con -Version 1.0.0"
    }
}

Write-Host "Empaquetando Video Serial Visualizer $Version" -ForegroundColor Cyan

# Publicado limpio: si quedaran archivos de un build anterior, terminarian dentro del instalador.
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

Write-Host "`n[1/2] dotnet publish..." -ForegroundColor Cyan
dotnet publish $projectPath -c Release
if ($LASTEXITCODE -ne 0) { throw "Fallo dotnet publish" }

Write-Host "`n[2/2] vpk pack..." -ForegroundColor Cyan
vpk pack `
    --packId VideoSerialVisualizer `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe VideoSerialVisualizer.exe `
    --packTitle "Video Serial Visualizer" `
    --packAuthors "David Nieves" `
    --icon $iconPath `
    --outputDir $releasesDir
if ($LASTEXITCODE -ne 0) { throw "Fallo vpk pack" }

Write-Host "`nListo. Archivos en: $releasesDir" -ForegroundColor Green
Write-Host "Instalador: VideoSerialVisualizer-win-Setup.exe" -ForegroundColor Green
Write-Host ""
Write-Host "Recordatorio: cuando tengas el certificado, agregale a vpk pack el parametro" -ForegroundColor Yellow
Write-Host "  --signTemplate 'signtool sign /fd sha256 /tr <URL-timestamp> /td sha256 /f <cert.pfx> /p <pass> {{file}}'" -ForegroundColor Yellow
Write-Host "para que el instalador y el exe salgan firmados y no los frene SmartScreen." -ForegroundColor Yellow
