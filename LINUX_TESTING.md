# Linux Jellyfin Test Environment

This starts a local Linux Jellyfin container and mounts the packaged plugin release folder.

## 1) Start test server

```powershell
docker compose -f docker-compose.linux-test.yml up -d
```

Open Jellyfin:

- http://localhost:8096

## 2) Rebuild package before each test

When you change plugin code, rebuild and regenerate the release bundle:

```powershell
dotnet publish Jellyfin.Plugin.Announcements.csproj -c Release -o publish
.\create-release-bundle.ps1
```

Then restart container so Jellyfin reloads plugin assemblies:

```powershell
docker compose -f docker-compose.linux-test.yml restart jellyfin
```

## 3) Confirm plugin injection state

Check container logs:

```powershell
docker logs jellyfin-linux-test --tail 300 | Select-String -Pattern "Announcements|index.html|JS Injector|banner.js"
```

Expected success signal:

- "[Announcements] Patched ... index.html"

If you still see:

- "jellyfin-web/index.html not found"
- "JS Injector plugin not found; banner auto-injection disabled"

then banner script is not being injected.

## 4) Verify banner endpoint manually

In browser, open:

- http://localhost:8096/Plugins/Announcements/banner.js
- http://localhost:8096/Plugins/Announcements/banner.css

Both should return content (not 404).

## 5) Stop and clean up

Stop container:

```powershell
docker compose -f docker-compose.linux-test.yml down
```

Remove test data too (optional):

```powershell
Remove-Item -Recurse -Force .\docker\jellyfin-test
```
