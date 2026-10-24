# Shared NXML Sidecar UI — 2026-10-24

## Delivered boundary

Schema 3 uses the product NXML suite, shared controls, product styles, dynamic
fragments, input and renderer. A global presentation coordinator discovers any
named product node, including nodes created after registration or during logical
frame preparation. Feature controllers do not declare slots, render modules or
dispatch their actions. Architecture checks reject new NXML Sidecar adapters in
feature controllers and prohibit translating shared documents into the legacy
control enum.

Append/prepend/before/after, component modification and subtree replacement share
one presentation graph. The logical tree remains owned by controllers. Live
bindings, child permutations and underlying content survive replacement; retiring
a layer restores its live predecessor or current host presentation. Failed mounts
roll back, retained hidden pages remain hidden, stale input is rejected, and
coherent state refresh preserves control identities and drafts.

Sources travel as a hash-verified binary bundle with actual UTF-8 byte limits.
The outer registration codec now transports large bundles with bounded ordered
continuations while preserving the old encoding up to 65,535 bytes. Compilation
and host state-reference validation precede READY. Mirror updates compile on a
coalesced worker and publish immutable State; unchanged render frames neither
parse/compile source nor use IPC. Empty extension state bypasses tree discovery.
Unmodified components retain direct component-slot reads.

NXML `Argument` and local `ArgumentFrom` references provide common scalar/form
submission. Missing, cyclic and excessively deep references fail compilation.
The global intent route delivers declared Sidecar commands once, preserves host
commands, drops retired-source operations, and retains operation diagnostics
without logging input drafts. Deactivation/reactivation retains current values
and renews exposure identities. Cancellation and session terminal paths retire UI.

This slice does **not** deliver automatic instrumentation of every function.
The existing explicitly annotated Function Patch compiler remains unchanged.
The earlier all-function requirement remains outstanding; UI coverage must not
be reported as function-instrumentation coverage. Legacy text/card/UI document
schemas remain compatibility adapters, rather than the language for new UI.

## Executed evidence

Local Windows x64, Release:

- Sidecar protocol: 36 passed, 0 skipped, including a source bundle over 64 KiB
  through REGISTER_ITEM, immutable ownership, malformed paths and source budgets.
- NXML: 84 passed, including future target discovery, shared custom components,
  product-compatible styles, multiple dynamic fragments, draft retention, local
  form references, same-frame late projection, mount rollback and layering.
- Renderer: 124 passed.
- XSR runtime: 175 passed, 0 skipped, including pre-READY validation, asynchronous
  state projection, deactivation/reactivation and terminal retirement.
- Desktop composition: 371 passed, 0 failed. The new test registers a large NXML
  bundle through a real loopback Sidecar session, creates an unrelated page
  afterwards, submits a local form value, checks host/Sidecar routing, refreshes
  state, preserves its draft, and restores current host content after retirement.
- Architecture: 72 projects passed, including the new no-feature-adapter guard.
- Formatting and whitespace checks completed for changed C# sources.

Actual NativeAOT execution, rather than publish-only evidence:

- Windows x64 NativeAOT NXML executable: 84 passed.
- Windows x64 NativeAOT runtime executable: 175 passed, 0 skipped.
- Windows x64 NativeAOT Desktop: published, then `--validate-shell` executed and
  exited with code 0. Trimming ran as part of NativeAOT publication.

The final native runtime run passed after retrying transient Windows temporary
executable cleanup failures encountered during concurrent native publication.
No test was skipped and no antivirus exclusion was added. Desktop publication
still reports the existing Jint IL2098 trim warning; this change does not claim
to remove it. macOS/Linux results and remote CI remain distinct evidence.

Validation logs and native executables are local under
`.tmp-cleanup-validation/universal-sidecar-20261024/`, excluded from commits.
