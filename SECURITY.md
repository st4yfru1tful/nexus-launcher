# Security policy

## Supported versions

Nexus is pre-release software. Security fixes are made against the latest unreleased work and the most recent published `0.1.x` release, if one exists. Older prerelease builds may not receive patches.

| Version line | Security support |
| --- | --- |
| Latest development branch | Best effort |
| Latest published `0.1.x` | Best effort |
| Older versions | Not supported |

## Reporting a vulnerability

Do not include exploit details, secrets, personal file paths, or proof-of-concept binaries in a public issue.

Use GitHub’s **Private vulnerability reporting** feature for this repository when it is enabled. Maintainers should enable that feature before the first public release. If it is not available, contact the repository owner through GitHub to establish a private channel before sharing technical details.

Include enough information to reproduce safely:

- affected version or commit
- supported Windows version
- concise impact assessment
- minimal reproduction steps
- relevant sanitized logs, screenshots, or code references
- whether the issue requires a local file, malformed manifest, network response, archive, or elevated operation

Do not send credentials, API keys, personal databases, or executable samples unless a maintainer explicitly provides a secure transfer channel.

## Security expectations

Nexus is expected to follow these rules:

- Normal use runs as the current user and does not request global administrator privileges.
- It does not disable or weaken Windows Defender, SmartScreen, UAC, licensing, DRM, or authentication checks.
- It does not read browser passwords, protected Windows application folders, or unrelated user files to populate a library.
- It does not automatically execute downloads, remote content, AI output, or unvalidated provider data.
- Discovery providers isolate parse and I/O failures so an untrusted local manifest cannot terminate a complete scan.
- Optional credentials are not written to logs, settings files, test fixtures, or source control.

## Provider and plugin expectations

Every external provider is a security boundary. A contributor adding one must:

- Use documented and permitted APIs or local formats.
- Validate all untrusted input, including paths, URIs, archives, manifests, and JSON.
- Apply timeouts, cancellation, size limits, and safe error handling to network/file operations.
- Avoid command-shell interpolation of provider-controlled text.
- Avoid executing a path or installer merely because a remote response says to do so.
- Ask for explicit user action before downloads, installs, updates, elevation, or account sign-in.
- Describe the provider’s data handling in [PRIVACY.md](PRIVACY.md).

Archive extraction must defend against path traversal, symbolic-link surprises, and resource exhaustion. Update handling must verify the user-visible version and checksum before requesting execution of an installer.

## Credential handling

For development, use environment variables or local secret storage outside the repository. For released functionality, use a supported OS-protected secret mechanism where appropriate. Never commit `.env` files, access tokens, client secrets, certificates, API keys, or passwords. Rotate a secret immediately if it was exposed in a commit, CI log, issue, or release artifact.

## Disclosure process

Maintainers should acknowledge a private report promptly, assess impact and affected versions, prepare a fix and regression test, and coordinate disclosure with the reporter where possible. The final security advisory should explain affected versions, mitigation, and upgrade guidance without publishing harmful details unnecessarily.
