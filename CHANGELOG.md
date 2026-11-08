# Nexforge AION 2 Companion changelog

All notable user-facing changes are recorded here. Versions follow Semantic Versioning.

## Unreleased

## [0.2.2]

**Nexforge Update — Focus the Fight**

This update focuses on combat analysis, encounter tracking, and a more focused Phaseward experience.

We're continuing to refine Companion based on development progress and community feedback, while keeping experimental features separate from tools players can rely on.

### Added

- **Damage Taken Tracking** — View recorded incoming damage for party members in the DPS Meter, Combat History, and Combat Timeline.
- **Incoming Damage Timeline** — Explore when damage was received during an encounter, where detailed combat data is available.
- **Experimental Stagger Indicator** — Phaseward can now display a dedicated Stagger indicator with an estimated countdown when a possible Stagger event is detected.

### Improved

- **Phaseward HUD** — Simplified the live overlay to focus exclusively on Stagger. Other experimental mechanic warnings and predictions are temporarily hidden while we work on their accuracy.
- **Boss Encounter Tracking** — Improved handling of encounters where a boss returns to full health, helping distinguish interrupted attempts from completed fights.
- **Combat History** — Expanded saved encounter details with incoming damage statistics where available.
- **Combat Timeline** — Added support for reviewing incoming damage alongside existing combat analysis.
- **Overlay Studio** — Updated Phaseward positioning and preview behavior to reflect the simplified HUD.
- **In-App Changelog** — Improved the presentation of release notes, including feature descriptions and important notices.
- **Localization** — Updated language resources for English, Hungarian, German, and Polish.

### Phaseward Development Update 💜

Phaseward remains one of our biggest ongoing projects, but it is still experimental.

With this update, we're narrowing the live HUD to Stagger detection while continuing to study other boss mechanics separately.

**Stagger detection and timing estimates are not yet guaranteed to be accurate.** Some events may not be detected, and estimated durations may differ from what happens in-game.

We're taking this approach because we'd rather improve one mechanic at a time than display warnings that players cannot depend on.

Your feedback continues to be incredibly valuable. If you encounter incorrect Stagger timing or missing detections, please report them through Companion's Feedback system.

**Thank you for helping us improve Phaseward, one encounter at a time!** 💜

### Known Issues

- **Phaseward:** Stagger detection and countdown timing remain experimental. Other boss mechanic warnings are temporarily unavailable in the live HUD.
- **Damage Taken:** Older saved fights may not contain incoming damage data.
- **Combat Analysis:** Some damage events may be unavailable when the necessary combat information could not be captured.
- **Online Features:** Connected services and research features depend on service availability.

---

Developed by **Nexforge Studios**.

*An independent fan-made companion application. Not affiliated with or endorsed by NCSOFT.*

## [0.2.1]

**Nexforge Update — Every Fight Tells a Story**

We're continuing to improve AION 2 Companion, with a focus on combat analysis, usability, update management, and the future of Phaseward.

This update expands what you can learn from your encounters and introduces new ways to help us improve the tools you use.

### Added

- **Player Death Tracking** — Added an optional death counter next to player names in the DPS Meter during supported boss encounters.
- **Combat Position Statistics** — Added Front, Back, and Unknown hit information to combat details when reliable data is available.
- **Boss Status Overlay** — Added a separate window for boss HP information, encounter timing, and estimated time to defeat when available.
- **Phaseward Community Research** — Added optional settings for sharing boss observations to help improve future encounter analysis.
- **Detailed Boss Research** — Added a separate, opt-in research-sharing option for players who want to contribute more detailed encounter data.
- **Installation Health Monitoring** — Added support for checking application files and preparing repairs when verification information is available.

### Improved

- **Combat History Redesign** — Updated the combat archive with a cleaner layout, improved encounter navigation, sorting options, and more accessible player statistics.
- **Combat Timeline** — Reworked timeline rendering, zoom handling, event loading, and skill navigation to improve interaction with complex encounters.
- **Player Details** — Improved the presentation of damage, healing, deaths, and available positional statistics.
- **Saved Fight Display** — Improved class icon handling when reviewing previously recorded encounters.
- **Update Center** — Improved support for smaller, compatible update packages, with the full installer available as a fallback.
- **Localization** — Updated English, Hungarian, German, and Polish language resources.

### Fixed

- Addressed an initialization issue that could cause the redesigned Combat History view to fail while loading its sorting controls.

### Security & Privacy

- Sharing Phaseward research data is optional.
- Detailed research uploads require separate consent. They may include recorded network data containing identifiers or location information.
- Players can review the available sharing controls and disable research uploads in Settings.
- Installation health checks and repair preparation are supported where compatible verification information is available.

### Phaseward — Still Under Development 💜

We know many of you are looking forward to a reliable boss encounter assistant.

Phaseward now includes additional tools for collecting and studying boss observations, but **it is not yet reliable enough to depend on for encounter warnings**. Some mechanics may be missed, detected too late, or displayed inaccurately.

We're continuing to work on these limitations, and we'd love your help.

If you experience an incorrect warning, a missing mechanic, or unexpected behavior, please let us know through the Companion Feedback system.

You can also choose to participate in Phaseward's community research features. Every useful observation helps us better understand what needs improvement.

**Thank you for your patience and for helping us build something better together!**

### Known Issues & Limitations

- **Phaseward:** Boss mechanic detection and warning timing are still experimental. Do not rely on Phaseward for critical gameplay decisions.
- **Combat Statistics:** Death and positional hit information may be unavailable for some encounters. Unknown values are not treated as confirmed zeroes.
- **Combat Timeline:** Large or complex encounters may still require further performance improvements.
- **Online Features:** Group submissions, research sharing, and other connected features depend on service availability and eligibility.

---

Developed by **Nexforge Studios**.

*An independent fan-made companion application. Not affiliated with or endorsed by NCSOFT.*

## [0.2.0]

**Nexforge Update — Beyond the Damage**

A new chapter for AION 2 Companion.

This update brings new tools for boss encounters, more freedom to customize your overlays, and improvements across the application.

### Added

- **Nexforge Phaseward** — A new boss encounter assistant featuring mechanic information, encounter tracking, and timing estimates for supported bosses.
- **Overlay Studio** — Customize the position, size, visibility, and opacity of your in-game HUD elements.
- **HUD Presets** — Choose between Minimal, Combat, and Compact layouts, or create your own.
- **Diagnostics Center** — Review reports after unexpected crashes or freezes and decide whether to share them with Nexforge Support.
- **Group Fight Preview** — View your party's recorded damage, DPS, healing, and HPS in the redesigned Leaderboard submission interface.

### Improved

- Improved the DPS Meter and Phaseward HUD layouts.
- Updated the Combat Timeline's event display and interaction handling.
- Redesigned the Leaderboard submission interface.
- Updated English, Hungarian, German, and Polish interface text.
- Improved the presentation of update information.

### Security & Privacy

- You control whether diagnostic reports are shared or deleted.
- Diagnostic reports are designed to avoid collecting personal gameplay details.

### A Note About Phaseward 💜

We want to be transparent with you.

**Phaseward is still a work in progress.**

Some boss encounters and mechanics are working as expected, while others may have inaccurate timers, missing warnings, or mechanics that aren't detected correctly.

We've spent a significant amount of time improving Leaderboards and working on other important parts of Companion. Unfortunately, that meant Phaseward didn't receive as much attention and testing as we originally wanted.

**We're sorry about that.**

We know how important reliable boss mechanics are, especially when every second matters during a fight.

But we're not giving up on Phaseward. Far from it.

**And this is where we'd love your help!**

If you notice something wrong, please share it through Companion's Feedback system. Tell us which boss you were fighting, what Phaseward displayed, and what actually happened.

Every report helps us understand what needs fixing and brings Phaseward one step closer to becoming the boss assistant we want it to be.

We can't promise that every mechanic will work perfectly right away, but we're committed to improving it together with the community.

**Thank you for your patience, your feedback, and for being part of Nexforge.** 💜

### Known Issues

- Phaseward may miss mechanics or display inaccurate timing estimates for certain boss encounters.
- Full Group Leaderboard submissions are not yet generally available.
- Some Combat Timeline interactions may still experience visual alignment or performance issues.

---

Developed by **Nexforge Studios**.

*An independent fan-made companion application. Not affiliated with or endorsed by NCSOFT.*

##  [0.1.91]

**Nexforge Update — Language Improvements**

This update focuses on language support and localization improvements.

### Improved

- Updated the English, Hungarian, German, and Polish language packs.
- Refreshed the bundled translations available in AION 2 Companion.

### Requirements

- Windows 10 or Windows 11 (64-bit)
- [Npcap](https://npcap.com/#download) for network packet capture
- AION 2
- Administrator mode and borderless fullscreen for the Map Helper

### Installation

1. Download `Nexforge-AION2-Companion-Setup-0.1.91.exe` below.
2. Run the installer and follow the instructions.
3. Restart AION 2 Companion after updating.

No separate .NET installation is required.

---

Developed by **Nexforge Studios**.

*An independent fan-made companion application. Not affiliated with or endorsed by NCSOFT.*

##  [v0.1.9] - 2026-10-01

**Nexforge Update**

This update focuses on easier character management, a redesigned update experience, and improvements to character profiles.

### Added

- Added a Verified Characters section in Character Center, allowing you to view characters linked to your Nexforge Account.
- Added the ability to sync your verified characters and select which character to view.
- Added automatic update notifications when a new Companion version is available.
- Added an improved Update Center showing release notes, download progress, and installation options.
- Added optional background downloads for new updates. Installation remains under your control.

### Improved

- Improved character switching and profile loading to better keep character information separate.
- Improved verification status handling when changing characters or switching Nexforge Accounts.
- Improved the appearance of character profiles, including verification indicators and long text values.
- Improved update management with download cancellation and clearer progress messages.
- Update installation is designed to avoid interrupting active game monitoring sessions.
- Improved the display of release notes inside the application.

### Requirements

- Windows 10 or Windows 11 (64-bit)
- [Npcap](https://npcap.com/#download) for network packet capture
- AION 2
- Administrator mode and borderless fullscreen for the Map Helper

### Installation

1. Download the official Companion installer from the release assets.
2. Run the installer and follow the instructions.
3. Install Npcap if it is not already available.
4. Launch AION 2 Companion.

Existing users can use the in-app update feature when a newer version becomes available.

No separate .NET installation is required when using the official self-contained installer.

---

Developed by **Nexforge Studios**.

*An independent fan-made companion application. Not affiliated with or endorsed by NCSOFT.*

##  [v0.1.8] - 2026-10-01

### Added
- Introduced Character Center with character search, detailed profiles, local caching, and offline access to previously fetched character data.
- Added Nexforge Account-based character verification workflow with a dedicated verification window and persisted challenge state.
- Added class-specific skill catalog integration using local ForgeData packs.
- Added in-app Leaderboards with boss, class, metric and search filters, public rankings, and account submission history.
- Added leaderboard submissions from saved boss encounters, including available recorded combat evidence and submission withdrawal support.
- Added an encrypted NSL combat history format with integrity verification.
- Added server identification support using the encrypted `AION2_Servers.nfg` data pack.
- Added boss HP tracking, estimated time-to-defeat calculations, and combat-ending rule helpers.
- Added new character-related UI assets and character/verification test coverage.

### Changed
- Reworked the Characters section, character search, character details and associated navigation.
- Redesigned the Leaderboards interface and submission review dialog.
- Improved combat history presentation and saved encounter metadata.
- Updated combat history storage to use NSL files for new and updated encounters.
- Expanded localization support for character-related functionality.
- Improved local skill artwork resolution and asset packaging.

### Fixed
- Improved reporting of Leaderboard API and network errors instead of displaying only a generic unavailable message.
- Improved protection against duplicate or inconsistent combat evidence in leaderboard submissions.
- Refined damage, HP and encounter-ending processing for supported combat events.
- Improved character refresh handling with cooldowns, cached data and cancellation of outdated profile requests.

### Security
- Added authenticated, integrity-checked storage for local combat history files.
- Restricted leaderboard submission identity fields to available encounter and server data rather than arbitrary manual server-name entry.

### Migration
- Existing JSON combat history files remain readable and are migrated to NSL when processed by the new storage implementation.
- The new combat history encryption key is tied to the local Windows user environment. Backup and recovery behavior must be verified before release.

### Known limitations
- Online character information depends on the availability of the companion API and its upstream data sources.
- Leaderboard submission and approval require a compatible backend deployment.
- Some skill artwork requires separately prepared local asset packs.
- Automated tests, installer validation and production API integration are not yet confirmed for this release candidate.

### Verification status
Source implementation reviewed. Final build, regression testing, deployment verification, and Project Lead approval remain pending.

## [0.1.7] - 2026-10-29

### Added

- Added Nexforge Account connection through the default browser, with secure saved sessions, sign out and a cancellable connection flow.
- Added an account area and character workspace prepared for equipment, skills, stigmas, Daevanion, talents, Arcana and character statistics.
- Added automatic local character detection for the character workspace while online character data is still being expanded.
- Added packaged Nexforge Language files for English, Hungarian, German and Polish, plus community language import in Settings.
- Added the standalone Nexforge NFL Language Editor as an optional release download for community translators.
- Added healing-only encounter support and healing event tracking alongside damage in the live meter and combat history.

### Changed

- Reworked the main navigation around Home, Account, DPS Meter, DPS Log, Timers and a compact More section.
- Moved encrypted ForgeData packs into a dedicated runtime folder and kept language packs in `Data/Language`.
- Expanded localization to cover messages generated while the application is running, not only static interface labels.
- Prepared reusable image and icon handling for future character equipment and skill data.

### Fixed

- Prevented duplicate combat frames from inflating damage or healing totals.
- Improved healing recognition so resource restoration and unrelated effects are not counted as healing.
- Protected DPS and HPS rates from unrealistic values during extremely short encounters.
- Improved account connection feedback for cancellation, offline use, expired sessions and unavailable services.

## [0.1.6] - 2026-10-26

### Added

- Redesigned Settings as a responsive Nexforge control center with clear General, Capture, Combat, Overlay, Event Timer, Data, Pet and Diagnostics sections.
- Prepared a clearly disabled Nexforge Account area for future character, Discord and cloud-profile linking without enabling unfinished online actions.
- Added a Nexforge Account teaser to the Dashboard and a dedicated Characters module preview marked as under forge.
- Added an opt-in browser login UI rehearsal that locks the client and defers automatic capture until the test flow is confirmed.
- Added a live local data-pack status and kept every existing Settings preference connected to its saved value.
- Added persistent All/Bosses modes to the live DPS overlay and Combat Log.
- Added encrypted `AION2_Bosses.nfg` runtime data support with template-ID and legacy-name matching.
- Added boss HP and enrage status to the DPS overlay when verified data is available.

### Changed

- Simplified the sidebar by grouping secondary and unfinished modules under an expandable More menu.
- Polished the Dashboard and Characters empty states so missing character data and unfinished features are presented clearly.
- Boss mode keeps recording trash encounters but excludes non-boss damage from the active view and historical boss analysis.

### Fixed

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
