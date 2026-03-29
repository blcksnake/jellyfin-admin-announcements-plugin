# Changelog

All notable changes to this project will be documented in this file.

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
