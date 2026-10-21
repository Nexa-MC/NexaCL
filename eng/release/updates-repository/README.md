# NexaCL updates

Independent distribution repository for **NexaCL Firefly**.

[Releases](https://github.com/Nexa-MC/NexaCL-Update/releases) contain verified publisher-signed
installers, portable packages, manifests and signatures. Source and development history live in
[Nexa-MC/NexaCL](https://github.com/Nexa-MC/NexaCL).

The publication workflow checks the original complete distribution against the public key
stored here and the pinned fingerprint `5701218D69B531E1A7ED35BB6E31F5974A273AEE` before making
an update visible. It needs no signing private key. Published versions are immutable; a failed
upload stays in draft form for retry.

Run **Publish verified updates** with a canonical source tag, or leave it blank to synchronize
the newest public Alpha, Beta and stable releases. The scheduled workflow performs the same
check every 15 minutes. Its default branch is `main`.
