# Nexus Launcher roadmap

This roadmap describes priorities, not a guarantee that every item will ship on a particular date. A feature is only considered supported after it has a tested implementation, an understandable failure state, and accurate documentation.

## 0.1 — local launcher foundation

- [ ] Reliable WPF library experience with loading, empty, and error states.
- [ ] Local Windows application discovery with source-specific diagnostics.
- [ ] Steam local manifest discovery with multi-library support and malformed-file handling.
- [ ] Safe executable or provider-URI launching.
- [ ] Duplicate resolution and user-visible provider status.
- [ ] Deterministic parser, normalization, and launch tests.
- [ ] Installer, portable ZIP, checksum verification, and a public prerelease once validated.

## Next — catalog quality

- [ ] Editable metadata, tags, custom collections, install-size calculation, and durable playtime tracking beyond the current local library fields.
- [ ] Additional launcher providers selected for reliable local formats and permitted access.
- [ ] Better application classification that excludes installers, helpers, and system components by default.
- [ ] Accessibility pass: keyboard navigation, visible focus, screen-reader labels, high contrast, text scaling, and reduced motion.
- [ ] Diagnostics with secret redaction and actionable scan status.

## Later — optional connected features

- [ ] Opt-in metadata enrichment with transparent matching confidence and caching.
- [ ] Official store-page/package-manager handoff where terms and APIs allow it.
- [ ] Mod-provider integrations that honor authentication, licensing, archive safety, and rate limits.
- [ ] Optional AI identification using minimal metadata only, a user-supplied key, request controls, and a local cache.
- [ ] Optional cloud synchronization with clear ownership, conflict handling, and deletion controls.
- [ ] Release update checks with version/checksum verification and explicit user approval.

## Non-goals

Nexus will not support piracy, DRM or ownership bypasses, credential collection, hidden telemetry, global privilege elevation, disabling Windows security features, or downloading/executing untrusted software automatically.

## How priorities change

Reliability, safety, privacy, and accessibility outrank breadth. A narrow, well-tested local integration is more valuable than an unverified provider badge. Please use GitHub issues and discussions once the repository is public to suggest priorities.
