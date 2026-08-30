# Nexus Launcher

![Nexus Launcher mark](assets/nexus-mark.svg)

Nexus Launcher is a local-first Windows launcher for bringing installed games
and desktop applications into one searchable library. It is a WPF application
on .NET 9 for 64-bit Windows 10 (22H2 or later) and Windows 11.

> **Release status:** Nexus v0.2.0 is a prerelease. It is a usable release
> build, but it is not presented as a finished 1.0 product.

## What Nexus does now

- Builds a local library from Steam manifests, installed-app registry entries,
  Start Menu shortcuts, and folders you explicitly select.
- Searches, filters, launches, favorites, hides, removes, and opens folders
  for local entries without uninstalling anything.
- Searches Steam's public storefront catalog for games, showing catalog
  listings and opening the official Steam page only after you choose it.
- Searches WinGet for Windows software and starts WinGet only after an
  explicit install confirmation.
- Offers review-before-apply AI metadata suggestions for library entries when
  the optional Nexus AI gateway has been deployed and configured.
- Imports user-selected ZIP mods safely into a selected game's Mods folder,
  and makes local library/settings backups.

Nexus never assumes game ownership, purchases, downloads, installs, or bypasses
Steam's account, regional, age, licensing, or DRM checks. Store results are
discovery information, not an entitlement signal.

## AI metadata: an honest boundary

The desktop app does not ask for, store, or call OpenAI with an API key. OpenAI
documents application credentials such as API keys or workload identity
federation, not an end-user OpenAI sign-in flow for a desktop launcher. Nexus
therefore uses OAuth with a **developer-owned Nexus AI gateway**, never a
pretend OpenAI OAuth button.

The public build ships the safe client path, but no production Nexus AI gateway
is bundled or configured. Until one is deployed, local launcher features work
normally and AI controls remain unavailable. A real gateway must authenticate
its own users, enforce quotas/rate limits, and keep any model-provider
credentials on the server. Read [docs/AI-GATEWAY.md](docs/AI-GATEWAY.md) before
enabling it.

When enabled, an AI metadata request sends only a title and any available
provider, publisher, version, executable filename, and parent-folder name.
It never sends a full executable/install path, launch URI/arguments, a complete
library, files, or binaries. Suggestions are shown for review and may only
fill an empty description or add tags after the user approves them.

## Integration availability

| Area | v0.2.0 behavior |
| --- | --- |
| Steam local library | Reads local library manifests and launches installed games with a Steam URI when an app ID is available. |
| Steam game discovery | Searches the Steam storefront catalog and, with confirmation, opens a validated official Store page. |
| Windows applications | Reads standard uninstall registry entries and Start Menu shortcuts without administrator rights. |
| Chosen folders | Scans only folders selected in Settings; it does not crawl all drives. |
| WinGet software search | Searches configured WinGet sources and invokes the local WinGet client only after confirmation. |
| AI metadata | Optional, off by default, privacy-minimized, gateway-OAuth client with review-before-apply suggestions. A deployed Nexus gateway is required. |
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
.\scripts\Package.ps1 -Version 0.2.0 -RequireInstaller
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
