# XSR-755 protected differential updates

## Contract

The XSR-754 helper may reconstruct a fresh immutable payload slot from a publisher-signed
differential bundle. The full eighteen-asset release envelope remains schema v1, preserving
compatibility with existing helpers. An optional `Nexa-Delta.json` and detached signature
bind each bundle to the exact target full-package digest, target version, RID, build variant,
configuration, and installed source version. No URL or local path is accepted from the caller.
The bundle is selected only when smaller than the authenticated full archive.

`nexa-file-delta-v1` bundles contain exactly `manifest.json` and `data`. The manifest lists
the complete target tree, executable bits, lengths and SHA-256 digests. Ordered chunks copy
bytes from the same relative file in the admitted installed slot or from the literal stream.
Content-defined chunking makes unchanged ranges reusable even after insertions. Deleted files
are absent from the fresh slot; the old installation and unknown files are never mutated.
All source and destination traversal retains admitted directory handles/descriptors.

The generator uses the existing `UpdateChunker` gear table and frozen V2 bounds/masks
(128 KiB minimum, 512 KiB average, 1 MiB maximum). Whole unchanged files bypass chunking;
the runtime applies signed byte ranges and needs neither native diff executables nor IPC.

The signed bundle hash and exact download length are checked before parsing. Actual manifest,
literal, per-file and aggregate output budgets are enforced while streaming. Paths, duplicate
names, special archive entries, chunk ranges, identity, final hashes and required executables
are validated before durable preparation. A failed optional delta cannot authorize activation;
the helper discards its fresh slot and attempts the separately authenticated full archive.
Cancellation does not trigger fallback. Neither signature failure nor source mismatch changes
the high-water journal. Recovery re-verifies cached release/index signatures and bundle bytes,
then reconstructs from the unchanged installed slot, including offline after durable reception.

## Publishing

The release workflow retrieves recent public same-channel releases from the fixed repository,
verifies their envelopes with the pinned publisher key and checks downloaded archives against
signed sizes/hashes. Generation processes uncompressed file content, not compressed TAR bytes.
Every published bundle is round-trip checked against the complete target archive tree and
omitted if it is not smaller. Index and bundles join distribution checksums, detached signing
and independent signature verification. Missing historical releases keep full-only publishing;
malformed or unauthenticated historical evidence fails generation rather than being trusted.
CI commit labels remain manual-only. No plugin, UI or user staging path gains update authority.

## Independent update repository and immutable backfill

History discovery queries only the pinned update, source and archived repositories,
merges duplicate canonical versions with update/source/history priority, and uses a
single five-record published history window across them. Signed historical envelopes
and exact portable bytes remain prerequisites. Missing optional history is allowed;
malformed metadata, signature failure and non-404 network failures remain errors.

An already published full-only release may gain one immutable differential extension:
`Nexa-Delta.json`, its signature, and the referenced bundles/signatures. Existing full
packages, release envelope, checksum file and all existing signatures are never replaced.
The original checksum list therefore retains its original coverage; each supplemental
bundle is authenticated independently by its detached signature and the signed delta
index, which binds the exact target full-package hash. Future normal releases continue
to include deltas in their complete checksum set.

Backfill uses the source repository's existing signing Secrets and never rebuilds or
executes historical packages. Bundles are round-trip checked before signing. Publication
uploads bundles and signatures first, then the index signature, and the index last so
clients cannot discover a partial extension. Retrying reuses identical existing bytes;
conflicting published assets fail without overwrite. The independent mirror verifies
the complete signed distribution before appending only missing differential assets to
an existing public release, preserving existing names, sizes and SHA256 digests. A
published index is immutable. Clients retain their existing authenticated full fallback.

## Validation

Contract tests cover reduced transfers, initial installer and active-slot sources, wrong
signatures, source mismatch, truncated/corrupt bundles, malformed paths/ranges, actual byte
budgets, missing executables, full fallback, cancellation, offline recovery and rollback.
Python tests cover chunk reuse after shifts, unchanged/deleted/new files, archive traversal,
signed identity and reproducible complete-tree reconstruction. Native CI exercises differential
transactions through real admitted filesystem ports on all six RIDs; AOT/trim checks remain
mandatory. Physical power-loss and same-account replacement-race acceptance remains with Dots.


## Independent-source publication evidence (2026-10-21)

- Publisher tooling commit: `2698b76005cf54541e6d01909b4e845c2288db16` on `main`.
- All 80 release tooling tests and the 72-project architecture suite passed locally.
- [Signed Alpha 8 backfill](https://github.com/Nexa-MC/NexaCL/actions/runs/39560086426) passed against the existing published Alpha 8 packages, without rebuilding them.
- [Independent mirror publication](https://github.com/Nexa-MC/NexaCL-Update/actions/runs/39561201887) verified the complete signed distribution and appended the extension successfully.
- Alpha 6 → Alpha 8 and Alpha 7 → Alpha 8 each have six differential bundles. Compared with the corresponding full portable archive, transfers are 14.6–25.7% and 17.2–28.4% smaller, respectively.
- Both public releases contain the same 66 asset identities. All original 40 files retain their exact sizes and SHA256 digests, including the original manifests, checksum file and detached signatures.
- The published `Nexa-Delta.json` signature was independently verified again with the unchanged pinned public key; all twelve target archive hashes match the original Alpha 8 release.
- Existing helpers keep their signature, source-version and full-fallback behavior. Historical builds pinned to old discovery repositories still need the documented initial manual repository migration.
- [Organization profile](https://github.com/Nexa-MC) now points to NexaCL source/feedback, NexaCL-Update releases and the website, with the old PCL-N/Patches roles described as historical.
