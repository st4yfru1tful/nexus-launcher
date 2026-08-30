# Nexus Launcher

![Nexus Launcher mark](assets/nexus-mark.svg)

Nexus Launcher is a local-first Windows launcher for bringing installed games and desktop applications into one searchable library. It is built as a WPF application on .NET 9 and targets 64-bit Windows 10 (22H2 or later) and Windows 11.

> **Release status:** Nexus `v0.1.1` is pre-release software. Treat it as an early release until a stable version is published.

## Why Nexus

Windows software is installed through many sources: launcher manifests, the installed-app registry, Start Menu shortcuts, portable folders, and package managers. Nexus is intended to make the useful parts of that library easier to find and launch without making a cloud account, an AI key, or an online service a prerequisite for basic local use.

The project favors a small set of honest integrations over controls that imply unsupported behavior. A provider that cannot return reliable local information should fail independently and report that state rather than block a scan or invent a result.

## Scope of the first release

`v0.1.0` is scoped to real local functionality:

- A WPF library that persists locally, supports search/category filtering, manual additions, favorites, hiding, opening install folders, and removing an entry without uninstalling it.
- Discovery from Windows installed-app registry entries, Steam’s local manifests, Start Menu shortcuts, and explicitly selected folders. Steam items launch through the Steam URI when an app ID is available.
- Conservative local executable inspection that filters common installers, helpers, updaters, runtimes, and Windows system binaries.
- A WinGet-backed Store page that searches configured WinGet sources and opens Windows Package Manager only after an explicit install confirmation.
- Manual ZIP mod import into a selected game’s `Mods` folder with a path-traversal guard; it is not a hosted mod catalog.
- User-triggered local library/settings backup and restore; it is not automatic cloud synchronization.
- Reproducible Windows CI and a tag-driven release path for an installer, portable archive, and SHA-256 checksums.

This is deliberately not a promise that every commercial launcher, store, cloud service, mod provider, or AI classifier is available in `v0.1.0`. In particular, online metadata, AI classification, remote cloud sync, automatic updates, and third-party mod/store catalogs are not implemented by the initial release. Those integrations need their own implementation, terms review, credentials where required, and user-facing privacy controls.

### Integration availability

| Area | `v0.1.0` behavior |
| --- | --- |
| Steam | Reads local `libraryfolders.vdf` and `appmanifest_*.acf` data, then launches installed games with a Steam URI. |
| Windows applications | Reads standard uninstall registry entries and Start Menu shortcuts without requesting administrator rights. |
| Chosen folders | Scans only folders explicitly selected in Settings; it does not crawl all drives. |
| Store | Searches and starts installation through the user’s installed WinGet client after confirmation. |
| Mods | Imports user-selected ZIP archives into a game’s local `Mods` folder after path validation. |
| Backup | Exports/restores a local ZIP-style library/settings backup chosen by the user. |
| Epic, GOG, EA, Ubisoft, Battle.net, Xbox, itch.io | Not implemented in the initial release. |
| Online metadata, AI, cloud sync, updater, hosted mod catalog | Not implemented in the initial release. |

## Download and installation

Download release assets from the repository's [Releases page](https://github.com/st4yfru1tful/nexus-launcher/releases). Assets are named:

- `NexusLauncher-Setup-x64.exe` — the Windows installer.
- `NexusLauncher-portable-x64.zip` — a self-contained portable build.
- `SHA256SUMS.txt` — SHA-256 hashes for the two distributables.

Extract the portable ZIP as a whole and keep `NexusLauncher.portable` next to
`NexusLauncher.exe`. That marker activates portable mode: library, settings,
cache, and diagnostic files are stored in the adjacent `NexusLauncherData`
folder rather than `%LOCALAPPDATA%\NexusLauncher`. The installer deliberately
does not include the marker, so installed and portable copies never share a
data root. Do not copy the marker into an installed Nexus directory.

Verify a download before opening it:

```powershell
Get-FileHash .\NexusLauncher-Setup-x64.exe -Algorithm SHA256
```

Compare the displayed hash with the matching entry in `SHA256SUMS.txt`. Only run downloads from the project’s official GitHub release.

Code signing is not configured by this repository alone; an unsigned release may cause a Windows reputation or publisher prompt. A release must not claim to be signed unless it was built with a maintainer-controlled signing certificate and the published signature has been verified.

## Build from source

### Prerequisites

- Windows 10 22H2+ or Windows 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- PowerShell 7 or Windows PowerShell for the helper scripts
- Inno Setup 6 only when building an installer

```powershell
git clone https://github.com/st4yfru1tful/nexus-launcher.git
Set-Location nexus-launcher
dotnet restore NexusLauncher.sln
dotnet build NexusLauncher.sln --configuration Debug
dotnet test NexusLauncher.sln --configuration Debug
dotnet run --project src/NexusLauncher.App/NexusLauncher.App.csproj
```

The checked-in project files are the source of truth for the exact SDK, target framework, and package versions.

## Packaging a release candidate

The packaging script publishes a folder-based, self-contained `win-x64` application, adds the portable-only mode marker to the ZIP payload, and—when Inno Setup is installed—compiles the installer and writes checksums. A folder publish is intentional: it is the more reliable deployment shape for a WPF app and keeps both the installer and portable archive straightforward to inspect.

```powershell
.\scripts\Package.ps1 -Version 0.1.0 -RequireInstaller
.\scripts\Verify-Release.ps1 -ArtifactsDirectory .\artifacts
```

Output is written to `artifacts/` and is intentionally excluded from source control. See [docs/RELEASING.md](docs/RELEASING.md) for the tag-to-release process.

## Project layout

```text
src/
  NexusLauncher.App/          WPF UI and composition root
  NexusLauncher.Core/         Domain models and application contracts
  NexusLauncher.Discovery/    Local discovery providers and parsers
tests/
  NexusLauncher.UnitTests/    Fast, deterministic unit tests
installer/                    Inno Setup installer definition
scripts/                      Build, test, package, and verification helpers
.github/workflows/            CI, security analysis, and release automation
```

More detail is in [ARCHITECTURE.md](ARCHITECTURE.md).

## Security and privacy

Nexus operates with the current user’s permissions for normal library scanning, does not disable Windows protections, and does not bypass software licensing or DRM. Read [SECURITY.md](SECURITY.md) before reporting a vulnerability.

Local discovery is intended to remain useful without an account or an AI key. Any future online metadata, AI classification, cloud sync, store, or mod integration must be optional and document exactly what it sends. The current policy is documented in [PRIVACY.md](PRIVACY.md).

## Contributing

Contributions are welcome once the repository is public. Please read [CONTRIBUTING.md](CONTRIBUTING.md), run the relevant tests, and keep provider additions isolated so one unavailable installation cannot break the rest of a scan.

## Roadmap and limitations

The prioritized roadmap is in [ROADMAP.md](ROADMAP.md). The project intentionally does not claim support for an external provider merely because an interface exists for it. Current and historical shipped behavior belongs in [CHANGELOG.md](CHANGELOG.md).

## License

Nexus Launcher is released under the [MIT License](LICENSE).
