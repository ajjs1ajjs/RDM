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
