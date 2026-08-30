# Contributing to Nexus Launcher

Thanks for helping improve Nexus. The goal is a reliable local-first Windows launcher, not a collection of superficial integrations. Please keep changes small, testable, and honest about what they enable.

## Development setup

You need Windows 10 22H2+ or Windows 11 and the .NET 9 SDK. Visual Studio 2022 with the **.NET desktop development** workload is helpful for WPF work, but the command line is the supported baseline.

```powershell
git clone https://github.com/st4yfru1tful/nexus-launcher.git
Set-Location nexus-launcher
dotnet restore NexusLauncher.sln
dotnet build NexusLauncher.sln --configuration Debug
dotnet test NexusLauncher.sln --configuration Debug
```

Run the app with:

```powershell
dotnet run --project src/NexusLauncher.App/NexusLauncher.App.csproj
```

Use the repository `.editorconfig`. Nullable references and .NET analyzers are enabled through `Directory.Build.props`; do not suppress warnings without explaining why the warning is a false positive or an accepted boundary.

## Before opening a pull request

- Keep the change focused and explain the user-visible effect.
- Run `dotnet build NexusLauncher.sln --configuration Release` and the relevant tests.
- Add or update tests for parser, identity, classification, and error-handling changes.
- Do not commit `bin/`, `obj/`, local databases, logs, installer output, or credentials.
- Update `CHANGELOG.md`, `README.md`, architecture notes, privacy, or security documentation whenever behavior changes them.
- Check keyboard behavior, visible focus, text scaling, and loading/error states for UI changes.

The CI workflow runs the same restore, build, and test steps on Windows. A pull request should not rely on a game launcher, a developer-specific install path, an external account, or a network response to pass.

## Architecture rules

- `NexusLauncher.Core` has no WPF or provider-specific dependency.
- Discovery/parsing code belongs in `NexusLauncher.Discovery`, not code-behind or a view model.
- The App project composes services and presents state; it should not contain source-specific scan logic.
- Inject dependencies through constructors. Avoid mutable static state and service locators.
- Treat manifest content, shortcut targets, registry values, file names, archive entries, and online provider responses as untrusted input.
- Use asynchronous APIs and `CancellationToken` for scan or network work. Never block the UI thread on I/O.

## Adding a discovery provider

1. Read the source’s local format and terms. Do not scrape proprietary UI or bypass access controls.
2. Add a provider in `NexusLauncher.Discovery` implementing the Core discovery contract.
3. Use the minimum scoped source locations. Do not recursively scan all drives by default.
4. Normalize paths and emit source-specific diagnostics instead of throwing a provider failure through the entire scan.
5. Supply stable provider IDs where available and preserve evidence needed for safe duplicate resolution.
6. Add fixtures and tests for valid input, malformed input, missing installs, cancellation, and duplicates.
7. Register it at the App composition root and add an accurate UI state for unavailable/not-installed cases.

## Adding an online provider

Metadata, store, mod, AI, and cloud providers require extra care:

- Make them opt-in where they send data or require credentials.
- Document what data leaves the device in `PRIVACY.md` before merging.
- Use official APIs or explicitly permitted integrations; honor rate limits and licensing.
- Never send executable binaries, arbitrary user files, credentials, or unrelated paths to an AI or metadata API.
- Do not infer price, ownership, installation state, or mod compatibility from unreliable data.
- Add timeout, cancellation, error, disabled, and offline paths to the UI.

## Tests

Prefer small, deterministic tests over a test count target. Good test subjects include:

- Steam VDF and app-manifest parsing
- Windows-path normalization and comparison
- executable classification and exclusion rules
- duplicate identity resolution
- launch-target validation
- settings serialization and migrations
- archive path traversal prevention for any future mod installer
- semantic version comparison and checksum verification for updater/release code

Do not use a live `Program Files`, Steam library, registry installation, or network API as a unit-test fixture.

## Pull requests and reviews

Use a clear title, link the issue when one exists, and state the manual verification you performed. Reviewers may request a narrower change when a pull request mixes UI redesign, provider implementation, and release configuration.

Security-sensitive changes (credentials, launch behavior, archive extraction, downloads, updates, cloud sync, and provider authentication) require explicit threat-model reasoning in the pull request. See [SECURITY.md](SECURITY.md).

## Releases

Maintainers should follow [docs/RELEASING.md](docs/RELEASING.md). Do not attach a source-only or unverified artifact to a release.
