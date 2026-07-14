param(
    [string]$ReleaseNotes
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$tauriConfPath = "$root/src-tauri/tauri.conf.json"
$tauriConfRaw = Get-Content $tauriConfPath -Raw | ConvertFrom-Json
$version = $tauriConfRaw.version
$appName = $tauriConfRaw.productName

if ([string]::IsNullOrWhiteSpace($version)) { throw "Failed to read version from tauri.conf.json" }

$tag = "v$version"
Write-Host "Release version: $tag" -ForegroundColor Cyan

# Check tag doesn't already exist
git fetch origin --tags --quiet
$localTag = git tag -l $tag
$remoteTag = git ls-remote --tags origin "refs/tags/$tag"
if ($localTag -or $remoteTag) {
    throw "Tag $tag already exists (local or origin). Bump version in tauri.conf.json first."
}

# Build everything: NSIS, MSI, portable
Write-Host "Building all targets..." -ForegroundColor Cyan
& "$PSScriptRoot/build-all.ps1"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# Check for uncommitted changes
$dirty = git status --porcelain
if ($dirty) {
    Write-Host $dirty
    throw "Uncommitted changes found (see above). Commit them manually before release."
}

git push origin master
git tag $tag
git push origin $tag

# Prepare release notes
if (-not $ReleaseNotes) {
    $changelog = Get-Content "$root/CHANGELOG.md" -Raw -ErrorAction SilentlyContinue
    if ($changelog -match "(?s)^# .*?\n\n(## .*?)\n\n## ") {
        $ReleaseNotes = $Matches[1]
    } else {
        $ReleaseNotes = "Release $tag."
    }
}

# Find build artifacts
$msiPath = Get-ChildItem "$root/src-tauri/target/release/bundle/msi/*.msi" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$nsisPath = Get-ChildItem "$root/src-tauri/target/release/bundle/nsis/*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$zipPath = Get-ChildItem "$root/dist-portable/*.zip" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

$assets = @()
if ($msiPath) { $assets += "`"$($msiPath.FullName)`"" }
if ($nsisPath) { $assets += "`"$($nsisPath.FullName)`"" }
if ($zipPath) { $assets += "`"$($zipPath.FullName)`"" }

$notesFile = [System.IO.Path]::GetTempFileName()
try {
    [System.IO.File]::WriteAllText($notesFile, $ReleaseNotes, [System.Text.UTF8Encoding]::new($false))
    $ghArgs = @(
        "release", "create", $tag
    )
    $ghArgs += $assets
    $ghArgs += "--title", "`"$tag`""
    $ghArgs += "--notes-file", "`"$notesFile`""
    & "gh" $ghArgs
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
} finally {
    Remove-Item $notesFile -Force -ErrorAction SilentlyContinue
}

Write-Host "Done: release $tag published." -ForegroundColor Green
