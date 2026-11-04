# Releasing

English | [简体中文](releasing.zh-CN.md)

A release includes source verification, six-platform builds, complete distribution signatures, and independent update-source verification. Success requires a public Release with verified assets, not merely a tag or started workflow.

1. Set the canonical version in `eng/xsr/Xsr.Version.props`, commit source and maintainer-written notes, and run source CI.
2. Create a draft prerelease with nonempty notes for the exact full commit SHA; create/push `v<version>`.
3. Launcher Build verifies six RIDs and builds native modules, JVM Host, helper, installers, and portable packages. If its token cannot read the draft, supply the same notes through `release_notes`.
4. Distribution requires all 18 full packages. It admits up to five compatible trusted historical versions, builds/reconstructs differentials, verifies embedded Sidecar signatures/provenance, signs the full distribution, and verifies it in an independent public-key environment.
5. Upload assets to the existing draft while retaining notes. Drafts are not automatically published. Publish only after complete jobs, asset inventories, and signatures pass.
6. Dispatch Publish verified updates in `Nexa-MC/NexaCL-Update` with the same tag. It verifies source bytes/signatures before publication. Check target metadata/assets against the source.

```sh
gh workflow run launcher-build.yml --ref v2.0.0.alpha.N -F release_notes=@/path/to/RELEASE.md
gh release edit v2.0.0.alpha.N --draft=false --prerelease --latest=false
gh workflow run publish.yml --repo Nexa-MC/NexaCL-Update -f tag=v2.0.0.alpha.N
```

Alpha/Beta remain prereleases. Full packages comprise per-architecture Windows Setup/MSI/ZIP, Linux DEB/RPM/AppImage/TAR, and macOS DMG/TAR. Signed metadata includes `SHA256SUMS`, `Nexa-Release.json`, and `Nexa-Delta.json` when differentials exist.

Publishing a signed draft already containing `SHA256SUMS.asc` skips rebuilding to protect immutable bytes. Retain failure logs and retry failed jobs without moving public tags, replacing assets, or bypassing signatures. Scheduled update synchronization may lag its fifteen-minute interval; manual synchronization still requires actual success.

Source Actions Secrets hold release private keys; the update repository needs public keys only. OpenPGP does not replace Windows Authenticode, Apple Developer ID, or notarization. This guide does not claim those OS credentials are configured.
