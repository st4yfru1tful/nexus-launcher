# Changelog

All notable changes to Nexus Launcher will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and version numbers follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

No unreleased changes yet.

## [0.1.1] - 2026-08-29

### Fixed

- Prevented the startup crash caused by WPF freezing shared theme brushes before Nexus applied the saved theme.
- Added a regression test covering application of a theme to a frozen WPF brush resource.

### Changed

- Rebuilt the launcher UI with a clearer visual hierarchy, resilient dark and light themes, a refined navigation rail, responsive library details, polished overlays, and custom theme-aware dropdowns.
- Added an intentional empty-state panel to the Library details view.

## [0.1.0] - 2026-08-29

Initial public prerelease of Nexus Launcher for Windows.

### Added

- A local-first WPF library with search, category filters, favorites, hide/remove controls, manual `.exe` additions, launch, and open-folder actions.
- Local Steam-manifest, Windows Registry, and Start menu discovery with duplicate resolution and source-specific scan diagnostics.
- A transparent WinGet search/install handoff, safe local mod-archive extraction, and opt-in local export/restore backups.
- A self-contained Windows x64 portable ZIP, Inno Setup installer, SHA-256 checksums, and tag-driven GitHub release workflow.
- Unit coverage for discovery, Steam VDF parsing, path normalization, duplicate handling, executable classification, and current/legacy WinGet result tables.

### Security and privacy

- The application runs as the current user and has no telemetry, login, bundled credentials, background updater, cloud upload, metadata lookup, or AI request path in this release.
- Backups are local ZIP files only; restore validates their required JSON documents before replacing live data.

### Known limitations

- This prerelease does not include Epic, GOG, EA, Ubisoft, Battle.net, Xbox, or itch.io providers; metadata enrichment; connected cloud sync; playtime tracking; automatic updates; or binary code signing.

[Unreleased]: ../../compare/v0.1.1...HEAD
[0.1.1]: ../../compare/v0.1.0...v0.1.1
[0.1.0]: ../../releases/tag/v0.1.0
