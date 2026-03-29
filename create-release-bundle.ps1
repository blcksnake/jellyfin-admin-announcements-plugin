# create-release-bundle.ps1
# Builds publish output and creates a clean zip for GitHub/Jellyfin repository distribution.

param(
    [string]$Version = "0.2.0.0",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$publishDir = Join-Path $PSScriptRoot "publish"
$releaseRoot = Join-Path $PSScriptRoot "release"
$bundleDir = Join-Path $releaseRoot ("Announcements_{0}" -f $Version)
$zipPath = Join-Path $releaseRoot ("Announcements_{0}.zip" -f $Version)

if (-not $SkipBuild) {
    dotnet publish (Join-Path $PSScriptRoot "Jellyfin.Plugin.Announcements.csproj") -c Release -o $publishDir
}

if (-not (Test-Path $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

if (Test-Path $bundleDir) {
    cmd /c rmdir /s /q "$bundleDir"
}

New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null

$requiredFiles = @(
    "Jellyfin.Plugin.Announcements.dll",
    "Jellyfin.Plugin.Announcements.deps.json",
    "Newtonsoft.Json.dll"
)

foreach ($file in $requiredFiles) {
    $src = Join-Path $publishDir $file
    if (-not (Test-Path $src)) {
        throw "Missing required publish file: $src"
    }
    Copy-Item $src $bundleDir -Force
}

Copy-Item (Join-Path $PSScriptRoot "meta.json") (Join-Path $bundleDir "meta.json") -Force
Copy-Item (Join-Path $PSScriptRoot "manifest.json") (Join-Path $bundleDir "manifest.json") -Force

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $bundleDir "*") -DestinationPath $zipPath -Force

Write-Host "[OK] Release folder: $bundleDir" -ForegroundColor Green
Write-Host "[OK] Release zip: $zipPath" -ForegroundColor Green
