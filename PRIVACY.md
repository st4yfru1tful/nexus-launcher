# Privacy policy

## Summary

Nexus Launcher is designed to work as a local-first launcher. Local discovery,
library management, and launching do not require an account, telemetry consent,
an OpenAI API key, or a cloud service.

This policy describes v0.2.0 behavior. It must be updated before a feature
changes what Nexus stores or sends.

## Data kept on the device

Local launcher functionality may store:

- library names, categories, provider IDs, launch targets, installation paths,
  favorites, hidden-item choices, and user-authored metadata
- scan timestamps, provider diagnostics, preferences, and local request quotas
- a cache needed to avoid repeating local work
- sanitized diagnostics needed to troubleshoot a failure

Installed Nexus stores this under %LOCALAPPDATA%\NexusLauncher. Portable Nexus
stores it in an adjacent **NexusLauncherData** directory. Library/settings data
are JSON. The optional Nexus AI session is separately encrypted for the current
Windows user and is excluded from backups; it is not stored in settings,
diagnostics, or source control.

## Current v0.2 behavior

### Local library

Steam, registry, shortcut, and selected-folder discovery read local Windows
sources. Nexus does not upload a library scan. Selected folders are chosen by
the user; Nexus does not crawl all drives by default.

### Store search

The Steam game search sends the search term, English locale, and a two-letter
country code derived from the current Windows region to Steam's storefront
endpoint. It does **not** send a Nexus library, Steam credentials, ownership
information, files, or payment information. The result is used only to show
catalog entries. When the user explicitly chooses **View in Steam**, Nexus opens
a validated official Steam Store URL in the default browser. Steam controls its
own account, regional, age, ownership, purchase, and download checks.

The WinGet software search invokes the locally installed Windows Package
Manager after the user searches or confirms installation. Query text and a
selected package ID are handled by WinGet and its configured sources under
their own terms.

### Optional Nexus AI metadata

AI metadata is off by default. It can only make a request after all of these
conditions are true:

1. The user enables it in Settings.
2. A production Nexus AI gateway is configured and the user has signed in to
   that developer-owned service through OAuth with PKCE.
3. The user explicitly requests a suggestion for one library entry.
4. The local monthly request limit allows it.

For that request, Nexus sends only a title and any available provider,
publisher, version, executable filename, and parent-folder name. It never
sends a full executable or install path, launch URI or arguments, complete
library, file content, executable binary, document, browser data, password, or
payment information.

The desktop application does not send OpenAI API keys or authenticate users
directly with OpenAI. A deployed Nexus AI gateway is responsible for its own
authentication, model-provider configuration, retention policy, rate limits,
and privacy notice. The public v0.2.0 build does not bundle or configure that
gateway, so no AI request can occur until a maintainer deploys one.

Suggestions are reviewable. Nexus does not automatically run, install,
download, launch, or silently overwrite an entry with AI output. Approval may
fill an empty description and add non-duplicate tags only.

### Backup and diagnostics

Backup/export creates a user-selected local archive containing **library.json**
and, when present, **settings.json**. It is not automatically synced,
uploaded, or encrypted by Nexus. AI session files and cache/session artifacts
are not backup inputs.

The diagnostic export contains the Nexus version, Windows version, process
architecture, local library-item count, Nexus data-folder path, and a UTC
timestamp. It does not include library/settings content or AI tokens. The
data-folder path can still be personal, so users should review it before
sharing.

## Network use and optional features

The baseline local path works without network access. Online features must be
optional, time-bounded, and clear about their purpose.

| Feature | Data boundary | Required behavior |
| --- | --- | --- |
| Steam game discovery | Search term, locale, region code | Show catalog data only; validate URLs; require an explicit browser handoff. |
| WinGet search/install | Query and selected package ID through the local WinGet client | Require an explicit install confirmation. |
| Nexus AI metadata | Minimal title/provider/publisher/version/filename labels | Default off; gateway OAuth; request limit; review before applying. |
| Cloud sync (future) | Only user-selected catalog/settings records | Explicit sign-in/enablement, documented backend, conflict handling. |
| Update checks (future) | Current app version and chosen channel | Show update source and ask before any installer is launched. |

Nexus must not fabricate prices, ownership, installed state, or online results
when a provider is unavailable.

## Telemetry and advertising

Nexus does not include behavioral analytics, advertising identifiers, or
invasive telemetry by default. Any future telemetry requires a documented
purpose, a setting, and a policy update. Raw executable binaries, private
documents, browser data, passwords, and unrelated personal files are never
acceptable telemetry.

## Credentials

API keys, OAuth tokens, certificates, and cloud credentials are sensitive. They
must never appear in logs, diagnostics, exported settings, backups, source
control, or release artifacts. Nexus stores an optional gateway session
encrypted for the current Windows user; disconnecting removes it. A gateway
must keep any model-provider credentials on its server, never in the desktop
client.

## User control

Users can use local functionality with store search, AI metadata, cloud
synchronization, and other connected features disabled. Removing an item from
the Nexus catalog does not uninstall its underlying application. Any future
cloud deletion feature must explain its scope before confirmation.

## Changes to this policy

Changes that expand collection, transmission, retention, or third-party sharing
require review of this file, relevant UI copy, SECURITY.md, tests, and release
notes before merge.
