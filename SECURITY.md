# Security policy

## Supported versions

Security fixes target the latest development work and the most recent
published 1.0.x release.

| Version line | Security support |
| --- | --- |
| Latest development branch | Best effort |
| Latest published 1.0.x | Supported |
| Older versions | Not supported |

## Reporting a vulnerability

Do not include exploit details, secrets, personal paths, or proof-of-concept
binaries in a public issue.

Use GitHub's **Private vulnerability reporting** feature for this repository
when it is available. If it is not enabled, contact the repository owner
through GitHub to arrange a private channel before sending technical details.

Include:

- affected version or commit
- supported Windows version
- concise impact assessment
- minimal safe reproduction steps
- sanitized logs, screenshots, or code references
- whether the issue needs a local file, malformed manifest, network response,
  archive, or elevated operation

Never send credentials, API keys, personal databases, or executable samples
unless a maintainer provides an approved secure transfer path.

## Security expectations

Nexus is expected to follow these rules:

- Normal use runs as the current user and does not request global administrator
  privileges.
- It does not disable or weaken Windows Defender, SmartScreen, UAC, licensing,
  DRM, ownership, account, age, or regional checks.
- It does not read browser passwords, protected Windows app folders, or
  unrelated user files to populate a library.
- It does not automatically execute downloads, remote content, AI output, or
  unvalidated provider data.
- Discovery isolates parse/I/O failures so an untrusted local manifest cannot
  end a complete scan.
- Credentials never appear in logs, settings, backup files, tests, source
  control, release artifacts, or diagnostics.

## Store-provider boundary

The Steam game catalog is an untrusted network input. Nexus accepts only
well-formed app IDs, constructs the official Store URL from that validated
numeric ID, allows HTTPS storefront links only to store.steampowered.com, and
allows image URLs only from HTTPS steamstatic.com hosts. It does not launch a
game, purchase, download, install, authenticate, or infer ownership from a
catalog response.

WinGet installation remains an explicit user-confirmed handoff to the locally
installed Windows Package Manager. A provider-controlled string must never be
interpolated into a shell command.

## On-device metadata intelligence boundary

The on-device provider treats the local model runtime and its output as
untrusted:

- Nexus resolves a local `ollama.exe` from known installation locations and
  starts it directly without shell interpolation.
- The Nexus-owned process binds only to a random IPv4 loopback port and starts
  with `OLLAMA_NO_CLOUD=1` and model keep-alive disabled. Nexus's loopback HTTP
  client does not use a proxy.
- Nexus accepts only already-downloaded models whose local details explicitly
  advertise text-generation capability. It rejects embedding-only and
  cloud-model candidates and never installs Ollama or pulls a model.
- No remote/custom endpoint, web access, or tool execution is exposed to the
  local provider.
- Requests and responses are bounded, JSON output is constrained by a schema
  and validated again locally, and all changes remain review-before-apply.
- Nexus owns and stops the child process. It does not attach to or terminate an
  unrelated Ollama process.

The local provider is not a sandbox for a malicious model or compromised
runtime. Users should install Ollama and models only from sources they trust.

## Nexus Cloud gateway boundary

The desktop app has no OpenAI API key field and never directly calls OpenAI.
Its optional OAuth flow is only for a developer-owned Nexus AI gateway:

- Public HTTPS gateway, authorization, and token URLs plus a public client ID
  are accepted; loopback, query-bearing, credential-bearing, and non-HTTPS
  configuration values are rejected.
- OAuth uses authorization-code flow with PKCE, state validation, and a local
  loopback callback. No desktop client secret is used.
- OAuth sessions are encrypted with Windows DPAPI for the current user and
  stored outside settings, logs, and local backups. Disconnect removes the
  session.
- Gateway requests are HTTPS-only, do not follow redirects, have short
  timeouts and response-size limits, and send only a privacy-minimized metadata
  record.
- Gateway/model output is untrusted. Nexus validates it, presents it for
  review, and never auto-launches, downloads, installs, or silently changes
  launch fields.

A gateway operator must enforce its own authentication, authorization,
rate-limits, abuse controls, retention policy, and server-side credential
storage. The public v1.0.0 build contains no configured production gateway.
On-device AI never silently falls back to Nexus Cloud.

## Local icon and packaged-image boundary

Nexus uses packaged application, ambient, and cover fallback assets so an
unavailable image does not become a missing-texture state. Library icons are
read only from normalized local files. UNC/network paths are rejected, icon
extraction is isolated behind safe failure states, results and misses use a
bounded cache, and a packaged/vector fallback remains visible when extraction
fails. Remote Store images remain untrusted provider data and must use HTTPS,
allowed hosts, response limits, and a local fallback.

## Provider and plugin expectations

Every external provider is a security boundary. Contributors must:

- Use permitted APIs or documented local formats.
- Validate untrusted paths, URIs, archives, manifests, and JSON.
- Apply timeouts, cancellation, response-size limits, and safe error handling.
- Avoid shell interpolation and never execute a remote destination merely
  because a response provides it.
- Ask for user action before downloads, installs, updates, elevation, or
  sign-in.
- Update PRIVACY.md with the provider's data handling.

Archive extraction must defend against path traversal, symbolic-link surprises,
and resource exhaustion. Update handling must verify a user-visible version and
checksum before asking to run an installer.

## Disclosure process

Maintainers should acknowledge a private report promptly, assess affected
versions, prepare a fix and regression test, and coordinate disclosure with the
reporter where possible. The final advisory should give mitigation and upgrade
guidance without publishing harmful exploit details.
