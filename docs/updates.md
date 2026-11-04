# Updates and transactions

`src/modules/updates/` owns update contracts, discovery, transactions, native clients, and codecs. `nexa-updates` owns execution; `Nexa.Update.Helper` is an independent protected installer. UI projects state and submits intents.

Publisher identity is pinned to `5701218D69B531E1A7ED35BB6E31F5974A273AEE`; the public key is `GPG-PUBLIC-KEY.asc`. Signed manifests bind product, channel, version, runtime, configuration, filenames, sizes, and SHA-256. Source admission, downgrade protection, and actual content verification precede installation.

Signed `Nexa-Delta.json` selects a platform differential matching the installed source. Target bytes and full-package digests are independently constrained. Missing, mismatched, or unreconstructable differentials fall back to an independently authenticated full package. Release tools verify reconstruction, not merely archive readability.

Portable updates and protected installations have separate permission paths. The installed protected helper verifies its location, publisher, and operation objects. It does not elevate an arbitrary downloaded helper. Transactions use a restricted object namespace, versioned slots, journals, recovery, activation, and rollback. Interruptions, replacement races, offline recovery, and failed restarts require platform evidence.

Sidecar updates have independent candidates, signature admission, activation, and state. Update-source synchronization verifies actual source assets before publishing a complete target release; interrupted candidates remain drafts. Public assets cannot be overwritten with rebuilt bytes of the same version.

Source-directory migration does not change user configuration, install layouts, distributed filenames, or update formats.
