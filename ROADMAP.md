# Nexus Launcher roadmap

This roadmap describes priorities, not a guarantee that every item will ship on
a particular date. A feature is supported only after it has a tested
implementation, a clear failure state, and accurate documentation.

## 0.1 — local launcher foundation

- [x] Local WPF library with search, category filters, manual additions,
  favorites, hide/remove, launch, and open-folder actions.
- [x] Local Windows, Start Menu, and Steam-manifest discovery with
  source-specific diagnostics and duplicate handling.
- [x] Safe executable or provider-URI launching.
- [x] WinGet package search/install handoff after explicit confirmation.
- [x] Local mod-archive import and local library/settings backup/restore.
- [x] Installer, portable ZIP, checksum verification, and public prerelease.

## 0.2 — trusted discovery and AI foundation

- [x] Steam storefront game search with a validated, explicit official-store
  handoff and no ownership/install claim.
- [x] Store scope switching that keeps Steam catalog results distinct from
  WinGet software results.
- [x] Optional privacy-minimized AI metadata suggestion flow with
  review-before-apply behavior.
- [x] OAuth PKCE client for a developer-owned Nexus AI gateway, encrypted
  per-user session storage, request quota, and safe response validation.
- [x] Documentation of the precise Store/AI boundaries and no direct OpenAI
  OAuth/API-key client claim.

## 1.0 — stable local-launcher baseline

- [x] Packaged Nexus window, taskbar, and installer icons plus branded ambient
  and cover fallback artwork.
- [x] Safe local executable/shortcut icon propagation and extraction with UNC
  rejection, bounded caching, and a visible fallback when no icon is available.
- [x] On-device, review-before-apply metadata suggestions through a dedicated
  no-cloud Ollama child process and already-downloaded local models.
- [x] Explicit provider boundaries: on-device AI never silently falls back to
  Nexus Cloud, and the public build does not imply a hosted gateway.
- [x] Dark/light theme token, empty-state, focus, layout, and visual hierarchy
  hardening for the documented 1.0 screens.
- [x] Stable semantic-version, packaging, checksum, installer, and portable
  release defaults for 1.0.0.

## Next — expand deliberately

- [ ] Deploy a maintainer-controlled Nexus identity service and AI gateway,
  with consent, privacy notice, quotas, rate limits, abuse controls, and
  server-side credential management.
- [ ] Add a bounded metadata cache with expiration, provenance, and user
  controls once a production gateway exists.
- [ ] Add AI-assisted library/search intent only after defining a minimal
  request contract, transparent result provenance, and explicit opt-in.
- [ ] Add model choice/download guidance without making Nexus silently install
  a runtime, fetch a model, or enable a cloud model.
- [ ] Let users review/edit metadata and add custom collections, install-size
  calculation, and durable playtime tracking.
- [ ] Accessibility pass: keyboard navigation, visible focus, screen-reader
  labels, high contrast, text scaling, and reduced motion.
- [ ] Additional launcher providers chosen for reliable local formats and
  permitted access.

## Later

- [ ] Official provider integrations where terms and APIs permit them.
- [ ] Mod-provider integrations with authentication, licensing, archive safety,
  and rate-limit controls.
- [ ] Optional cloud synchronization with conflict handling and deletion
  controls.
- [ ] Update checks with version/checksum verification and explicit approval.

## Non-goals

Nexus will not support piracy, DRM or ownership bypasses, credential
collection, hidden telemetry, global privilege elevation, disabling Windows
security features, or downloading/executing untrusted software automatically.

## How priorities change

Reliability, safety, privacy, and accessibility outrank breadth. A narrow,
well-tested provider is more valuable than a broad unverified badge.
