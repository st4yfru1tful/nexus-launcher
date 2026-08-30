# Privacy policy

## Summary

Nexus Launcher is designed to be useful as a local-first launcher. Basic local discovery and launching should not require an account, telemetry consent, an OpenAI key, or a cloud service.

This document is a product and contribution policy for the repository. It must be updated before an implemented feature changes the data Nexus stores or sends. A provider is not considered supported until its user-facing settings and data handling are documented.

## Data kept on the device

Local launcher functionality may need to store information such as:

- application/library names, categories, provider IDs, launch targets, and installation paths
- scan timestamps, provider diagnostics, user preferences, favorites, and hidden-item choices
- optional locally tracked usage information, if that feature is enabled in a release
- local caches required to avoid repeating discovery or metadata work
- sanitized diagnostic logs needed to troubleshoot a failure

In the initial release, Nexus stores its local catalog and settings under `%LOCALAPPDATA%\NexusLauncher\data`, with cache and log folders under `%LOCALAPPDATA%\NexusLauncher`. The catalog and settings are JSON files. This information is for operating the launcher and remains under the user’s Windows profile unless the user explicitly uses an online feature.

## Current 0.1 behavior

- Steam, registry, shortcut, and selected-folder discovery read local Windows sources. They do not upload a library scan.
- The Store page invokes the locally installed Windows Package Manager (`winget`) only after the user searches or confirms an install. WinGet may contact the package sources configured on the device; its query text and selected package ID are handled by WinGet under its own terms and privacy policy.
- Backup/export creates a user-selected local ZIP-format backup containing `library.json` and, when present, `settings.json`. It is not automatically synced, uploaded, or encrypted by Nexus.
- The initial release does not implement online metadata lookup, OpenAI/AI classification, third-party mod browsing, or remote cloud synchronization. These features are presented as unavailable rather than as controls that silently enable an undeclared service.
- The source does not include a telemetry SDK or analytics endpoint for the initial release.

## Network use and optional features

The baseline local discovery path should work without network access. Any online feature must be optional and state its purpose before it sends a request.

| Feature type | Permitted data boundary | Required behavior |
| --- | --- | --- |
| Metadata lookup | Only the identifiers and minimal title/publisher data needed to match an item | Respect provider terms, use timeouts, cache results, and allow disabling it. |
| AI executable classification | Minimal executable metadata such as file name, product name, publisher, version, and an install-folder label | Never upload executable binaries, documents, photos, browser data, credentials, or arbitrary file contents. Require an explicit AI setting and key. |
| Cloud sync (future) | Only the catalog/settings records chosen for synchronization | Require explicit sign-in/enablement, disclose the backend, and provide conflict handling. |
| Store or mod provider | The search/install request and provider-required identity data | Use permitted APIs, show the source, and require explicit user action before install/download. |
| Update checks | Current app version and requested release channel | Show the version/update source and ask before downloading or launching an installer. |

Nexus must not fabricate prices, ownership, installed state, or online results when a provider is unavailable.

## Telemetry and advertising

The repository policy is not to add behavioral analytics, advertising identifiers, or invasive telemetry by default. If a future release adds any telemetry, it must be clearly described here, disabled by default unless there is a compelling legal/operational reason otherwise, and controllable in the product. Raw executable binaries, private documents, browser data, passwords, and unrelated personal files are never acceptable telemetry.

## Credentials

API keys, OAuth tokens, and cloud credentials are sensitive. They must not be included in logs, diagnostics, crash reports, exported settings, source control, or release artifacts. A manually supplied secret should use an OS-protected secret mechanism where the feature supports one; environment variables are appropriate for local development only.

## Diagnostic reports

The initial diagnostic export contains the Nexus version, Windows version, process architecture, local library item count, the Nexus data-folder path, and a UTC timestamp. It does not include the catalog/settings file content, but the data-folder path can still be personal. Users should review a diagnostic report before sharing it. Contributors should use sanitized paths and values in issue reports and test fixtures.

## User control

Users should be able to use local functionality with online metadata, AI, cloud synchronization, and other online services turned off. Removing an item from the Nexus catalog must not uninstall the underlying application. When data removal or cloud deletion is implemented, its scope and consequences must be clear before confirmation.

## Changes to this policy

Changes that expand collection, transmission, retention, or third-party sharing require a review of this file, the relevant UI copy, `SECURITY.md`, tests, and release notes before merge.
