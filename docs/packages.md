# Shared packages

`eng/packages/manifest.json` defines shared packages independently of product Alpha/Beta versions. The closure includes Core, Contracts, Sidecar.Protocol, Sidecar.Transport, Sidecar.Host.Contracts, XSR Abstractions, Diagnostics, State, Runtime, the framework-neutral Renderer and NXML Compiler/Runtime, and separate BuildTools. Desktop, Avalonia, native platform implementations and Host services are outside this closure.

Version `0.1.0-alpha.6` adds negotiated, document-local form submissions and the canonical NXML compiler packages. Referencing `Nexa.Xsr.Renderer.Nxml.Runtime` brings the compiler's standard `Ui/Components/*.c.nxml` and `Ui/Styles/*.nss` files into both build and publish output. Keep these files with your executable: default component/style resolution uses `AppContext.BaseDirectory`, not the working directory. Shared NXML compilation is available without a window backend or launcher installation.

Version `0.1.0-alpha.7` fixes the clean-build failure in
the Alpha 6 package pipeline: instrumentation no longer emits a synthetic PDB
for an assembly compiled without debug symbols. The UI form and Host catalogs
retain their existing wire layouts and fingerprints.

Version `0.1.0-alpha.8` prevents a lock-order inversion between presentation
replacement and UI command inspection. A presentation lease captures the session's
immutable negotiated features and registration before entering the presentation
lock. This changes no wire layouts or guest SDK contracts.

Version `0.1.0-alpha.5` adds `SidecarViewStateClient` for binary NXML bundles and document-local model updates. The base catalog (`0.1.0-alpha.2`), effects catalog (`0.1.0-alpha.3`) and presentation catalog (`0.1.0-alpha.4`) remain unchanged.

Version `0.1.0-alpha.2` introduced `SidecarHostRequestClient` and `Nexa.Sidecar.Host.Contracts`. These are general Sidecar APIs, independent of the plugin SDK. The client contains its own DTOs and generated binary codecs; it does not reference launcher service implementation assemblies. See [the Host business ABI](sidecar-host-business.md) for session admission, schema discovery, and cancellation semantics.

```sh
python eng/packages/build.py --output /absolute/path/to/feed
python eng/packages/verify.py --feed /absolute/path/to/feed
python eng/packages/verify.py --feed /absolute/path/to/feed --aot
```

Use the pinned .NET 10 SDK, Python 3, and required native dependencies. The local feed manifest records source SHA, dependencies, and package SHA-256. Building a feed does not publish it to a public registry.

Runtime packages contain assemblies, licenses, documentation, matching `.nfp` catalogs, and buildTransitive copy targets. BuildTools contains private build dependencies only; Roslyn and Mono.Cecil must not enter product output. Consumers explicitly reference BuildTools with `PrivateAssets=all` and set `NexaAutomaticFunctionPatches=true` to instrument their own code.

Verification uses independent consumer projects and fresh caches to check command acceptance/completion, state, instrumentation installation/retirement, incremental builds, and managed/AOT output. Consumers do not reference NexaCL workspace paths. Referencing Runtime alone does not start Sidecars or enable remote plugins.

Published package versions and bytes are immutable; content changes require a new package version. Source locations follow `eng/architecture/projects.json`; API identities do not change with directory moves.
