# Releasing Nexus Launcher

This checklist is for maintainers. A GitHub release must contain real, launchable artifacts—not source-only placeholders.

## Before tagging

1. Choose a semantic version. Use `v0.1.0` for the first prerelease unless the shipped product has earned a stable `1.0.0` designation.
2. Update `CHANGELOG.md` with only behavior that was implemented and verified.
3. Confirm that `README.md`, `PRIVACY.md`, and `SECURITY.md` reflect the release.
4. Run a clean local validation if practical:

   ```powershell
   .\scripts\Test.ps1 -Configuration Release
   .\scripts\Package.ps1 -Configuration Release -Version 0.1.0 -RequireInstaller
   .\scripts\Verify-Release.ps1 -ArtifactsDirectory .\artifacts
   ```

5. Check `git status` and inspect staged changes for credentials, local databases, logs, user paths, and build output.
6. Test the installer and portable archive on a clean Windows environment when possible. Confirm that the application starts and that uninstalling does not silently delete user data.
7. If code signing is configured outside this repository, verify the signature on the final installer. Do not imply a build is signed when it is not.

## Publish

The `Release` workflow runs for a pushed tag matching `v*`.

```powershell
git tag -a v0.1.0 -m "Nexus Launcher v0.1.0"
git push origin v0.1.0
```

The workflow uses the repository `GITHUB_TOKEN` to:

1. restore, test, and self-contained-publish the WPF app for `win-x64`;
2. install Inno Setup and build `NexusLauncher-Setup-x64.exe`;
3. create `NexusLauncher-portable-x64.zip`;
4. write `SHA256SUMS.txt`;
5. verify file names, hashes, and portable archive contents; and
6. create or update the matching GitHub Release with those assets.

Do not create a GitHub Release manually before artifacts are verified. Published assets are intentionally immutable: the workflow refuses to overwrite an existing asset. If a distributed binary must change, create a new version tag and release instead of replacing the existing one.

## Installer contract

`scripts/Package.ps1` calls Inno Setup with these preprocessor values:

- `MyAppVersion` — the release version without the `v` prefix.
- `SourceDir` — the self-contained published app directory.

The installer script must accept those values (with local-development defaults), install the published files, preserve user data on upgrades, and emit `NexusLauncher-Setup-x64.exe` through the passed Inno output directory. The release workflow installs Inno Setup 6 on the GitHub-hosted Windows runner.

## After publishing

- Download both assets from the published release and verify their hashes against `SHA256SUMS.txt`.
- Confirm the release title, generated notes, version tag, and asset names are correct.
- Verify the repository contains no accidentally committed secrets or installer payloads.
- Only then announce the release.
