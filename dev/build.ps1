param(
    [string]$Version = "v1.0.0"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "dev/src/Bannerlord.AutoAmmoPickup/Bannerlord.AutoAmmoPickup.csproj"
$staging = Join-Path $root "out/Bannerlord.AutoAmmoPickup"
$binDir = Join-Path $staging "bin/Win64_Shipping_Client"
$projectBin = Join-Path $root "dev/src/Bannerlord.AutoAmmoPickup/bin/Release"
$moduleDataSource = Join-Path $root "dev/module/ModuleData"
$moduleDataDest = Join-Path $staging "ModuleData"

Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $binDir -Force | Out-Null

dotnet restore $project
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$builtDll = Join-Path $projectBin "Bannerlord.AutoAmmoPickup.dll"
if (!(Test-Path $builtDll)) {
    throw "Build output not found: $builtDll"
}

$pdbSrc = Join-Path $projectBin "Bannerlord.AutoAmmoPickup.pdb"

$templatePath = Join-Path $root "dev/module/SubModule.xml"
$subModuleOut = Join-Path $staging "SubModule.xml"
(Get-Content $templatePath -Raw).Replace("__VERSION__", $Version) | Set-Content $subModuleOut

if (Test-Path $moduleDataSource) {
    Copy-Item $moduleDataSource $moduleDataDest -Recurse -Force
}

Copy-Item $builtDll $binDir -Force
if (Test-Path $pdbSrc) {
    Copy-Item $pdbSrc $binDir -Force
}

$zipPath = Join-Path $root "out/Bannerlord.AutoAmmoPickup-$Version.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path "$staging/*" -DestinationPath $zipPath -Force

Write-Host "Build complete."
Write-Host "Mod folder: $staging"
Write-Host "Zip package: $zipPath"
