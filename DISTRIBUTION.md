# Distribution Guide

Use this checklist when publishing a new version to the community.

## 1) Bump Versions

Update version in:

- Jellyfin.Plugin.Announcements.csproj
- meta.json
- manifest.json
- CHANGELOG.md

## 2) Build and Publish

- dotnet build -c Release
- dotnet publish -c Release -o publish

## 3) Create Release Bundle

Use the prepared release output:

- release/Announcements_<version>/
- release/Announcements_<version>.zip

Required plugin files:

- Jellyfin.Plugin.Announcements.dll
- Jellyfin.Plugin.Announcements.deps.json
- Newtonsoft.Json.dll
- meta.json

## 4) GitHub Release

- Push tags and source
- Create a GitHub Release with the version tag (example: v0.2.0.5)
- Attach release/Announcements_<version>.zip
- Include changelog highlights

## 5) Jellyfin Community Distribution

For community plugin feeds/catalogs, provide:

- Repository URL
- Source license (MIT)
- Plugin GUID from meta.json
- Version
- TargetAbi
- Direct release asset URL for zip or package

To generate a repository manifest compatible with Jellyfin Repositories:

- powershell -ExecutionPolicy Bypass -File ./build-repository-manifest.ps1 -SourceUrl "https://github.com/blcksnake/jellyfin-admin-announcements-plugin/releases/download/v<version>/Announcements_<version>.zip" -Version "<version>" -ZipPath "./release/Announcements_<version>.zip" -OutputPath "./repository/manifest.json"

Then host `repository/manifest.json` at a stable public URL and share that URL.

Do not publish an updated `repository/manifest.json` until the final zip asset exists and the checksum has been regenerated for that exact artifact.

Recommended repository URL to add in Jellyfin:

- https://raw.githubusercontent.com/blcksnake/jellyfin-admin-announcements-plugin/main/repository/manifest.json

Common repository error fix:

- `An error occurred while getting the plugin details from the repository.`
- Cause: manifest format is incorrect (must be an array with `versions[]`) or JSON is invalid.
- Fix: regenerate with `build-repository-manifest.ps1` and verify the URL serves raw JSON.

## 6) Post-Release Validation

- Install in a clean Jellyfin instance
- Verify admin page loads
- Verify create/edit/delete and banner rendering
- Verify login-page toggle and dismiss behavior
