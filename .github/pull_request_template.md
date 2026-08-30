## Summary

Describe the user-visible change and why it belongs in Nexus.

## Validation

- [ ] `dotnet build NexusLauncher.sln --configuration Release`
- [ ] Relevant tests added or updated
- [ ] `dotnet test NexusLauncher.sln --configuration Release`
- [ ] Manual WPF verification completed where UI behavior changed

## Safety and documentation

- [ ] No credentials, personal paths, local databases, logs, or build artifacts are included.
- [ ] This change does not bypass licensing, DRM, authentication, or Windows security protections.
- [ ] I updated documentation, privacy, security, or the changelog where behavior requires it.
- [ ] For a provider/network/download/archive/credential change, I documented the threat model and failure states below.

## Notes for reviewers

Explain provider/API terms, data sent off-device, launch/download behavior, or migration implications here when applicable.
