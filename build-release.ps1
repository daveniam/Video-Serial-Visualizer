# Genera una release instalable y actualizable de Video Serial Visualizer.
#
# Uso:
#   .\build-release.ps1                 -> usa la <Version> del .csproj, sin firmar
#   .\build-release.ps1 -Version 1.1.0  -> fuerza una version puntual
#   .\build-release.ps1 -Sign           -> firma con SignPath (requiere el token, ver abajo)
#
# Produce en .\Releases:
#   - VideoSerialVisualizer-win-Setup.exe  (instalador para distribuir la primera vez)
#   - *-full.nupkg / *-delta.nupkg         (paquetes que consume el auto-updater)
#   - RELEASES                             (indice del feed)
#
# Para publicar una actualizacion:
#   1. Subi la version en <Version> del .csproj y commiteala.
#   2. Corre este script (con -Sign si vas a publicarla).
#   3. Crea un Release en GitHub (repo: UpdateService.GithubRepoUrl) con tag "v<version>" y
#      adjunta como assets TODO el contenido de .\Releases.
#
# Los .nupkg delta se calculan contra las versiones anteriores, asi que NO borres los assets de
# releases viejas: Velopack los necesita para armar la actualizacion incremental.
#
# Los usuarios instalados la reciben solos al abrir la app (ver UpdateService).
#
# ---------------------------------------------------------------------------------------------
# FIRMA DE CODIGO (SignPath)
#
# El token NUNCA va escrito en este archivo ni se commitea. Se toma de la variable de entorno
# SIGNPATH_API_TOKEN, o se pasa con -SignPathApiToken. Para dejarlo cargado en la sesion actual:
#
#     $env:SIGNPATH_API_TOKEN = "<token de la pagina de user details>"
#     .\build-release.ps1 -Sign
#
# Requiere el modulo de PowerShell de SignPath (una sola vez):
#
#     Install-Module -Name SignPath -Scope CurrentUser
#
# Se firman dos cosas: el ejecutable principal ANTES de empaquetar (para que la app instalada
# quede firmada) y el instalador DESPUES (que es lo que descarga la gente y lo que evalua
# SmartScreen). No se firma cada DLL: serian cientos de pedidos remotos.
# ---------------------------------------------------------------------------------------------

param(
    [string]$Version,
    [switch]$Sign,
    [string]$SignPathApiToken = $env:SIGNPATH_API_TOKEN,
    [string]$SignPathOrganizationId = "5a6c115a-3c2b-4298-b68f-89fde4594db5",
    [string]$SignPathProjectSlug = "Video_Serial_Visualizer",
    [string]$SignPathPolicySlug = "Video_Serial_Visualizer"
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

# Se valida todo lo de la firma ANTES de compilar: si falta el token o el modulo, es mejor
# enterarse ahora y no despues de dos minutos de build.
if ($Sign) {
    if (-not $SignPathApiToken) {
        throw "Falta el token de SignPath. Defini `$env:SIGNPATH_API_TOKEN o pasa -SignPathApiToken."
    }
    if (-not (Get-Module -ListAvailable -Name SignPath)) {
        throw "Falta el modulo de SignPath. Instalalo con: Install-Module -Name SignPath -Scope CurrentUser"
    }
    Import-Module SignPath
}

function Invoke-SignPathSigning {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Label
    )

    if (-not (Test-Path $Path)) {
        throw "No se encontro el archivo a firmar: $Path"
    }

    Write-Host "  Firmando $Label ..." -ForegroundColor DarkCyan

    # SignPath devuelve el archivo firmado aparte; recien despues se reemplaza el original, asi
    # un fallo a mitad de camino no deja un binario corrupto en su lugar.
    $signedPath = "$Path.signed"

    # Los timeouts generosos son por el tamano: el instalador pesa ~127 MB y hay que subirlo,
    # esperar la firma y volver a bajarlo. Con los valores por defecto puede cortarse a mitad.
    Submit-SigningRequest `
        -InputArtifactPath $Path `
        -ApiToken $SignPathApiToken `
        -OrganizationId $SignPathOrganizationId `
        -ProjectSlug $SignPathProjectSlug `
        -SigningPolicySlug $SignPathPolicySlug `
        -OutputArtifactPath $signedPath `
        -WaitForCompletion `
        -WaitForCompletionTimeoutInSeconds 1800 `
        -UploadAndDownloadRequestTimeoutInSeconds 1800

    if (-not (Test-Path $signedPath)) {
        throw "SignPath no devolvio un archivo firmado para $Label"
    }

    Move-Item -Path $signedPath -Destination $Path -Force

    $signature = Get-AuthenticodeSignature -FilePath $Path
    if ($signature.Status -ne 'Valid') {
        $cert = $signature.SignerCertificate

        # Subject == Issuer significa certificado auto-firmado: tipicamente la policy de "test
        # signing" de SignPath. Windows no lo considera de confianza, asi que no sirve de nada
        # frente a SmartScreen. Conviene decirlo explicito y no dejar un 'UnknownError' a secas.
        if ($cert -and $cert.Subject -eq $cert.Issuer) {
            throw ("El certificado usado es AUTO-FIRMADO ($($cert.Subject)), no uno de confianza " +
                   "publica. Sirve para probar el flujo, pero SmartScreen lo trata igual que un " +
                   "binario sin firmar. Revisa que -SignPathPolicySlug apunte a la policy de " +
                   "release y no a la de test.")
        }

        throw "La firma de $Label quedo en estado '$($signature.Status)': $($signature.StatusMessage)"
    }

    Write-Host "  OK: $Label firmado por $($signature.SignerCertificate.Subject)" -ForegroundColor Green
}

Write-Host "Empaquetando Video Serial Visualizer $Version" -ForegroundColor Cyan
if (-not $Sign) {
    Write-Host "(sin firmar: usa -Sign para publicar)" -ForegroundColor DarkYellow
}

# Publicado limpio: si quedaran archivos de un build anterior, terminarian dentro del instalador.
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

Write-Host "`n[1/4] dotnet publish..." -ForegroundColor Cyan
dotnet publish $projectPath -c Release
if ($LASTEXITCODE -ne 0) { throw "Fallo dotnet publish" }

Write-Host "`n[2/4] Firma del ejecutable principal..." -ForegroundColor Cyan
if ($Sign) {
    # Va antes de empaquetar: vpk calcula los hashes del contenido, asi que firmar despues
    # invalidaria el paquete.
    Invoke-SignPathSigning -Path (Join-Path $publishDir "VideoSerialVisualizer.exe") -Label "VideoSerialVisualizer.exe"
} else {
    Write-Host "  (omitido)" -ForegroundColor DarkGray
}

Write-Host "`n[3/4] vpk pack..." -ForegroundColor Cyan
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

Write-Host "`n[4/4] Firma del instalador..." -ForegroundColor Cyan
if ($Sign) {
    # Este es el que descarga la gente y el que evalua SmartScreen.
    Invoke-SignPathSigning -Path (Join-Path $releasesDir "VideoSerialVisualizer-win-Setup.exe") -Label "Setup.exe"
} else {
    Write-Host "  (omitido)" -ForegroundColor DarkGray
}

# --------------------------------------------------------------------------------------------
# Carpeta lista para publicar
#
# .\Releases NO se puede reorganizar en subcarpetas: vpk necesita encontrar ahi los .nupkg de las
# versiones anteriores para calcular los deltas. Por eso se deja plana como directorio de trabajo,
# y aparte se arma .\Publicar\v<version> con una COPIA de lo que hay que subir a GitHub.
# Asi cada release queda separada y no hay que adivinar que archivo corresponde a cual.
# --------------------------------------------------------------------------------------------
Write-Host "`nPreparando carpeta para publicar..." -ForegroundColor Cyan

$publishReleaseDir = Join-Path $PSScriptRoot "Publicar\v$Version"
if (Test-Path $publishReleaseDir) {
    Remove-Item $publishReleaseDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishReleaseDir | Out-Null

# Se copia todo el contenido de Releases: los indices referencian tambien los paquetes de
# versiones previas, asi que subirlos completos garantiza que cualquier usuario, venga de la
# version que venga, encuentre lo que necesita.
Copy-Item -Path (Join-Path $releasesDir "*") -Destination $publishReleaseDir -Force

$total = (Get-ChildItem $publishReleaseDir | Measure-Object -Property Length -Sum).Sum
Write-Host ("  {0} archivos, {1:N0} MB" -f (Get-ChildItem $publishReleaseDir).Count, ($total / 1MB)) -ForegroundColor Green

Write-Host "`nListo." -ForegroundColor Green
Write-Host "  Directorio de trabajo : $releasesDir" -ForegroundColor DarkGray
Write-Host "  PARA SUBIR A GITHUB   : $publishReleaseDir" -ForegroundColor Green
Write-Host "  Instalador            : VideoSerialVisualizer-win-Setup.exe" -ForegroundColor Green
Write-Host ""
Write-Host "Crea el Release en GitHub con tag v$Version y adjunta TODO el contenido de Publicar\v$Version" -ForegroundColor Cyan

if (-not $Sign) {
    Write-Host ""
    Write-Host "ATENCION: esta build NO esta firmada. SmartScreen va a bloquearla." -ForegroundColor Yellow
    Write-Host "Para publicarla, volve a correr con -Sign." -ForegroundColor Yellow
}
