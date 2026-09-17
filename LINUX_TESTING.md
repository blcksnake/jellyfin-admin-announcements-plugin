# Linux Jellyfin Test Environment

This starts a local Jellyfin 12.1 Linux container. Install the packaged plugin through
Jellyfin's normal repository/catalog flow; the Compose file intentionally does not mount
files into `/config/plugins`, because Jellyfin owns that directory and reconciles package state.

## 1) Start test server

```powershell
docker compose -f docker-compose.linux-test.yml up -d
```

Open Jellyfin:

- http://localhost:8096

## 2) Install the package through Jellyfin

Add the Announcements repository URL in Dashboard > Plugins > Repositories, install the
current Announcements version from the Catalog, then restart Jellyfin. This is the same
installation route used by end users and preserves Jellyfin's package metadata.

## 3) Rebuild package before each test

When you change plugin code, rebuild and regenerate the release bundle:

```powershell
dotnet publish Jellyfin.Plugin.Announcements.csproj -c Release -o publish
.\create-release-bundle.ps1
```

Publish the resulting ZIP to the repository, install/update it through the Catalog, then restart
the container so Jellyfin reloads the plugin assembly:

```powershell
docker compose -f docker-compose.linux-test.yml restart jellyfin
```

## 4) Confirm plugin injection state

Check container logs:

```powershell
docker logs jellyfin-linux-test --tail 300 | Select-String -Pattern "Announcements|index.html|banner.js"
```

Expected success signal:

- "[Announcements] Patched .../jellyfin-web/index.html - banners will auto-load on every page."

If you still see:

- "jellyfin-web/index.html not found"

then banner script is not being injected.

## 5) Verify banner endpoint manually

In browser, open:

- http://localhost:8096/Plugins/Announcements/banner.js
- http://localhost:8096/Plugins/Announcements/banner.css

Both should return content (not 404).

## 6) Run automated integration tests

With the test server running and the default `admin` / `admin` test account available:

```powershell
.\tests\Test-JellyfinIntegration.ps1
npm install
npm run test:browser
```

The API suite verifies Jellyfin 12 authentication, authorization, announcement CRUD, targeting data,
analytics, index-patch diagnostics, and deployed assets. The browser suite signs in through Jellyfin Web,
checks the rendered banner layout, and confirms the admin page loads announcements without HTTP 401.

## 7) Stop and clean up

Stop container:

```powershell
docker compose -f docker-compose.linux-test.yml down
```

Remove test data too (optional):

```powershell
Remove-Item -Recurse -Force .\docker\jellyfin-test
```
