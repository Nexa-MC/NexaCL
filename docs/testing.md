# Verification entry points

English | [简体中文](testing.zh-CN.md)

Run from the repository root. Build before checks that inspect compiled assemblies. Do not concurrently rebuild the same `bin/obj` outputs while a suite uses them.

```sh
dotnet restore NexaCL.slnx
dotnet build NexaCL.slnx -c Release -m:1
dotnet run --project tests/architecture/Nexa.Xsr.ArchitectureTests -c Release --no-build -- --repo-root .
dotnet run --project tests/runtime/Nexa.Xsr.Runtime.Tests -c Release --no-build
dotnet run --project tests/app/Nexa.Services.Tests -c Release --no-build
dotnet run --project tests/app/Nexa.Desktop.Tests -c Release --no-build
python -m unittest discover -s eng/release -p 'test_*.py'
python eng/xsr/generate_account_routes.py --check
python eng/xsr/generate_host_service_catalog.py --check
dotnet format NexaCL.slnx --no-restore --verify-no-changes --diagnostics IDE0055 IMPORTS
```

Run Renderer, Sidecar, theme, and native-port suites according to `NexaCL.slnx` and their workflows. Most suites are executable programs; an empty `dotnet test` result is insufficient. `eng/architecture/verify_layout.py` checks project locations, references, dependency ownership, and documentation links.

`xsr-ci.yml` covers builds, semantic architecture, instrumentation, service/UI regression, NXML/themes, formatting, AOT, and trimming. `native-engines.yml` verifies independent native boundaries. `launcher-build.yml` validates six-RID release closure, reproducibility, real windows, the signed Sidecar, installers, update transactions, complete packages, differential reconstruction, and distribution signatures.

Run graphical and OS integration checks on their native platforms. Record unavailable displays, permissions, credentials, or hardware. Historical reports are not current evidence. Do not hide failures by relaxing dependencies, swallowing exceptions, removing checks, or weakening security contracts.
