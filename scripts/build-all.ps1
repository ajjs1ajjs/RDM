$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot

$tauriConf = Get-Content "src-tauri\tauri.conf.json" -Raw | ConvertFrom-Json
$Version = $tauriConf.version
$AppName = $tauriConf.productName

Write-Host ""
Write-Host "=== $AppName v$Version - Full Build ===" -ForegroundColor Cyan
Write-Host ""

Write-Host "[1/2] Building installer via Tauri..." -ForegroundColor Yellow
npm run tauri build
if ($LASTEXITCODE -ne 0) { throw "Tauri installer build failed" }
Write-Host ""

Write-Host "[2/2] Building portable package..." -ForegroundColor Yellow
& "$PSScriptRoot\build-portable.ps1"
if ($LASTEXITCODE -ne 0) { throw "Portable build failed" }

Write-Host ""
Write-Host "=== All builds completed successfully ===" -ForegroundColor Green

# Step 3: Copy final builds to E:\Code\RDM\BUILD
Write-Host ""
Write-Host "=== Copying final builds to BUILD === " -ForegroundColor Yellow

$buildDest = "$projectRoot\BUILD"
if (!(Test-Path $buildDest)) {
    New-Item -ItemType Directory -Path $buildDest -Force | Out-Null
}

# Copy installers
$msiPath = Get-ChildItem "src-tauri\target\release\bundle\msi\*.msi" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($msiPath) {
    Copy-Item $msiPath.FullName "$buildDest\" -Force
    Write-Host "  Copied MSI installer to BUILD\" -ForegroundColor Gray
}

# Copy portable ZIP
$zipPath = Get-ChildItem "dist-portable\*.zip" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($zipPath) {
    Copy-Item $zipPath.FullName "$buildDest\" -Force
    Write-Host "  Copied portable ZIP to BUILD\" -ForegroundColor Gray
}

# Copy portable folder
$portableSource = "dist-portable\$AppName Portable"
if (Test-Path $portableSource) {
    $portableDest = "$buildDest\$AppName Portable"
    if (Test-Path $portableDest) {
        Remove-Item -Recurse -Force $portableDest -ErrorAction SilentlyContinue
    }
    Copy-Item -Recurse -Force $portableSource $portableDest
    Write-Host "  Copied portable folder to BUILD\" -ForegroundColor Gray
}

Write-Host "=== Copy Complete ===" -ForegroundColor Green
