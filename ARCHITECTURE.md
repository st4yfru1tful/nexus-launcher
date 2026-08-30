# Nexus Launcher architecture

## Design goals

Nexus is a Windows launcher that stays useful offline. Local cataloguing and
launching take precedence over optional connected integrations.

- The UI stays responsive while discovery runs.
- A broken local manifest, shortcut, or provider does not stop other scans.
- A library does not require an external account.
- Providers never bypass ownership, DRM, licensing, account, regional, age, or
  Windows-access controls.
- Optional connected features minimize data, fail independently, and never
  place secrets in the desktop app.

## Project boundaries

~~~text
NexusLauncher.App
        │ composition root, WPF views/view-models, local services
        ▼
NexusLauncher.Core
        │ domain models, use-case contracts, shared policies
        ▲
NexusLauncher.Discovery
        │ local provider implementations, parsers, normalization

NexusLauncher.UnitTests
        └── deterministic tests for App, Core, and Discovery behavior
~~~

The App project is the composition root. It wires local discovery, persistence,
store search, optional AI gateway services, and WPF view models. Views do not
parse manifests, access the registry, or make external requests directly.

## Local runtime flow

1. Nexus loads locally persisted settings and catalog data.
2. A scan coordinator runs eligible local discovery providers in the
   background.
3. Providers return normalized candidates and non-fatal diagnostics.
4. Core resolves duplicates using durable provider IDs before weaker local
   evidence such as paths and publisher/title.
5. View models publish responsive status, empty, error, and action states.
6. Launch validates the stored local target and starts it using the least
   privileged appropriate mechanism.

Library and settings are local JSON through LibraryRepository and
SettingsService. Backups explicitly include only library/settings data; a
future database or sync adapter needs migration and backup tests first.

## Store discovery boundary

The Store page is deliberately a discovery and handoff surface:

~~~text
User search
  ├── Games: HTTPS Steam storefront catalog search
  │     └── validated app ID → explicit browser handoff to official Steam page
  └── Software: local WinGet search
        └── explicit confirmation → visible WinGet process
~~~

Steam network data is untrusted. The client uses an HTTPS endpoint, disables
redirects, caps response size, accepts only valid app IDs, and constructs the
Store URL itself. A Steam result never becomes a local library entry,
installation request, entitlement, or executable. WinGet output is parsed into
validated package IDs and only starts after confirmation.

## Optional Nexus AI metadata boundary

OpenAI is not an authentication provider for the desktop client. The optional
AI path is a developer-owned Nexus gateway:

~~~text
Selected local item
  │
  ├── user enables AI + requests one suggestion
  ├── minimal record: title/provider/publisher/version/file-name/folder-label
  │
  ▼
Nexus desktop ── HTTPS + OAuth PKCE ──> Nexus AI gateway
                                          └── server-owned model integration
~~~

The desktop client has no API-key field and no client secret. A deployment
configures public DNS HTTPS gateway, authorization, and token endpoints plus a
public OAuth client ID; an unconfigured build disables AI entirely. OAuth uses
state and PKCE with a loopback callback. The per-user session is encrypted
using Windows DPAPI and kept outside settings, diagnostics, and backups.

AiMetadataRequestFactory is the privacy gate. It cannot fall back to a full
install path, launch URI, launch arguments, or entire local catalog. Gateway
responses are size-limited and validated. AI output is a suggestion: only the
user's explicit approval may fill an empty description or add tags; it cannot
alter a title, executable, URI, arguments, provider identity, or launch action.

The current client supports selected-item metadata suggestions only. It does
not claim semantic library search, automatic classification, model-driven
installs, or a deployed public gateway.

## Provider contracts

- **Discovery providers** identify local installations and return normalized
  candidates.
- **Store providers** search or open official pages/package managers; they do
  not invent price, ownership, or installation state.
- **Metadata providers** enrich a known item only with clear matching evidence
  and explicit review.
- **Mod providers** manage only providers whose authentication, licensing, and
  archive rules permit it.
- **Cloud providers** are optional, explicit about synchronized data, and
  conflict-safe.

External calls require timeouts, cancellation, response-size limits, validation,
and understandable failure states.

## Security and releases

Normal scans use current-user permissions. Nexus does not elevate globally,
alter Windows security controls, execute remote content automatically, or
accept executable destinations from remote data.

Release tags build a self-contained win-x64 package, installer, portable ZIP,
and SHA-256 checksums on GitHub-hosted Windows runners. Any future updater must
verify a user-visible version and checksum before asking to run an installer.
