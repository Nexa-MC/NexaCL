# Repository layout

Infrastructure is layered and business code is grouped by capability. Adjacent directories do not imply permitted dependencies.

```text
src/
  app/          Entry points, native Host, composition, JVM Host, update helper
  foundation/   Core, Domain, shared Contracts
  runtime/      XSR abstractions, state, scheduling, diagnostics
  rendering/    Renderer, backends, NXML, window ABI
  modules/      Accounts, network, Minecraft, updates, other capabilities
  engines/      PAC, Crypto, Media, Markup, Artwork
  platform/     Platform contracts, implementations, Explorer integration
  sidecar/      Independent-process protocol and transport
  interop/      Native calls, buffers, asynchronous resources, service ABI
tests/          Matching feature groups; Shared contains shared fixtures
tools/          Compilers, probes, benchmarks
eng/            Build, architecture, release, generators, engineering gates
docs/           Current project documentation
```

Projects retain their full names. In `src/modules/accounts/`, `Nexa.Services.Accounts.Contracts` defines messages/state, `Nexa.Services.Accounts` implements them, `Nexa.Native.Accounts.Client` calls the owner, and `Nexa.Native.Accounts` is the NativeAOT entry. `protocol/` contains source shared by both sides. Minecraft, Network, and Updates follow the same grouping. A capability does not necessarily need a native client or a separate distributed DLL.

`src/app/Nexa.Services` retains Foundation composition, compatibility forwarding, and Minecraft adaptation; `Nexa.Services.Composition` binds routes and resource ports. These are composition responsibilities, not a generic implementation layer. `Nexa.Desktop` owns UI/controllers and managed development. The released same-named executable comes from `Nexa.Bootstrap`; UI becomes `nexa-ui`.

Common ABI source lives under `src/interop/`; feature protocols live under their module's `protocol/`. The window protocol is `src/rendering/protocol/window/`, and Host admission catalogs are `src/app/protocol/`. Shared source may compile into multiple NativeAOT owners without exchanging CLR objects or creating another distributed assembly.

Project, assembly, namespace, and distribution identities remain stable. `NexaCL.slnx` groups match disk locations; `eng/architecture/projects.json` records the complete project map. Moves/additions update these and affected engineering scripts together. Keep project `bin/obj` outputs out of commits.

See [migration](repository-migration.md). Workflows pinned to historical source must interpret that revision's paths: `alpha17-recovery.yml` deliberately retains old locations, while current-branch workflows use the new inventory.
