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

Resource fingerprints retain the provider's exact SHA and whitespace-filtered Murmur2 identities.
The normalization loop selects hardware-accelerated 256-bit vectors, 128-bit vectors
(including ARM64 AdvSimd), or a scalar fallback. No machine-wide instruction-set
requirement is added. After building, exercise the managed dispatch paths on x64:

```sh
dotnet tests/app/Nexa.Services.Tests/bin/Release/net10.0/Nexa.Services.Tests.dll "file fingerprints"
DOTNET_EnableAVX2=0 DOTNET_EnableAVX512F=0 dotnet tests/app/Nexa.Services.Tests/bin/Release/net10.0/Nexa.Services.Tests.dll "file fingerprints"
DOTNET_EnableHWIntrinsic=0 dotnet tests/app/Nexa.Services.Tests/bin/Release/net10.0/Nexa.Services.Tests.dll "file fingerprints"
```

These cases compare against a scalar reference at vector and stream-read boundaries.
Managed feature switches do not establish NativeAOT or ARM64 execution coverage;
run the same case in the published test executable on each target platform.

Download lifecycle regressions cover new callers arriving while a canceled writer
is retiring, independently canceled waiters, failing cancellation callbacks, and
release of per-artifact cache coordination after the last active or queued user.
DoH tests stall the response body after headers to verify that the request deadline
still triggers fallback while explicit caller cancellation propagates.

`ResourceInstalledFilterScansInventoryOnce` exercises the real resource-page toggle
with 5,003 installed files, merged provider identities, disabled mods, and a non-mod
result. It bounds inventory enumeration rather than relying on timing: the fixture
visits 5,003 entries with the index, compared with 25,012 before the optimization.
