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
store search, explicitly selected metadata providers, and WPF view models.
Views do not parse manifests, access the registry, or make external requests
directly.

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

## Optional metadata intelligence boundary

Metadata intelligence is off by default and has two explicitly selected
providers. There is no silent local-to-cloud fallback.

~~~text
Selected local item
  │
  ├── user enables metadata intelligence + requests one suggestion
  ├── minimal record: title/provider/publisher/version/file-name/folder-label
  │
  ├── On-device AI
  │     └── random IPv4 loopback port ──> Nexus-owned Ollama child
  │           OLLAMA_NO_CLOUD=1; compatible downloaded text model only
  │
  └── Nexus Cloud (only when externally configured)
        └── HTTPS + OAuth PKCE ──> developer-owned Nexus AI gateway
                                     └── server-owned model integration
~~~

For the on-device provider, Nexus starts and owns a dedicated `ollama.exe`
process, rejects remote endpoints and cloud-model names, disables Ollama cloud,
and communicates only over its random loopback endpoint. It neither installs
Ollama nor downloads a model. No web or tool capability is supplied. Requests
use a structured-output schema, and responses remain bounded and validated.

For Nexus Cloud, the desktop client has no API-key field and no client secret.
A deployment configures public DNS HTTPS gateway, authorization, and token
endpoints plus a public OAuth client ID. OAuth uses state and PKCE with a
loopback callback. The per-user session is encrypted using Windows DPAPI and
kept outside settings, diagnostics, and backups. The public build includes no
hosted gateway or identity service.

OpenAI is not an authentication provider for the desktop client. There is no
embedded OpenAI API credential or end-user OpenAI OAuth flow. A gateway operator
that chooses OpenAI must keep its application credential on the server.

AiMetadataRequestFactory is the privacy gate. It cannot fall back to a full
install path, launch URI, launch arguments, or entire local catalog. Gateway
responses are size-limited and validated. AI output is a suggestion: only the
user's explicit approval may fill an empty description or add tags; it cannot
alter a title, executable, URI, arguments, provider identity, or launch action.

The current providers support selected-item metadata suggestions only. They do
not claim semantic library search, automatic classification, model-driven
installs, or a deployed public gateway.

## Visual asset and icon flow

Window, taskbar, installer, logo, ambient, and cover-fallback assets are
packaged with the application. A remote Store image or local executable icon
is an enhancement, not a required texture.

Discovery preserves an explicit local icon path when a provider exposes one
and otherwise uses the validated executable path. The WPF icon service rejects
UNC/network paths, extracts and freezes local Windows icons, and keeps a bounded
cache keyed by path and file timestamp. Extraction failures return `null`, so
the view's packaged or vector fallback remains visible. Store artwork follows
its separate HTTPS host/size validation boundary and falls back to packaged
cover art.

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
