# Building and development

English | [简体中文](building.zh-CN.md)

Run commands from the repository root. Install the .NET 10 SDK pinned in `global.json` and Python 3. NativeAOT additionally requires the target OS compiler, linker, and platform libraries. The actual setup is specified by `.github/workflows/launcher-build.yml` and `xsr-ci.yml`.

```sh
dotnet restore NexaCL.slnx
dotnet build NexaCL.slnx -c Release -m:1
dotnet run --project src/app/Nexa.Desktop -c Release --no-build
```

Project names and assembly identities remain stable across directory changes. `Directory.Build.props` identifies the repository and product source roots; `Directory.Build.targets` enables automatic instrumentation for product projects under `src/`. Tools and tests do not infer product identity from directory depth; native projects retain explicit instrumentation settings.

## Native distributions

```sh
python eng/release/publish_launcher.py --rid linux-x64 --output artifacts/launcher
python eng/release/smoke_native_modules.py --payload artifacts/launcher
```

Supported distribution RIDs are `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`. Compile and verify on the matching platform. `eng/release/package.py --help` describes packaging; the tool validates runtime closure and produces platform installers and portable packages.

The instrumentation compiler in `tools/compiler/Nexa.Xsr.Patch.Compiler` is a build dependency. Roslyn, Mono.Cecil, and CoreCLR must not enter the NativeAOT product closure. Restore, generate, compile, and verify in dependency order. Builds sharing `bin/obj` must run serially; use a separate checkout for independent native and managed modes.

Actions supplies authorized secrets. Source must not contain tokens, API credentials, or signing keys. Record missing external dependencies explicitly and retain the verification gates.
