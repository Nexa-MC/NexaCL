# Automatic function instrumentation

The compiler in `tools/compiler/Nexa.Xsr.Patch.Compiler` runs as a managed build tool. Source generation and IL rewriting create bounded function-entry shapes and `.nfp` catalogs. NativeAOT products neither compile runtime code nor include Roslyn or Mono.Cecil.

`Directory.Build.props` identifies product source under `src/`. `Directory.Build.targets` enables common instrumentation and a build-only compiler reference. Native shared libraries retain their existing independent/shared Core ABI settings. Moving tools or tests must not accidentally apply product instrumentation based on directory depth.

Rendering assemblies under `src/rendering/` weave only their major bodies. An entry on every accessor, value constructor, and per-node helper stops the compiler from inlining them: on the renderer kernel benchmark a 1,600-node frame spent 25–45% of its time on that overhead, and `Nexa.Xsr.Renderer.dll` was four times larger. The common build therefore passes `--minimum-body 256` for that root. A body keeps its entry when its IL is at least that large and it is not a value-type constructor or a compiler-synthesized member (record members, lambdas, state machines); an async or iterator method is measured by its state machine. Every other body is still inventoried, with status `Internal`, and installing a patch on it is rejected like any other target without a managed entry. A project cannot choose its own scope.

`eng/xsr/Xsr.AutomaticFunctionPatches.targets` instruments after CoreCompile. Matching `.nfp` files must accompany assemblies in build and publish output. Compiler tests cover call boundaries, parameter/result shapes, entry/leave phases, exceptions, retirement, and scoped coverage. Mismatched catalogs must fail explicitly. Verification recomputes each `Internal` entry from the shipped body and the scope its assembly declares, and accepts a declared scope only from rendering assemblies.

```sh
dotnet run --project tools/compiler/Nexa.Xsr.Patch.Compiler -c Release -- --self-test-automatic --core src/foundation/Nexa.Core/bin/Release/net10.0/Nexa.Core.dll
# Build Desktop before inspecting its output.
dotnet run --project tools/compiler/Nexa.Xsr.Patch.Compiler -c Release --no-build -- --verify-automatic src/app/Nexa.Desktop/bin/Release/net10.0
```

Shared-package BuildTools use the same targets but require explicit consumer opt-in. Shipping a catalog does not install an extension into a consumer. Cross-process extensions also negotiate feature bits, authorization, and session resource lifetimes.
