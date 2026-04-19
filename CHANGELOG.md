# Changelog

 All notable changes to this project will be documented in this file.

## 0.2.0.6 - 2026-04-18

- Added audience, device, role, and specific-user targeting for announcements.
- Added explicit exclude-device controls and live Jellyfin user picker controls in the admin targeting editor.
- Fixed user-targeted delivery so explicitly included users resolve correctly even when Jellyfin returns mixed GUID formats.
- Fixed cross-user leakage when switching accounts in the same browser session by clearing rendered banners on viewer/token changes.
- Fixed delayed post-login banner loading by invalidating stale viewer caches and re-polling immediately on auth/session changes.
- Expanded targeting summary output in the admin list to include excluded device segments and user targeting counts.
- Refreshed the admin page and banner visuals into a more consistent Jellyfin-friendly design system.
- Fixed config page rendering by injecting the scoped admin stylesheet at runtime so Jellyfin reliably applies the custom layout.
- Fixed Settings tab visibility so plugin settings and diagnostics sections display correctly when switched.
- Fixed small utility button styling so actions like Refresh users and Refresh no longer fall back to mismatched white host styling.
- **Security**: Added per-IP rate limiting on all anonymous analytics endpoints to prevent data poisoning and abuse.
- **Security**: Added hard cap (10,000) on active-impression tracking per announcement to prevent memory exhaustion.
- **Security**: Added maximum length and count validation on announcement fields (Title ≤ 200, Message ≤ 5000, Tags ≤ 50, UserIds ≤ 500, LibraryIds ≤ 100).
- **Security**: Added `IsIndexPathSafe()` guard to prevent admin-configured paths from targeting sensitive system files during banner injection.
- **Security**: Fixed `</body>` injection to use `LastIndexOf` instead of `Replace` to prevent duplicate script tags on malformed HTML.
- **Security**: Replaced `Math.random()` session ID generation with `crypto.getRandomValues()` for cryptographically random session tokens.
- **Security**: Replaced JS Injector assembly substring lookup with exact `string.Equals(..., Ordinal)` comparison to prevent name-spoofing by similarly-named assemblies.
- **Security**: Added error logging to `AnnouncementStore` data load/save operations so disk/permission failures are visible in server logs instead of silently swallowed.
- **Security**: Removed pinned `Microsoft.AspNetCore.Mvc.Abstractions 2.2.0` (EOL, CVE-2019-0657, CVE-2019-0980, CVE-2019-0981) — version now supplied transitively by Jellyfin.Controller.

## 0.2.0.5 - 2026-04-06

- Added announcement analytics for views, dismisses, and live active impressions.
- Added admin quick actions to duplicate, archive/unarchive, and enable/disable announcements.
- Added tag support for announcements, including cleaned-up tag entry and list filtering.
- Added an operational diagnostics panel showing injection mode, resolved path, startup time, and plugin version.
- Added branded logo metadata for Jellyfin catalog display and a repository banner for GitHub.
- Refined the admin list to surface lifecycle state, analytics counters, and cleaner announcement management.

## 0.2.0.4 - 2026-03-30

- **MAIN TESTING RELEASE**: v0.2.0.4 is the primary public testing build for the plugin.
- Verified repository-based install and update flow for Jellyfin plugin delivery.
- Confirmed production Linux and Docker deployment support when `jellyfin-web/index.html` is writable by the runtime user.
- Improved plugin settings UI visibility and readability in the admin panel.
- Added custom path configuration and verbose path logging for non-standard Jellyfin installs.
- Validated announcement CRUD, login-page visibility, dismiss behavior, and startup injection flow.

## 0.2.0.3 - 2026-03-30

- **FEATURE**: Added robust path resolution for non-standard Jellyfin installations via plugin configuration UI.
- Added `CustomWebPath` and `CustomIndexPath` configuration options in plugin settings.
- Added `EnablePathLogging` debug logging option to troubleshoot path resolution issues.
- Enhanced path resolution priority order: config settings > env vars > relative paths > hardcoded locations.
- Added `/opt/jellyfin` path candidate for Linux installations.
- Improved path resolution logging with verbose debug output when enabled.
- **API**: Added GET/POST `/Plugins/Announcements/Admin/PathConfig` endpoints for path configuration management.
- **UI**: Added "Plugin Settings" section in admin config page for path and logging configuration.
- Fully backward compatible - all new features are optional with sensible defaults.

## 0.2.0.2 - 2026-03-29

- Added explicit startup injection outcome handling and error logging when both JS Injector registration and index patch fallback fail.
- Refactored injection methods to return success/failure, improving operational diagnostics in production logs.
- Updated README prerequisites and troubleshooting to recommend JS Injector-first setup for Linux/container deployments.

## 0.2.0.1 - 2026-03-29

- Added cross-platform web index candidate detection for Linux/macOS/container layouts.
- Added support for `JELLYFIN_WEB_INDEX_PATH` and `JELLYFIN_WEB_DIR` environment overrides.
- Fixed release script publish target ambiguity when both `.sln` and `.csproj` are present.
- Added Docker-based Linux Jellyfin test environment and validated banner endpoints/injection.

## 0.2.0.0 - 2026-03-29

- Simplified admin model to severity-only (Info, Warning, Critical).
- Removed user-facing priority controls to reduce confusion.
- Added severity-based ordering in API/store and banner display.
- Fixed edit/save datetime-local timezone behavior that could hide active announcements.
- Fixed dismiss/edit interaction where announcements could disappear unexpectedly.
- Improved admin UX around edit mode and dismiss behavior.

## 0.1.0.0 - 2026-03-28

- Initial release.
- Added announcement CRUD endpoints and admin page.
- Added global banner rendering and dismiss behavior.
