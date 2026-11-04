# Shared packages

`eng/packages/manifest.json` defines shared packages independently of product Alpha/Beta versions. The closure includes Core, Contracts, Sidecar.Protocol, Sidecar.Transport, XSR Abstractions, Diagnostics, State, Runtime, and separate BuildTools. Renderer and platform implementations are outside this closure.

```sh
python eng/packages/build.py --output /absolute/path/to/feed
python eng/packages/verify.py --feed /absolute/path/to/feed
python eng/packages/verify.py --feed /absolute/path/to/feed --aot
```

Use the pinned .NET 10 SDK, Python 3, and required native dependencies. The local feed manifest records source SHA, dependencies, and package SHA-256. Building a feed does not publish it to a public registry.

Runtime packages contain assemblies, licenses, documentation, matching `.nfp` catalogs, and buildTransitive copy targets. BuildTools contains private build dependencies only; Roslyn and Mono.Cecil must not enter product output. Consumers explicitly reference BuildTools with `PrivateAssets=all` and set `NexaAutomaticFunctionPatches=true` to instrument their own code.

Verification uses independent consumer projects and fresh caches to check command acceptance/completion, state, instrumentation installation/retirement, incremental builds, and managed/AOT output. Consumers do not reference NexaCL workspace paths. Referencing Runtime alone does not start Sidecars or enable remote plugins.

Published package versions and bytes are immutable; content changes require a new package version. Source locations follow `eng/architecture/projects.json`; API identities do not change with directory moves.
