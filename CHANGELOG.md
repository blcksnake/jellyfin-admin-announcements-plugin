# Changelog

All notable changes to this project will be documented in this file.

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
