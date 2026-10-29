# Shared XSR packages (first slice)

Status: first local-feed implementation validated. Product APIs and versioning remain unchanged.

The first independently consumable closure is Core, Contracts, Sidecar.Protocol,
Sidecar.Transport, Xsr.Abstractions, Diagnostics, State and Runtime. Shared packages
use a separate prerelease version in eng/packages/manifest.json. Renderer and
Platform.Runtime are outside this first release.

Each runtime package contains its assembly, license, README, matching .nfp catalog
and a buildTransitive target copying that catalog into build/publish output.
A separate Nexa.Xsr.BuildTools package contains the managed patch compiler and its
private dependencies. It must never add Roslyn/Mono.Cecil to product runtime assets.
Consumers explicitly reference BuildTools with PrivateAssets=all and enable
NexaAutomaticFunctionPatches=true to instrument their own assemblies. Package
catalogs remain available even when consumer instrumentation is not enabled.

The repository and package consumers share the instrumentation target implementation.
A package supplies a compiler path; repository builds resolve the existing compiler
project. No consumer needs a path into the NexaCL checkout. Native AOT still runs
the build compiler on the build machine as a managed tool.

Build output is a local feed plus a manifest of source commit, package dependencies
and SHA-256 hashes. Building does not publish to a public registry. Acceptance uses
an isolated package-only consumer, route/state checks, install/retire of an automatic
patch, catalog presence and absence of build compiler assemblies in product output.
Linux x64 AOT must execute those same paths. Windows CI runs the managed consumer.

## Usage and validation

Requires .NET SDK 10.0.403 and Python 3. Build dependencies are locked. From this
checkout run:

```sh
python eng/packages/build.py --output /absolute/path/to/feed
python eng/packages/verify.py --feed /absolute/path/to/feed
# Linux x64 with native compiler/linker prerequisites:
python eng/packages/verify.py --feed /absolute/path/to/feed --aot
```

Use the local feed in NuGet.Config and exact prerelease PackageReference versions.
`feed-manifest.json` records the source SHA and every nupkg SHA-256. Source lock +
package lock are required together: don't rebuild changed source under an existing
published version. Each published content change requires a new shared package version.
The default product version (NexaCL alpha/beta etc.) is independent of this version.

Nine packages now pass fresh-cache restore and content checks. The consumer tests
route acceptance separately from completion, state availability, consumer-side
automatic instrumentation, patch install/retire, incremental rebuild and catalog
copy to managed/AOT publish output. The build compiler's assemblies remain out of
product output. Licensing includes the repository license and bundled compiler
third-party licenses/notices. This is a local feed implementation, not a registry
publication or signed release. Runtime's existing 183 tests pass separately.

NexaSM's default path uses only local routing. No Sidecar startup or SidecarReady
barrier is required. NexaSM does not reference BuildTools or instrument its host;
optional extension consumers opt in explicitly. Existing Runtime assemblies retain
their established instrumentation semantics. There is no claim that simply loading
the package starts Sidecar processes or enables remote plugins.

## Verification note (2026-10-29)

The shared target change preserves the repository path: all 183 Runtime executable
tests passed, with no skips. The package-only managed/AOT probe passed patch
install/retire and catalog/output checks using a fresh NuGet cache.

The full architecture executable reports 84 existing Product NXML closure wording
violations in untouched Desktop sources. Running with the original HEAD target
restored produced byte-identical diagnostics; this change does not repair or waive
that unrelated baseline. The shared-package CI is separate, not a claim that every
NexaCL pipeline is green. An initial parallel architecture run encountered an
assembly being written by the runtime build; the recorded comparison was rerun
sequentially after builds completed.

Shared packages omit debug symbols and SourceLink (no PDBs are shipped); builds disable both so checkout paths and temporary instrumentation symbol paths cannot change package bytes. Source provenance remains pinned in package repository metadata.
