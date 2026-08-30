# Changelog

All notable changes to Nexus Launcher will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and version numbers follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

No unreleased changes yet.

## [1.0.0] - 2026-08-30

### Added

- Optional on-device metadata suggestions using a dedicated, Nexus-owned
  Ollama child process on a random IPv4 loopback port.
- Local runtime and model availability checks with clear missing-runtime,
  missing-local-model, ready, and failure states, including explicit local
  text-generation capability checks.
- Structured, bounded local model requests and review-before-apply metadata
  output using only the existing privacy-minimized one-item contract.
- Branded window, taskbar, and installer icons, a packaged Nexus logo and
  ambient artwork, and packaged cover fallbacks for unavailable artwork.
- Safe propagation and extraction of icons from local executables and shortcut
  metadata with UNC/network-path rejection and a bounded cache.

### Changed

- Metadata intelligence now offers an on-device path without requiring a
  hosted Nexus gateway. Nexus Cloud remains a separate, explicitly selected
  provider and never receives an automatic fallback request.
- The launcher theme system, responsive spacing, empty states, controls, and
  navigation iconography were refined for a coherent 1.0 visual system.
- Release, assembly, packaging, and generated release-note defaults now use
  version 1.0.0.

### Security and privacy

- Nexus starts Ollama directly without a shell, binds it to a random loopback
  port, sets `OLLAMA_NO_CLOUD=1`, disables model keep-alive, bypasses proxies
  for its loopback client, and stops the child process when the provider is
  disposed.
- On-device AI uses only an already-downloaded text-generation model, rejects
  embedding-only and cloud models plus remote endpoints, and does not install
  Ollama, pull models, browse the web, or expose tools.
- Closing Nexus cancels active local metadata requests and immediately stops
  only the isolated Ollama process tree that Nexus started.
- Both metadata providers send only the selected item's title and optional
  provider, publisher, version, executable filename, and parent-folder label.
  Full paths, launch fields, libraries, files, and binaries remain excluded.
- The public build contains no hosted Nexus Cloud gateway, OpenAI API key, or
  end-user OpenAI OAuth flow. A configured gateway keeps any model-provider
  credential on its server.
- Remote Store artwork and local icon extraction fail into packaged/vector
  fallbacks instead of producing missing-image states.

### Known limitations

- On-device metadata intelligence requires a separately installed Ollama
  runtime and at least one already-downloaded local text-generation model.
  Nexus intentionally does not install either prerequisite.
- Nexus Cloud requires a separately deployed Nexus identity service and AI
  gateway; neither service is included with this release.
- Metadata intelligence suggests metadata for one selected item. Semantic
  library search, automatic background enrichment, and AI store ranking are
  not included.
- Epic, GOG, EA, Ubisoft, Battle.net, Xbox, and itch.io discovery; cloud sync;
  an automatic updater; and binary code signing are not included.

## [0.2.0] - 2026-08-29

### Added

- Steam Store game discovery with search, price/platform presentation, trusted image handling, and an explicit browser handoff to the validated official Store page.
- Clear Store scopes for Steam games and WinGet software, plus cancellation/stale-result protection when the scope or query changes.
- An optional, disabled-by-default Nexus AI metadata suggestion flow for a single selected library item.
- Privacy-minimized AI request construction, local monthly request controls, review-before-apply metadata updates, and encrypted per-user session storage.
- OAuth authorization-code-with-PKCE client support for a developer-owned Nexus AI gateway, with strict HTTPS configuration and bounded response validation.
- Nexus AI gateway deployment documentation.

### Changed

- Store and AI UI now explain the trusted-provider handoff and exact AI metadata boundary.
- Project privacy, security, architecture, and roadmap documentation now describe v0.2 behavior accurately.

### Security and privacy

- Steam results cannot provide arbitrary browser destinations: Nexus constructs official Store URLs from validated numeric app IDs.
- Steam and gateway clients use HTTPS, disabled redirects, timeouts, response-size limits, and safe failure states.
- The desktop app does not ask for, store, or directly use an OpenAI API key or OpenAI OAuth token.
- v0.2.0 does not deploy or configure a production Nexus AI gateway, so AI remains unavailable unless a maintainer supplies one.

### Known limitations

- AI in this release is a secure client foundation for selected-item metadata suggestions, not a deployed service, semantic search, automatic classification, store ranking, or metadata cache.
- Steam discovery does not establish ownership, availability, age eligibility, purchase state, download state, or installation state.

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

[Unreleased]: ../../compare/v1.0.0...HEAD
[1.0.0]: ../../compare/v0.2.0...v1.0.0
[0.2.0]: ../../compare/v0.1.1...v0.2.0
[0.1.1]: ../../compare/v0.1.0...v0.1.1
[0.1.0]: ../../releases/tag/v0.1.0
