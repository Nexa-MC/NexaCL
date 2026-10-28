# Bundled official Market Sidecar

Alpha 16 bundles the independently versioned Market Sidecar 1.0.0. Its private
source stays in Nexa-MC/Nexa-Cloud-Market-Sidecar and never enters the Host build
graph. That repository's immutable CI run produces six NativeAOT `.nsc` files.
A dedicated prerelease distributes officially signed binary inputs only. `market-sidecar.lock.json`
pins the source commit, Host contract reference and each input's size/SHA-256.
No source archive or credential is copied to the public repository.

Each launcher packaging job verifies the pinned input before using the existing
publisher signing secrets. It creates `.nsc.asc` with the pinned official release
key, verifies the signature in a separate public-only keyring, and places both
files in the launcher executable directory. No key is compiled into Sidecar;
only public verification material belongs in the Host. The runtime discovers
and validates the `.nsc` with its existing production verifier and authenticated
process bootstrap. No unsigned/development admission exception is introduced.

macOS Sidecar builds explicitly apply and verify their native ad-hoc code signature
before hashes are pinned and publisher signatures are created. NativeAOT does not
produce this signature consistently across architectures; packaging must never
rewrite an already publisher-signed binary.

The six final portable packages are independently reopened and checked against
the lock and publisher signature before release signing/publication. Native
installers use the same staged payload. macOS bundle signing preserves the
Sidecar's already signed bytes and reseals only the outer bundle after restoring
the Sidecar. Update manifests and differential packages include both `.nsc` and
`.nsc.asc`, preserving signature verification after updates.
