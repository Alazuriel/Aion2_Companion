# Nexforge AION 2 Companion changelog

All notable user-facing changes are recorded here. Versions follow Semantic Versioning.

## [0.1.0] - 2026-10-24

### Added

- Runtime language switching with English, Hungarian, German and Polish support for the application shell and settings.
- Automatic background monitoring on application startup.
- User-facing support package export with optional recent combat history.
- Saved combat history, notes, favorites and fight comparison.
- Modern Nexforge interface for the main window, DPS meter and boss assistant.
- Pet collection status, filtering and map integration.
- Secure in-app updates, a versioned changelog and a branded per-user installer.

### Changed

- Developer tools are hidden unless the application is launched with `--developer`.
- Diagnostic log files are size-limited and rotated automatically.
- Map Helper settings clearly explain administrator and borderless-fullscreen requirements.

### Known issues

- A combat can occasionally end while the fight is still active.
- The Map Helper can occasionally appear when the in-game map is not open.
- Some older feature pages are still being migrated to the new localization system.
