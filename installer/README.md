# Installer notes

`NexusLauncher.iss` is the Inno Setup 6 definition for the Windows installer. It is kept separate from the application project so a release build can publish a self-contained payload and hand it to Inno Setup without mixing installer-only logic into WPF.

The release package script calls Inno Setup with:

```text
/DMyAppVersion=<semantic-version>
/DSourceDir=<absolute-publish-directory>
/O<absolute-artifacts-installer-directory>
/FNexusLauncher-Setup-x64
```

The definition should provide sensible local-development defaults for `MyAppVersion` and `SourceDir`, but it must honor the supplied values in CI. It should install only the contents of `SourceDir`, provide normal Start Menu/uninstall entries, support upgrades, and avoid deleting user library data on an ordinary upgrade or uninstall without an explicit user choice.

Build locally with:

```powershell
.\scripts\Package.ps1 -Version 1.0.0 -RequireInstaller
```

Do not commit compiled setup executables or the `installer/output/` directory.
