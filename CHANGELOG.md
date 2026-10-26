# Nexforge AION 2 Companion changelog

All notable user-facing changes are recorded here. Versions follow Semantic Versioning.

## Unreleased

- Redesigned Settings as a responsive Nexforge control center with clear General, Capture, Combat, Overlay, Event Timer, Data, Pet and Diagnostics sections.
- Prepared a clearly disabled Nexforge Account area for future character, Discord and cloud-profile linking without enabling unfinished online actions.
- Added a Nexforge Account teaser to the Dashboard and a dedicated Characters module preview marked as under forge.
- Added an opt-in browser login UI rehearsal that locks the client and defers automatic capture until the test flow is confirmed.
- Added a live local data-pack status and kept every existing Settings preference connected to its saved value.
- Simplified the sidebar by grouping secondary and unfinished modules under an expandable More menu.
- Polished the Dashboard and Characters empty states so missing character data and unfinished features are presented clearly.
- Added persistent All/Bosses modes to the live DPS overlay and Combat Log.
- Boss mode keeps recording trash encounters but excludes non-boss damage from the active view and historical boss analysis.
- Added encrypted `AION2_Bosses.nfg` runtime data support with template-ID and legacy-name matching.
- Added boss HP and enrage status to the DPS overlay when verified data is available.
- Automatic DPS overlay display no longer activates the window or steals focus from AION 2.
- Pet Intelligence is now shown only after the map matcher has recognized the game map.

## [0.1.5] - 2026-10-25

### Added

- Nexforge Event Timers with live countdowns for verified recurring AION 2 events.
- Region-aware schedules for Global, Korea and Taiwan, including daylight-saving handling.
- Per-event advance warning, start notification, sound and visual alert settings.
- A compact, movable and resizable timer overlay with individually selectable event icons.
- Persistent alert history so the same event occurrence is not announced twice after a restart.

### Changed

- Event schedules now come from an encrypted Nexforge data pack and can be updated without changing application code.
- The guided tour introduces Event Timers after updating to version 0.1.5.

### Fixed

- Rejected malformed combat records that could create impossible multi-million damage hits.
- Direct player skills are now verified against the exact class skill database instead of a loose two-digit prefix.
- Stale buffered summon hits can no longer restart or switch a finished encounter.
- A single-instance guard prevents two Companion processes from recording every fight twice.

## [0.1.1] - 2026-10-25

### Added

- Live DPS tracking with party rankings, total damage, critical-hit data and healing statistics.
- Persistent combat history with encounter search, favorites, notes and fight comparison.
- Per-player skill breakdowns and interactive combat timelines.
- Pet collection tracking with ownership state, level filters and Pet Farm Helper.
- Interactive map overlay with pet spawn locations and focused pet filtering.
- Movable DPS, player-details and timeline overlays with saved window placement.
- English, Hungarian, German and Polish interface languages.
- Optional automatic capture when the Companion starts.
- System tray support with configurable close behaviour.
- Built-in update checking, release changelog and verified installer downloads.
- Nexforge-styled per-user Windows installer with no separate .NET installation required.
- Version-aware interactive onboarding after first installation and feature updates.
- User-controlled support package export for troubleshooting.

### Changed

- Redesigned the main window and combat tools with the Nexforge visual system.
- The DPS navigation item now opens and closes the live meter directly.
- Unfinished modules display a clear **Still under forge** state instead of demo content.
- Developer tools are hidden during normal use and appear only in developer mode.
- Diagnostic logging is optional, size-limited and automatically rotated.
- Map Helper settings now explain administrator and borderless-fullscreen requirements.

### Known issues

- An encounter can occasionally be closed while combat is still active.
- The Map Helper can occasionally appear when the in-game map is not open.
- The Map Helper is most reliable in borderless fullscreen and requires the Companion to run as administrator.
- Healing totals may be incomplete while support for additional healing effects is being expanded.
- Boss timers and class guides are still under forge and are not available in this release.
