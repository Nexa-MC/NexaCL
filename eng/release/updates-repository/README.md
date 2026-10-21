# NexaCL updates

Independent distribution repository for **NexaCL Firefly**.

[Releases](https://github.com/Nexa-MC/NexaCL-Update/releases) contain verified publisher-signed
installers, portable packages, manifests and signatures. Source and development history live in
[Nexa-MC/NexaCL](https://github.com/Nexa-MC/NexaCL).

The publication workflow checks the original complete distribution against the public key
stored here and the pinned fingerprint `5701218D69B531E1A7ED35BB6E31F5974A273AEE` before making
an update visible. It needs no signing private key. Published package bytes, manifests and
signatures are immutable; a failed initial upload stays in draft form for retry.

Differential updates use `Nexa-Delta.json`, its detached signature and signed per-platform
bundles. The client selects a smaller bundle for its installed source version and retains
an independently authenticated full-package fallback. Historical baselines are discovered
across the pinned update, source and archived repositories.

A full-only public release may receive a signed differential extension later. The publisher
verifies the original distribution and appends only missing differential files, preserving
all existing asset hashes and signatures. Bundles are uploaded first and the signed index
last; interrupted uploads can resume without replacing published bytes. The original
`SHA256SUMS` remains unchanged during backfill; the new files are authenticated by their
detached signatures and signed index. Only the source repository holds the signing key.

Run **Publish verified updates** with a canonical source tag, or leave it blank to synchronize
the newest public Alpha, Beta and stable releases. The scheduled workflow performs the same
check every 15 minutes. Its default branch is `main`.
