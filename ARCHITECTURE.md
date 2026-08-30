# Nexus Launcher architecture

## Design goals

Nexus is a Windows launcher that should remain useful when offline. Its architecture therefore puts local cataloguing and launching ahead of optional online integrations. The key constraints are:

- The UI must stay responsive while discovery runs.
- A broken manifest, shortcut, or provider must not stop other providers.
- A user’s catalog must not depend on an external account.
- Providers must never bypass ownership checks, DRM, licensing, or Windows access controls.
- Secrets must be opt-in, stored outside source control, and never logged.

The codebase uses WPF on .NET 9 for a dependable Windows desktop deployment story. The project may grow, but a new project is justified only when it creates a real dependency boundary.

## Project boundaries

```text
NexusLauncher.App
        │ composition root, WPF views/view-models, user interactions
        ▼
NexusLauncher.Core
        │ domain models, use-case contracts, value objects, shared policies
        ▲
NexusLauncher.Discovery
        │ local provider implementations, parsers, normalization
        └─────────────────────────────────────────────────────────────

NexusLauncher.UnitTests
        └── tests Core and Discovery without requiring a live launcher install
```

### `NexusLauncher.Core`

Core owns concepts that are meaningful independently of the UI or a particular platform source: library entries, categories, launch targets, scan results, provider diagnostics, and discovery contracts. It must not reference WPF or call the file system/registry directly. This makes behavior testable and prevents provider details from leaking through the application.

### `NexusLauncher.Discovery`

Discovery turns local evidence into normalized candidates. Each provider owns its source-specific parsing and returns diagnostics instead of throwing failures across the scan. Known examples include Steam library manifests and Windows application/shortcut sources. Providers should be cancellation-aware, avoid scanning arbitrary drives by default, and preserve enough evidence for duplicate resolution without exposing it in the UI unnecessarily.

### `NexusLauncher.App`

The App project is the composition root. It wires services together, runs background work, maps application models to WPF view models, and presents loading, empty, disabled, and error states. View models should request work through Core contracts; they should not parse manifests or access the registry themselves.

### Tests

Unit tests live in `tests/NexusLauncher.UnitTests`. Parsing, normalization, duplicate handling, and classification rules should be deterministic and use fixtures rather than a developer’s installed games. A provider requiring an actual Windows installation belongs in a separately named integration test only when it can run safely in CI or be explicitly skipped.

## Runtime flow

1. The application starts and loads its locally persisted catalog if one is available.
2. A scan coordinator schedules eligible local discovery providers in the background.
3. A provider reads only its supported local source, produces candidates and diagnostics, and respects cancellation.
4. Core normalizes identities and resolves duplicates so the user sees one logical item rather than the same installation from a shortcut and a launcher manifest.
5. The UI updates through view models on the dispatcher and displays provider failures as non-fatal status information.
6. A launch request validates the stored target and starts a local executable or provider URI using the least privileged appropriate mechanism.

The initial release persists its app-facing catalog and settings as local JSON through `LibraryRepository` and `SettingsService`. Core contracts are the boundary future persistence work should adopt. A SQLite-backed catalog, metadata cache, session tracker, or cloud adapter must include migration/backup tests before it can replace user data.

## Provider contracts

Provider interfaces are narrow on purpose:

- **Discovery providers** identify local installations and return normalized candidates.
- **Metadata providers** may enrich a known item only when they have a valid identifier or sufficiently strong matching evidence.
- **Store providers** may search or open official store pages; they must not fabricate prices or ownership state.
- **Mod providers** may manage only sources whose APIs, licensing, and authentication flows allow it.
- **Cloud providers** must be optional, explicit about the data synchronized, and conflict-safe.

Adding a provider must not add a hard dependency on an account or network availability. Put all network calls behind timeouts, cancellation, rate limits, and an opt-in setting.

## Composition and lifetime

Object composition belongs in one application boundary, rather than in views or code-behind. Prefer constructor injection and explicit interfaces over service locators or global mutable state as the service graph grows. Long-lived services such as the catalog, settings, logging, and scan coordinator may be singletons when they are thread-safe. Per-operation objects—such as a scan context or parser—should be transient and receive a `CancellationToken` from their caller.

## Data and identity

An installation can be observed from multiple sources. Identity resolution should prioritize durable identifiers in this order where available:

1. Provider-owned identifier (for example, a Steam app ID).
2. Windows package or installed-application identity.
3. Normalized install directory and executable path.
4. Stable local metadata such as product name and publisher.
5. User-confirmed mapping.

Names alone are not sufficient to merge items. A lower-confidence candidate should remain separate or ask for a user decision instead of silently overwriting catalog data.

## Security model

Normal scans use the current user’s permissions. Nexus must not attempt to read protected Windows application folders, alter Defender/SmartScreen/UAC, elevate globally, execute downloaded content automatically, or accept executable paths from untrusted remote input. Paths, arguments, manifests, archives, and provider responses are untrusted inputs and must be validated before use.

Optional credentials belong in a supported OS secret store or an environment variable during development. They do not belong in JSON settings, logs, test fixtures, GitHub Actions files, or commits.

## Updates and releases

Releases are built on GitHub-hosted Windows runners from a version tag. The release pipeline packages a self-contained `win-x64` app, compiles the Inno Setup installer, produces a portable ZIP, computes SHA-256 hashes, verifies the outputs, and uploads them to the matching GitHub Release. The updater, if added, must verify a user-visible version and checksum before asking the user to run an installer.

## Future extensions

The requested product area includes online metadata, AI assistance, cloud synchronization, storefronts, and mod management. Those are extension points, not implicit guarantees. Each extension needs a separate security/privacy review, failure state, settings surface, and automated tests before being presented as supported functionality.
