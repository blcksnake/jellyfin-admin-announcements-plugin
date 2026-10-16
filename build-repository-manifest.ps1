param(
    [Parameter(Mandatory = $true)]
    [string]$SourceUrl,

    [string]$Version = "0.2.0.8",
    [string]$ZipPath = ".\release\Announcements_0.2.0.8.zip",
    [string]$OutputPath = ".\repository\manifest.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ZipPath)) {
    throw "Release zip not found at $ZipPath"
}

$metaPath = Join-Path $PSScriptRoot "meta.json"
if (-not (Test-Path $metaPath)) {
    throw "meta.json not found at $metaPath"
}

$meta = Get-Content $metaPath -Raw | ConvertFrom-Json
$checksum = (Get-FileHash -Path $ZipPath -Algorithm MD5).Hash.ToLowerInvariant()

$outputDir = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDir) -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$newVersion = [ordered]@{
    version = $Version
    changelog = $meta.changelog
    targetAbi = $meta.targetAbi
    sourceUrl = $SourceUrl
    checksum = $checksum
    timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
}

# Keep older ABI builds available. Jellyfin uses targetAbi to select the newest
# compatible release, so replacing the whole version list would strand 10.11 users.
$existingVersions = @()
if (Test-Path $OutputPath) {
    $existingManifest = Get-Content $OutputPath -Raw | ConvertFrom-Json
    $existingPlugin = @($existingManifest) |
        Where-Object { $_.guid -eq $meta.guid } |
        Select-Object -First 1
    if ($null -ne $existingPlugin) {
        $existingVersions = @($existingPlugin.versions) |
            Where-Object { $_.version -ne $Version }
    }
}

$pluginEntry = [ordered]@{
    guid = $meta.guid
    name = $meta.name
    overview = $meta.overview
    description = $meta.description
    owner = "BLCKSNAKE"
    category = $meta.category
    imageUrl = $meta.imageUrl
    versions = @($newVersion) + @($existingVersions)
}

# Jellyfin repository manifests must be a top-level JSON array.
$json = "[" + ($pluginEntry | ConvertTo-Json -Depth 6) + "]"
Set-Content -Path $OutputPath -Encoding UTF8 -Value $json
Write-Host "[OK] Wrote Jellyfin repository manifest: $OutputPath" -ForegroundColor Green
Write-Host "[OK] MD5: $checksum" -ForegroundColor Green
