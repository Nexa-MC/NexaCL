# Automatic product function patches

2026-10-24. Follows the shared NXML Sidecar migration and implements the function
coverage requirement in [universal extensions](../universal-sidecar-extensions.md).

The previous product exposed explicitly marked caption functions. Ordinary new
functions required source annotations and hand-maintained admission points. The
common build now rewrites every executable body in owned product assemblies after
C# lowering, before trimming and NativeAOT. The published catalog and embedded
catalog agree, with deterministic signature-derived IDs. A new ordinary method
requires no Patch attribute, delegate adapter or Desktop registration.

The Core engine uses immutable activation tables and an atomic session batch.
An unpatched call checks the assembly-local nullable bitmap, without argument
packing, allocation, interpreter execution or process exchange. Typed arguments
are packed only on the active branch. Stack-only values and reference returns
use compiler-local typed references. Nonfatal program faults roll back and retire
the offending hook; deactivation discards unfinished changes and restores normal
execution. A global hook limit bounds admission across sessions.

Runtime accepts the negotiated, shape-checked NFP3 program through existing signed
FunctionPatch registration. Desktop installs admission once. Original exceptions,
finally and cancellation remain authoritative. Generic constraints, caller-info
attributes, native calling conventions and constructor initialization remain in
the compiled implementation. Module bootstraps eagerly register referenced product
catalogs without initializing every product type or using reflection. Catalog
publication does not root otherwise unreachable implementation bodies.

The patch engine and generated bookkeeping are excluded to prevent self-interception.
Abstract/runtime declarations are inventoried without claiming managed execution.
This covers product implementations in their owning process; it does not rewrite
the CLR, framework libraries or another process's native code.

The compiler remains a managed build dependency; Mono.Cecil/Roslyn do not enter
the shipping runtime. Protocol now references the dependency-free Core ABI for
injected entries. The locked graph records that runtime dependency; the common
build-only tool edge is checked separately from runtime project references.
Existing explicit NFP1/NFP2 programs and source-rewriter builds remain compatible.

Required regression entry points:

- Compiler `--self-test-automatic --core ...`: ordinary and generic members,
  constructors, initialization, ref/out, readonly refs, stack-only generics,
  Span, function pointers, async, iterators, native facades, exceptions, incremental
  idempotence, missing-catalog repair, missing-body rejection and early discovery.
- Compiler `--verify-automatic <Desktop output>`: every owned DLL has its matching
  catalog and every cataloged managed body has actual generated interception.
- Compiler `--benchmark-automatic --core ...`: original/instrumented checksums and
  zero-allocation fast paths; reports timing without a machine-specific limit.
- Runtime acceptance: actual Sidecar activation/retirement/reactivation, generic/
  stack/reference arguments, malformed and denied programs, atomic batches,
  in-flight retirement, fault isolation, budgets and no-op allocation.
- Architecture: automatic defaults, build-only compiler ownership, common publish
  catalog, no product opt-outs or compiler-package runtime references, and CI gates.
- Real published NativeAOT runtime executable and Desktop shell smoke, followed
  by the existing renderer and UI regressions.

## Executed local evidence

Windows x64, .NET 10, Release, 2026-10-24:

- Full solution build: zero warnings/errors.
- Desktop closure: 57 assemblies, 40,343 executable bodies verified against their
  matching published catalogs. Native import facades are included; no-body and
  infrastructure entries are separate.
- Compiler fixture: 46 assertions, including early discovery of an unused product
  dependency and a same-prefix uninstrumented vendor that must remain untouched.
- Runtime: 181 managed tests and 181 actual NativeAOT tests, zero skipped. Native
  cases include active constructor, Task/iterator, generic, Span and byref hooks.
- Desktop: 371 passed; Avalonia backend: 44 passed; NXML: 84 passed; renderer: 124
  passed; Sidecar protocol: 36 passed, zero skipped; architecture: 72 projects.
- Actual NativeAOT Desktop `--validate-shell` and `--validate-setup`: exit 0.
  Trimming ran during NativeAOT publication. The existing Jint IL2098 warning
  remains distinct from the instrumentation compiler and is not claimed fixed.
- Desktop managed dependency manifest and native publish directory contain no
  instrumentation compiler, Mono.Cecil, Roslyn, backups or reference response files.
  The native publish directory contains all 57 matching `.nfp` catalogs.
- Formatting verification and whitespace checks passed for changed sources.

The isolated, non-inlined 10-million-call benchmark reported 1.12 ns/call before
instrumentation and 2.25 ns/call after, with zero allocated bytes in both cases.
These are machine-local timings, not a latency promise. The renderer's existing
correctness/allocation benchmark gate also passed. The native Desktop executable
grew from 65,135,104 to 90,828,800 bytes relative to the preceding shared-NXML build;
catalogs and active branches carry a code/data size cost even when the fast path
is inactive. This change does not claim zero binary or instruction overhead.

During concurrent native compilation two five-second Desktop waits timed out.
Focused repeats and a complete isolated run subsequently passed all 371 cases;
the deadlines and product logic were not weakened. Screenshot failures now emit
read/realization/quota counters to make future failures actionable. A stale
Windows-only decoration assertion was corrected to the existing XSR-833 contract,
retaining Full native capabilities on Windows/macOS and None on Linux.

An extra full Services run was stopped by the certificate test inheriting this
machine's user-scoped API certificate while using its temporary test password.
That run is not counted as passing. Cross-platform CI and actual physical-device
coverage remain separate from the local evidence above.

Local logs and executables are under
`.tmp-cleanup-validation/universal-sidecar-20261024/` and are excluded from commits.
