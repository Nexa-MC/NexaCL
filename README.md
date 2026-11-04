# NexaCL Firefly — derivative development and contributions

English | [简体中文](README.zh-CN.md)

This repository is for **NexaCL derivative developers, contributors, and maintainers**. It contains a Minecraft Java launcher built with .NET 10, NativeAOT, and XSR. Official distributions target Windows, macOS, and Linux on x64 and ARM64.

Players can find installers and release notes on [Releases](https://github.com/Nexa-MC/NexaCL/releases). This README explains how to understand, build, modify, and distribute the source.

| Goal | Start here |
| --- | --- |
| Develop a derivative | [Derivative development](docs/derivative-development.md) |
| Contribute a change | [Contribution guide](CONTRIBUTER.md) |
| Locate implementation owners | [Repository layout](docs/repository-layout.md), [project index](docs/modules.md) |
| Understand DLL boundaries | [Architecture](docs/architecture.md), [native modules](docs/native-modules.md) |
| Build, test, and distribute | [Building](docs/building.md), [testing](docs/testing.md), [releasing](docs/releasing.md) |
| Develop UI, themes, or extensions | [Presentation](docs/presentation.md), [themes](docs/themes.md), [Sidecar](docs/sidecar.md) |
| Track launcher compatibility | [Feature parity](docs/launcher-feature-parity.md), [completion checklist](docs/parity-completion.md) |

## Local development

Install the SDK pinned in `global.json` and Python 3. Native compilation also requires the target platform's compiler and libraries.

```sh
dotnet restore NexaCL.slnx
dotnet build NexaCL.slnx -c Release -m:1
dotnet run --project src/app/Nexa.Desktop -c Release --no-build
```

Running Desktop directly is the managed development path. Official distributions use `eng/release/publish_launcher.py` to build Bootstrap, Host, UI, independent service/engine modules, and the signed Sidecar. A development executable is not a complete release.

## Source organization

`src/app` owns entry points and composition. `foundation`, `runtime`, and `rendering` own shared contracts, XSR, and presentation. `modules/<feature>` groups service contracts, implementations, clients, and native owners. `engines`, `platform`, `sidecar`, and `interop` own native engines, OS integration, process transport, and ABI primitives.

`tests` mirrors feature ownership; `tools` contains compilers, probes, and benchmarks; `eng` contains engineering and distribution scripts. Project paths are recorded in `eng/architecture/projects.json`. Project identity, source directory, and distributed DLL name are separate concerns.

## Development boundaries

Follow [Apache License 2.0](LICENSE) and all applicable third-party licenses and notices. Derivatives must configure their own product identity, authorized service credentials, update sources, and signing trust. Upstream private keys and service credentials are not provided.

Read the [contribution guide](CONTRIBUTER.md) and [documentation index](docs/README.md) before changing ownership or trust boundaries. Current account eligibility, regional, and update requirements are documented in [security](docs/security.md). Keep build outputs, caches, local configuration, and credentials out of commits.
