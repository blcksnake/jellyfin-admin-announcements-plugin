# Jellyfin Announcements Plugin

Server-wide announcements and maintenance banners for Jellyfin.

## Status

- Version: 0.2.0.0
- Target Jellyfin ABI: 10.11.0.0
- Framework: .NET 9

## Features

- Admin CRUD for announcements
- Severity-based messaging: Info, Warning, Critical
- Optional start/end scheduling
- Login-page visibility toggle per announcement
- Optional dismiss controls
- Dismiss behavior: session or permanent
- Auto-load banner script support through index patching

## Install (Manual)

1. Build publish output:

   dotnet publish -c Release -o publish

2. Copy these files into your Jellyfin plugin folder:

- Jellyfin.Plugin.Announcements.dll
- Jellyfin.Plugin.Announcements.deps.json
- Newtonsoft.Json.dll
- meta.json

Folder format should be:

- Announcements_<version>

Example:

- C:/Users/<user>/AppData/Local/Jellyfin/plugins/Announcements_0.2.0.0

3. Restart Jellyfin.
4. Hard-refresh browser (Ctrl+Shift+R).

## Install (Script)

Run the deployment helper on the Jellyfin host:

powershell -ExecutionPolicy Bypass -File ./deploy-to-jellyfin.ps1

## Build

- dotnet build -c Release
- dotnet publish -c Release -o publish

## Release Bundle

A clean distribution bundle is generated under:

- release/Announcements_0.2.0.0/
- release/Announcements_0.2.0.0.zip

## Repository Install (No Compile Needed)

You can host this plugin in a Jellyfin repository so users install directly from Jellyfin.

1. Upload `release/Announcements_0.2.0.0.zip` to a public URL (for example, a GitHub Release asset).
2. Generate `repository/manifest.json`:

   powershell -ExecutionPolicy Bypass -File ./build-repository-manifest.ps1 -SourceUrl "https://github.com/blcksnake/jellyfin-admin-announcements-plugin/releases/download/v0.2.0.0/Announcements_0.2.0.0.zip"

3. Host `repository/manifest.json` at a stable raw URL.
   Example: `https://raw.githubusercontent.com/blcksnake/jellyfin-admin-announcements-plugin/main/repository/manifest.json`
4. In Jellyfin: `Dashboard > Plugins > Repositories > +` and paste that manifest URL.

This allows end users to install/update without building from source.

## Compatibility Notes

- Plugin is tested with Jellyfin 10.11.6.
- If announcements do not appear, verify index patching and browser hard refresh.

## Contributing

See CONTRIBUTING.md.

## Security

See SECURITY.md.

## License

MIT. See LICENSE.

## Disclaimer

This project is community-maintained and not an official Jellyfin project.
