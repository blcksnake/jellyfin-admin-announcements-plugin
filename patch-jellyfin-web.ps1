# patch-jellyfin-web.ps1
# Run this once on the machine where Jellyfin is installed (as Administrator if needed).
# It adds a single <script> tag to jellyfin-web/index.html so announcement
# banners appear automatically on every Jellyfin page. JS Injector is not required.

param(
    [switch]$Undo, # Pass -Undo to remove the patch
    [string]$IndexPath = "C:\Program Files\Jellyfin\Server\jellyfin-web\index.html"
)
$scriptTag = '<script src="/Plugins/Announcements/banner.js" defer></script>'

if (-not (Test-Path $indexPath)) {
    Write-Error "index.html not found: $indexPath"
    Write-Host "If this is a VM/sandbox install, pass -IndexPath with the real jellyfin-web/index.html path." -ForegroundColor Yellow
    exit 1
}

$content = [System.IO.File]::ReadAllText($indexPath, [System.Text.Encoding]::UTF8)

if ($Undo) {
    if ($content -notmatch [regex]::Escape($scriptTag)) {
        Write-Host "Not patched - nothing to undo." -ForegroundColor Green
        exit 0
    }
    $content = $content -replace ([regex]::Escape($scriptTag) + "`r?`n?"), ''
    [System.IO.File]::WriteAllText($indexPath, $content, [System.Text.Encoding]::UTF8)
    Write-Host "[OK] Patch removed from index.html." -ForegroundColor Green
    exit 0
}

if ($content -match 'Plugins/Announcements/banner\.js') {
    Write-Host "[OK] index.html is already patched - banners will auto-load." -ForegroundColor Green
    exit 0
}

# Back up before editing
Copy-Item $indexPath "$indexPath.bak" -Force
Write-Host "[OK] Backup saved: $indexPath.bak" -ForegroundColor DarkGray

# Insert just before </body>
$content = $content -replace '(</body>)', "$scriptTag`n`$1"
[System.IO.File]::WriteAllText($indexPath, $content, [System.Text.Encoding]::UTF8)
Write-Host "[OK] Patched index.html - announcement banners will now show on every page." -ForegroundColor Green
Write-Host "Restart Jellyfin and hard-refresh your browser (Ctrl+Shift+R) to apply." -ForegroundColor Cyan
