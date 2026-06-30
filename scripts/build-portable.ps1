param(
    [string]$OutputDir = "dist-portable"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot

# Auto-detect version from tauri.conf.json
$tauriConf = Get-Content "src-tauri\tauri.conf.json" -Raw | ConvertFrom-Json
$Version = $tauriConf.version
$AppName = $tauriConf.productName

Write-Host "=== $AppName v$Version Portable Build ===" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Gray

# Step 1: Ensure the release was built
$releaseDir = "src-tauri\target\release"
$exeName = "rdm-manager.exe"

Write-Host "[1/3] Building frontend assets..." -ForegroundColor Yellow
npm run build
if ($LASTEXITCODE -ne 0) { throw "npm run build failed" }

Write-Host "Running cargo build --release..." -ForegroundColor Yellow
Set-Location src-tauri
cargo build --release
if ($LASTEXITCODE -ne 0) { throw "cargo build --release failed" }
Set-Location $projectRoot

# Step 2: Prepare portable directory
Write-Host "[2/3] Creating portable package..." -ForegroundColor Yellow

$portableDir = "$OutputDir\$AppName Portable"
if (Test-Path $portableDir) {
    try { Remove-Item -Recurse -Force $portableDir -ErrorAction Stop }
    catch {
        $ts = Get-Date -Format "HHmmss"
        $portableDir = "$OutputDir\$AppName Portable $ts"
        Write-Host "  Original dir locked, using: $portableDir" -ForegroundColor Yellow
    }
}
New-Item -ItemType Directory -Path $portableDir -Force | Out-Null

# Copy the release exe
Copy-Item "$releaseDir\$exeName" "$portableDir\" -Force

# Copy WebView2 loader if it exists as a separate file
if (Test-Path "$releaseDir\WebView2Loader.dll") {
    Copy-Item "$releaseDir\WebView2Loader.dll" "$portableDir\" -Force
}

# Copy icon as standalone .ico for the portable folder
if (Test-Path "src-tauri\icons\icon.ico") {
    Copy-Item "src-tauri\icons\icon.ico" "$portableDir\" -Force
}

Write-Host "  Portable dir: $portableDir" -ForegroundColor Gray
Write-Host "  Executable:   $portableDir\$exeName" -ForegroundColor Gray

# Step 3: Create ZIP
Write-Host "[3/3] Creating portable ZIP archive..." -ForegroundColor Yellow

$zipName = "$AppName v${Version} Portable.zip"
$zipPath = "$OutputDir\$zipName"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

Compress-Archive -Path "$portableDir\*" -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Green
Write-Host "  Installer: src-tauri\target\release\bundle\" -ForegroundColor White
Write-Host "  Portable:  $zipPath" -ForegroundColor White
Write-Host "  Folder:    $portableDir\" -ForegroundColor White
