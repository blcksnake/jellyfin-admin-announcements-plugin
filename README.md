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

## Easy Install

Install directly from Jellyfin using a custom repository URL.

Repository URL to add in Jellyfin:

- https://raw.githubusercontent.com/blcksnake/jellyfin-admin-announcements-plugin/main/repository/manifest.json

Steps:

1. Open Jellyfin Dashboard.
2. Go to Plugins.
3. Open Repositories.
4. Add new repository URL using the link above.
5. Go to Catalog and install Announcements.
6. Restart Jellyfin.
7. Hard refresh browser with Ctrl+Shift+R.

No manual file copy and no local build is required for end users.

## Maintainers

For release packaging and repository publishing workflow, see DISTRIBUTION.md.

## Compatibility Notes

- Plugin is tested with Jellyfin 10.11.6.
- If announcements do not appear, restart Jellyfin and hard refresh browser.

## Contributing

See CONTRIBUTING.md.

## Security

See SECURITY.md.

## License

MIT. See LICENSE.

## Disclaimer

This project is community-maintained and not an official Jellyfin project.
