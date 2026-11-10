# Unified update center migration

Launcher and Sidecar updates now share a list and global check/install/restart
controls. Each row retains its own version, progress, retry or restart status.
The queue does not bypass either update service's signature and activation rules.
Incompatible Sidecars remain blocked until the required Host version is running.
Startup automatic checks now include Sidecars; changing channels cannot install
previous-channel offers. Download failures do not abort unrelated queued items.

Market proactively registers its display metadata through the existing Resource
registration, without a new wire message, CLR crossing or JSON data plane. Hosts
bind declarations to authenticated package identity and retire them with sessions.
Both managed and native service modes use the same projection and intent handling.

Validation covers binary framing/hash/metadata rejection and retirement, shared
queue ordering and failure continuation, channel changes, duplicate input and
restart blocking, existing update recovery/rollback tests, and light/dark layouts
at 800 and 1040 pixels. Repository architecture checks remain mandatory.

## Verification evidence

- Desktop composition suite: 397 passed; update-focused subset: 14 passed.
- XSR runtime suite: 185 passed, including hash rejection, metadata bounds,
  forward-compatible TLVs and declaration retirement.
- Market behavior suite: 27 passed; architecture gate: 3 projects and 16 cases.
- Market signed packaging/tamper tests: 2 passed.
- Host layout/dependency gate: 101 projects verified.
- Managed Desktop build and native-service/NativeAOT-analyzed module build:
  zero warnings/errors. Native validation uses `NexaNativeServices=true`,
  `NexaExternalModules=true`, `NexaNativeModule=true`,
  `NexaNetworkTestInternals=false`, and `PublishAot=true` with restore enabled.
- Update/onboarding preview fixtures inspected at 800/1040 px in light/dark mode,
  including available, downloading, failed and restart states. Preview versions
  are test data, not a newly published launcher release.

Market source: `3ab79e3a7d5a64b8326bb96ffeced73faa7d62d0`.
[Six-platform NativeAOT distribution build](https://github.com/Nexa-MC/Nexa-Cloud-Market-Sidecar/actions/runs/42818518506)
and [Host input staging](https://github.com/Nexa-MC/NexaCL/actions/runs/42819737296)
succeeded. Published binary sizes and SHA-256 digests match
`eng/release/market-sidecar.lock.json`; the release pipeline will apply the pinned
publisher signature when packaging the launcher.
