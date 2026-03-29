# deploy-to-jellyfin.ps1
# Run this script on the machine where Jellyfin is installed.
# It deploys publish artifacts into Jellyfin's plugin directory.
# It does NOT create release zip files. Use create-release-bundle.ps1 for packaging.

param(
    [switch]$NoRestart,
    [switch]$IncludeSymbols
)

$ErrorActionPreference = "Stop"

$pluginAssembly = "Jellyfin.Plugin.Announcements.dll"
$depsFile = "Jellyfin.Plugin.Announcements.deps.json"
$symbolsFile = "Jellyfin.Plugin.Announcements.pdb"
$extraRuntimeFiles = @(
    "Newtonsoft.Json.dll"
)

$publishDir = Join-Path $PSScriptRoot "publish"
$metaPath = Join-Path $PSScriptRoot "meta.json"
$manifestPath = Join-Path $PSScriptRoot "manifest.json"

Write-Host "=== Jellyfin Announcements Deployment ===" -ForegroundColor Cyan

if (-not (Test-Path $publishDir)) {
    throw "Publish directory not found: $publishDir. Run: dotnet publish -c Release -o publish"
}

if (-not (Test-Path $metaPath)) {
    if (Test-Path $manifestPath) {
        Write-Host "[!!] meta.json not found; using manifest.json values." -ForegroundColor Yellow
        $meta = Get-Content $manifestPath -Raw | ConvertFrom-Json
        $pluginName = $meta.Name
        $pluginVersion = $meta.Version
    } else {
        throw "Neither meta.json nor manifest.json found in project root."
    }
} else {
    $meta = Get-Content $metaPath -Raw | ConvertFrom-Json
    $pluginName = $meta.name
    $pluginVersion = $meta.version
}

if ([string]::IsNullOrWhiteSpace($pluginName) -or [string]::IsNullOrWhiteSpace($pluginVersion)) {
    throw "Could not parse plugin name/version from metadata."
}

$dataRoot = Join-Path $env:LOCALAPPDATA "jellyfin"
$pluginsRoot = Join-Path $dataRoot "plugins"
$pluginFolder = Join-Path $pluginsRoot ("{0}_{1}" -f $pluginName, $pluginVersion)

Write-Host ("Plugin: {0} {1}" -f $pluginName, $pluginVersion) -ForegroundColor White
Write-Host ("Data root: {0}" -f $dataRoot) -ForegroundColor DarkGray
Write-Host ("Plugin folder: {0}" -f $pluginFolder) -ForegroundColor DarkGray

if (-not (Test-Path $pluginsRoot)) {
    New-Item -ItemType Directory -Path $pluginsRoot -Force | Out-Null
}
if (-not (Test-Path $pluginFolder)) {
    New-Item -ItemType Directory -Path $pluginFolder -Force | Out-Null
    Write-Host "[OK] Created plugin folder" -ForegroundColor Green
} else {
    Write-Host "[OK] Plugin folder exists" -ForegroundColor Green
}

# Stop service first so Windows does not lock plugin DLLs.
$svc = Get-Service -Name "JellyfinServer" -ErrorAction SilentlyContinue
$serviceWasRunning = $false
if ($svc -and $svc.Status -eq "Running") {
    Write-Host "Stopping Jellyfin service..." -ForegroundColor Yellow
    Stop-Service JellyfinServer -Force
    $serviceWasRunning = $true
    Start-Sleep -Seconds 2
    Write-Host "[OK] Jellyfin stopped" -ForegroundColor Green
}

# Remove previous plugin binaries to avoid stale file drift between builds.
Get-ChildItem $pluginFolder -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in ".dll", ".deps.json", ".pdb", ".xml" } |
    ForEach-Object { Remove-Item $_.FullName -Force }

$filesToDeploy = @($pluginAssembly, $depsFile)
if ($IncludeSymbols) { $filesToDeploy += $symbolsFile }
$filesToDeploy += $extraRuntimeFiles

foreach ($file in $filesToDeploy) {
    $src = Join-Path $publishDir $file
    if (Test-Path $src) {
        Copy-Item $src $pluginFolder -Force
        Write-Host ("[OK] Copied {0}" -f $file) -ForegroundColor Green
    } else {
        Write-Host ("[!!] Missing (skipped): {0}" -f $file) -ForegroundColor Yellow
    }
}

if (Test-Path $metaPath) {
    Copy-Item $metaPath (Join-Path $pluginFolder "meta.json") -Force
    Write-Host "[OK] Copied meta.json" -ForegroundColor Green
} elseif (Test-Path $manifestPath) {
    Copy-Item $manifestPath (Join-Path $pluginFolder "meta.json") -Force
    Write-Host "[OK] Copied manifest.json as meta.json" -ForegroundColor Green
}

# == Patch Jellyfin's index.html so the banner script loads on every page ====
$jellyfinWebRoot = "C:\Program Files\Jellyfin\Server\jellyfin-web"
$indexPath = Join-Path $jellyfinWebRoot "index.html"
$bannerScriptTag = '<script src="/Plugins/Announcements/banner.js" defer></script>'

if (Test-Path $indexPath) {
    try {
        $indexContent = [System.IO.File]::ReadAllText($indexPath)
        if ($indexContent -notmatch 'Plugins/Announcements/banner\.js') {
            # Insert just before closing </body>
            $indexContent = $indexContent -replace '(</body>\s*</html>)', "$bannerScriptTag`n`$1"
            [System.IO.File]::WriteAllText($indexPath, $indexContent, [System.Text.Encoding]::UTF8)
            Write-Host "[OK] Patched jellyfin-web/index.html - banner script will auto-load" -ForegroundColor Green
        } else {
            Write-Host "[OK] jellyfin-web/index.html already patched" -ForegroundColor Green
        }
    } catch {
        Write-Host ("[!!] Could not patch index.html (run as Administrator): {0}" -f $_.Exception.Message) -ForegroundColor Yellow
    }
} else {
    Write-Host ("[!!] index.html not found at {0} - banners won't auto-load" -f $indexPath) -ForegroundColor Yellow
}
# =============================================================================

if (-not $NoRestart) {
    if ($svc) {
        Write-Host "Starting Jellyfin service..." -ForegroundColor Yellow
        Start-Service JellyfinServer
        Write-Host "[OK] Jellyfin started" -ForegroundColor Green
    } else {
        Write-Host "[!!] JellyfinServer service not found. Start Jellyfin manually." -ForegroundColor Yellow
    }
} elseif ($serviceWasRunning -and $svc) {
    Write-Host "NoRestart set: Jellyfin remains stopped. Start it manually when ready." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Deployment Complete ===" -ForegroundColor Cyan
Write-Host "Plugin page: Dashboard > Plugins > Announcements" -ForegroundColor White
Write-Host "Banner script is injected into jellyfin-web/index.html and loads automatically on every page." -ForegroundColor White
