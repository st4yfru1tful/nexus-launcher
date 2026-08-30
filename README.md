# Nexus Launcher

![Nexus Launcher mark](assets/nexus-mark.svg)

Nexus Launcher is a local-first Windows launcher for bringing installed games
and desktop applications into one searchable library. It is a WPF application
on .NET 9 for 64-bit Windows 10 (22H2 or later) and Windows 11.

> **Release status:** Nexus v1.0.0 is the first stable release of the
> documented local-launcher feature set. Connected services and launcher
> providers listed as future work are not implied by the 1.0 label.

## What Nexus does now

- Builds a local library from Steam manifests, installed-app registry entries,
  Start Menu shortcuts, and folders you explicitly select.
- Searches, filters, launches, favorites, hides, removes, and opens folders
  for local entries without uninstalling anything.
- Searches Steam's public storefront catalog for games, showing catalog
  listings and opening the official Steam page only after you choose it.
- Searches WinGet for Windows software and starts WinGet only after an
  explicit install confirmation.
- Offers optional review-before-apply metadata suggestions through an isolated
  on-device Ollama process using an already-downloaded text-generation model.
- Includes an optional OAuth client for a developer-operated Nexus Cloud AI
  gateway when that external service is configured.
- Imports user-selected ZIP mods safely into a selected game's Mods folder,
  and makes local library/settings backups.
- Ships application and installer branding, built-in cover fallbacks, and safe
  extraction of icons from local executables and shortcuts.

Nexus never assumes game ownership, purchases, downloads, installs, or bypasses
Steam's account, regional, age, licensing, or DRM checks. Store results are
discovery information, not an entitlement signal.

## Metadata intelligence: local by default, cloud only when configured

Metadata intelligence is optional and off by default. The normal 1.0 path is
**On-device AI**: Nexus starts its own Ollama child process on a random IPv4
loopback port with `OLLAMA_NO_CLOUD=1`, uses only a model that is already
downloaded locally, and stops that process when Nexus closes. Nexus does not
install Ollama, pull models, accept a remote Ollama endpoint, enable web access,
or give the model tools. See [On-device AI setup](docs/LOCAL-AI.md).

**Nexus Cloud** is a separate, explicitly selected option. The public build
contains only an OAuth-with-PKCE client for a developer-owned gateway. It does
not include a hosted gateway or identity service, so this option is unavailable
until a maintainer deploys and configures both. Read
[the gateway deployment contract](docs/AI-GATEWAY.md) before enabling it.

The launcher has no OpenAI API-key field, bundled OpenAI credential, or direct
end-user OpenAI OAuth flow. If a Nexus Cloud operator chooses OpenAI for its
server-side model, that operator must keep the application credential on the
server. It is not a desktop-user credential.

For either provider, a request contains only one selected item's title and any
available provider, publisher, version, executable filename, and parent-folder
name. On-device requests stay on loopback. Nexus Cloud requests send that
minimal record to the configured gateway. Neither path sends a full executable
or install path, launch URI/arguments, complete library, files, or binaries.
Suggestions are shown for review and may only fill an empty description or add
tags after approval.

## Integration availability

| Area | v1.0.0 behavior |
| --- | --- |
| Steam local library | Reads local library manifests and launches installed games with a Steam URI when an app ID is available. |
| Steam game discovery | Searches the Steam storefront catalog and, with confirmation, opens a validated official Store page. |
| Windows applications | Reads standard uninstall registry entries and Start Menu shortcuts without administrator rights. |
| Chosen folders | Scans only folders selected in Settings; it does not crawl all drives. |
| WinGet software search | Searches configured WinGet sources and invokes the local WinGet client only after confirmation. |
| On-device metadata intelligence | Optional and off by default. Uses a Nexus-owned, no-cloud Ollama child process and an already-downloaded local text-generation model. Ollama and a compatible local model are user prerequisites. |
| Nexus Cloud metadata intelligence | Optional OAuth-with-PKCE client for an externally configured, developer-owned gateway. No hosted gateway is included. |
| Visual assets | Branded window/taskbar/installer icons, packaged ambient and cover fallbacks, and safe local executable-icon extraction with non-network fallbacks. |
| Mods | Imports a user-selected ZIP archive into a game's local Mods folder after path validation; it is not a hosted mod catalog. |
| Backup | Exports/restores a user-selected local library/settings backup; it is not cloud sync. |
| Epic, GOG, EA, Ubisoft, Battle.net, Xbox, itch.io | Not implemented. |
| Cloud sync, updater, hosted mod catalog | Not implemented. |

## Download and installation

Download release assets from the repository's
[Releases page](https://github.com/st4yfru1tful/nexus-launcher/releases).

- **NexusLauncher-Setup-x64.exe** — Windows installer
- **NexusLauncher-portable-x64.zip** — self-contained portable build
- **SHA256SUMS.txt** — SHA-256 hashes for the distributables

Extract the portable ZIP as a whole and keep **NexusLauncher.portable** next to
**NexusLauncher.exe**. That marker puts the library, settings, cache, and logs
in an adjacent **NexusLauncherData** folder rather than
%LOCALAPPDATA%\NexusLauncher. Do not copy that marker into an installed Nexus
directory.

Verify a download before opening it:

~~~powershell
Get-FileHash .\NexusLauncher-Setup-x64.exe -Algorithm SHA256
~~~

Compare the result with the matching entry in **SHA256SUMS.txt**. Code signing
is not configured by this repository alone, so an unsigned build can trigger a
Windows reputation or publisher prompt.

## Build from source

Prerequisites:

- Windows 10 22H2+ or Windows 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- PowerShell 7 or Windows PowerShell
- Inno Setup 6 only when building an installer

~~~powershell
git clone https://github.com/st4yfru1tful/nexus-launcher.git
Set-Location nexus-launcher
dotnet restore NexusLauncher.sln
dotnet build NexusLauncher.sln --configuration Debug
dotnet test NexusLauncher.sln --configuration Debug
dotnet run --project src/NexusLauncher.App/NexusLauncher.App.csproj
~~~

## Package a release candidate

The packaging script produces a self-contained win-x64 folder publish, portable
ZIP, installer, and SHA-256 checksums.

~~~powershell
.\scripts\Package.ps1 -Version 1.0.0 -RequireInstaller
.\scripts\Verify-Release.ps1 -ArtifactsDirectory .\artifacts
~~~

See [docs/RELEASING.md](docs/RELEASING.md) for the tag-to-release process.

## Project layout

~~~text
src/
  NexusLauncher.App/          WPF UI and composition root
  NexusLauncher.Core/         Domain models and application contracts
  NexusLauncher.Discovery/    Local discovery providers and parsers
tests/
  NexusLauncher.UnitTests/    Fast, deterministic unit tests
installer/                    Inno Setup installer definition
scripts/                      Build, test, package, and verification helpers
~~~

More detail is in [ARCHITECTURE.md](ARCHITECTURE.md).

## Security, privacy, and contributing

Nexus runs as the current user, does not disable Windows protections, and does
not bypass licensing or DRM. Read [SECURITY.md](SECURITY.md) before reporting a
vulnerability and [PRIVACY.md](PRIVACY.md) for precise data handling.

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md), run the
relevant tests, and keep provider additions isolated so one unavailable source
cannot break a scan. The roadmap and limitations are in [ROADMAP.md](ROADMAP.md);
historical shipped behavior is in [CHANGELOG.md](CHANGELOG.md).

## License

Nexus Launcher is released under the [MIT License](LICENSE).
