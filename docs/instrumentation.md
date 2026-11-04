# Automatic function instrumentation

The compiler in `tools/compiler/Nexa.Xsr.Patch.Compiler` runs as a managed build tool. Source generation and IL rewriting create bounded function-entry shapes and `.nfp` catalogs. NativeAOT products neither compile runtime code nor include Roslyn or Mono.Cecil.

`Directory.Build.props` identifies product source under `src/`. `Directory.Build.targets` enables common instrumentation and a build-only compiler reference. Native shared libraries retain their existing independent/shared Core ABI settings. Moving tools or tests must not accidentally apply product instrumentation based on directory depth.

`eng/xsr/Xsr.AutomaticFunctionPatches.targets` instruments after CoreCompile. Matching `.nfp` files must accompany assemblies in build and publish output. Compiler tests cover call boundaries, parameter/result shapes, entry/leave phases, exceptions, and retirement. Mismatched catalogs must fail explicitly.

```sh
dotnet run --project tools/compiler/Nexa.Xsr.Patch.Compiler -c Release -- --self-test-automatic --core src/foundation/Nexa.Core/bin/Release/net10.0/Nexa.Core.dll
# Build Desktop before inspecting its output.
dotnet run --project tools/compiler/Nexa.Xsr.Patch.Compiler -c Release --no-build -- --verify-automatic src/app/Nexa.Desktop/bin/Release/net10.0
```

Shared-package BuildTools use the same targets but require explicit consumer opt-in. Shipping a catalog does not install an extension into a consumer. Cross-process extensions also negotiate feature bits, authorization, and session resource lifetimes.
