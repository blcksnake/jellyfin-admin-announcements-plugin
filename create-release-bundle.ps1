# create-release-bundle.ps1
# Builds publish output and creates a clean zip for GitHub/Jellyfin repository distribution.

param(
    [string]$Version = "0.2.0.8",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Version must contain four numeric components."
}

$publishDir = Join-Path $PSScriptRoot "publish"
$releaseRoot = Join-Path $PSScriptRoot "release"
$bundleDir = Join-Path $releaseRoot ("Announcements_{0}" -f $Version)
$zipPath = Join-Path $releaseRoot ("Announcements_{0}.zip" -f $Version)

if (-not $SkipBuild) {
    dotnet publish (Join-Path $PSScriptRoot "Jellyfin.Plugin.Announcements.csproj") -c Release -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Plugin publish failed. Refusing to package stale output." }
}

if (-not (Test-Path $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

if (Test-Path $bundleDir) {
    $resolvedBundle = (Resolve-Path -LiteralPath $bundleDir).Path
    $resolvedRoot = (Resolve-Path -LiteralPath $releaseRoot).Path
    if (-not $resolvedBundle.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Bundle directory must be inside the release directory."
    }
    Remove-Item -LiteralPath $resolvedBundle -Recurse -Force
}

New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null

$requiredFiles = @(
    "Jellyfin.Plugin.Announcements.dll",
    "Jellyfin.Plugin.Announcements.deps.json"
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
