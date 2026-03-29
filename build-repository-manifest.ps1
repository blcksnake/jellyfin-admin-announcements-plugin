param(
    [Parameter(Mandatory = $true)]
    [string]$SourceUrl,

    [string]$Version = "0.2.0.2",
    [string]$ZipPath = ".\release\Announcements_0.2.0.2.zip",
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

$pluginEntry = @{
    guid = $meta.guid
    name = $meta.name
    overview = $meta.overview
    description = $meta.description
    owner = "BLCKSNAKE"
    category = $meta.category
    imageUrl = $meta.imageUrl
    versions = @(
        @{
            version = $Version
            changelog = $meta.changelog
            targetAbi = $meta.targetAbi
            sourceUrl = $SourceUrl
            checksum = $checksum
            timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        }
    )
}

# Jellyfin repository manifests must be a top-level JSON array.
$json = "[" + ($pluginEntry | ConvertTo-Json -Depth 6) + "]"
Set-Content -Path $OutputPath -Encoding UTF8 -Value $json
Write-Host "[OK] Wrote Jellyfin repository manifest: $OutputPath" -ForegroundColor Green
Write-Host "[OK] MD5: $checksum" -ForegroundColor Green
