# Independent Sidecar release inputs

A launcher source build requires neither the plugin SDK nor a PluginSidecar
checkout. Release packaging consumes already-built `.nsc` images, verifies their
reviewed hashes, adds the publisher's detached signature and verifies the
resulting portable archives. All plugin-specific managed code, native libraries
and UI assets remain inside the PluginSidecar image.

## Produce reviewed inputs

In `Nexa-MC/PluginSidecar`, run `distribution-build.yml` with `expected_sha` set to
the full reviewed commit. The workflow builds all six RIDs on matching runners.
It tests a lone image with no adjacent native/UI dependencies, concurrent native
extraction cleanup, and a real authenticated Host session importing and invoking
a Wasm guest. macOS ad-hoc signing occurs before the artifact hash is recorded.

Only after that run succeeds, run `publish-distribution-inputs.yml` with its run
ID and the same commit. The publication workflow verifies the successful run's
repository, workflow and commit, requires exactly six images, and publishes an
immutable `plugin-inputs-<commit-prefix>` prerelease with `provenance.json`.
These are unsigned inputs, not end-user installable releases.

## Enable a component in the launcher release

Review and commit `eng/release/plugin-sidecar.lock.json` with:

- `repository` and `inputRepository`: `Nexa-MC/PluginSidecar`.
- `commit` and `buildRun`: the complete source commit and successful distribution run.
- `inputRelease`: the immutable input prerelease tag.
- `runtimeVersion`: the actual Worker version, not the launcher version.
- `hostReference`: the full Host commit pinned by the Worker's reference lock.
- `minimumHostVersion`: the first launcher product release providing those generic APIs.
- `artifacts`: all six RIDs, each with the exact `size` and lowercase `sha256` from the verified provenance.

Never use zero hashes, an unverified cross-compilation, a branch name or an old
Host minimum version to fill this file. Until this reviewed lock exists,
PluginSidecar is explicitly omitted; the pipeline reports that omission. This is
not evidence that the plugin-enabled release is complete.

`independent_sidecars.py` fetches each pinned image, signs it, verifies it through
the NativeAOT Host, checks every portable archive and exports a combined update
catalog. Catalogs require the same component set on all RIDs and reject duplicate
identities. Re-exporting one component preserves the others. macOS outer-bundle
sealing temporarily holds all `.nsc` files so their signed bytes cannot change.

## Verification boundary

The local release tests exercise real GPG signatures, archive export, altered
hashes, mismatched runtime sets, duplicate identities and macOS sealing failure
recovery. The native Worker test establishes Linux behavior when run on Linux;
it does not substitute for successful Windows or macOS distribution jobs.
A CI billing or runner failure must be resolved before recording those artifacts
or publishing a plugin-enabled launcher release.
