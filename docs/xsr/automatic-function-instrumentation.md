# Automatic function instrumentation

Boundary decision, 2026-10-24. Extends the requirements in
[universal extensions](universal-sidecar-extensions.md).

Every owned product assembly is instrumented in the build, after C# lowering and
before trimming/NativeAOT compilation. This covers ordinary/instance/static and
generic methods, accessors, local functions, lambdas, async/iterator state machines,
constructors and managed facades for native imports without source annotations.
Abstract and runtime-provided declarations have no executable managed body and
are explicitly inventoried. The patch engine and generated bootstrap are a
recorded infrastructure exception: interception must not recursively intercept
its own admission, allocation or bookkeeping.

The compiler emits deterministic signature-derived target identities, phase/type
metadata and a catalog. Constructor interception begins after mandatory CLR
initialization. The original body, exception regions and normal return semantics
remain intact; tail/return phases run on successful returns, matching the existing
function ABI. A missing or retired program leaves original execution authoritative.
Unpatched entries test a nullable assembly-local activation bitmap; argument
packing, allocation and interpreter work occur only for enabled points.

Patch registration remains signed Sidecar registration and bounded local execution.
No synchronous IPC, runtime IL rewrite, CLR assembly loading, generated runtime
code or reflection dispatch is added. Primitive values support bounded arithmetic
and constants. Opaque managed values stay inside the host and can be forwarded;
byref-like values, pointers and reference returns use typed argument/result
references emitted by the build compiler. An operation that cannot supply a valid
CLR value is rejected or fails open, never fabricated as an unsafe reference.

The common engine belongs to the dependency-free Core layer. The build compiler
is a build-only dependency, never a shipping runtime reference. Runtime owns wire
program validation and leases; Desktop wires admission once. New product methods
enter the catalog through the normal build, without feature-specific registration.
Instrumented protocol/transport builds also carry Core for their generated local
entry points; their wire format and numeric dispatch remain independent of CLR
types. Consumers must publish their project-reference closure. A deliberately
portable SDK build can set `NexaAutomaticFunctionPatches=false`; shipped Host
projects and acceptance builds use automatic coverage by default.

## Automatic build and catalog

`Directory.Build.targets` enables the compiler on root product projects. An
independent managed tool runs after `CoreCompile`; its Roslyn/Mono.Cecil packages
are private build dependencies. The IL and portable PDB are rewritten together
in `obj`. An untouched backup permits compiler upgrades on incremental builds;
the compiler fingerprint prevents double instrumentation. Intermediate backups
are never published. The matching `AssemblyName.nfp` is copied with the product.

The catalog is embedded as `Nexa.FunctionPatches.Catalog.v1`, with an identical
external `.nfp` file. Brotli decompresses to a little-endian binary stream:
`NPC1` magic, Int32 count, then entries in ordinal target-ID order. Each entry has
BinaryWriter UTF-8 strings (7-bit encoded byte lengths): ID, signature and result
shape; result-traits byte; status byte; Int32 slot; UInt16 parameter count; and
each parameter's name string, shape string and traits byte. Trait bits are Value=1,
ByReference=2, StackOnly=4, Out=8, Address=16. Status is Instrumented=0, Abstract=1,
RuntimeProvided=2 or Infrastructure=3. Instrumented entries have dense slots;
other entries have slot -1. Required/optional modifiers remain in identities;
assignment shape omits modifiers. Generic shape parameters are `!n`/`!!n`.

Target ID is `function.auto.` plus the first 16 bytes of SHA-256 over UTF-8
`assembly-name|canonical-signature`, encoded as lowercase hex. Signature syntax
is `static|instance DeclaringType::Method``arity(parameters)->result`. The build
compiler is the authoritative implementation of this canonicalization. Plugins
read the published catalog rather than guessing names or reflection tokens.
The Host resolves only its compiled catalog; a modified external catalog cannot
create or alter an executable target. Module bootstraps contain direct static edges
to the generated catalog bootstrap of each referenced product assembly. Referenced
modules register before READY even if a feature has not been opened; this needs no
reflection or per-feature adapter. All product references in one build use the
same instrumentation setting. The common build emits assembly metadata
`Nexa.AutomaticFunctionPatches=1` into compiler reference assemblies; the weaver
uses that explicit ABI marker rather than guessing ownership from an assembly
name. An uninstrumented vendor reference does not receive a bootstrap call.
The compiler remains a dependency for the old explicit rewriter even when an SDK
build disables automatic instrumentation. Trimmed, unreachable implementations cannot be made
callable through a catalog entry.

## NFP3 wire program

Registration kind remains FunctionPatch=12 with flags=0, SHA-256 payload hash and
`TargetSemanticId` set to the compiled target ID. Peers negotiate
`SidecarFeatures.AutomaticFunctions` (bit 32). NFP1/NFP2 peers and explicit points
retain their existing format. NFP3 starts with ASCII `NFP3`, ABI byte 1, phase byte
(Head=1, Args=2, Tail=3, Return=4, Replace=5), UInt16 instruction count.

| Opcode | Operand | Meaning |
| --- | --- | --- |
| 1 | argument index UInt16 | load typed argument value, including host-local opaque values |
| 2 | none | load successful result in Tail/Return |
| 3 | kind byte, length UInt16, strict codec bytes | push primitive constant |
| 4 | none | checked same-type addition or bounded string concatenation |
| 5 | argument index UInt16 | store exactly matching argument in Args |
| 6 | none | store exactly matching result |
| 7 | none | skip original in Head/Replace after supplying a non-void result |
| 8 | none | final instruction, empty stack, no trailing bytes |
| 9 | argument index UInt16 | load typed argument reference for a reference return |
| 10 | none | load typed-default marker (never a fabricated byref) |
| 11 | none | compare same-type known primitives, producing Boolean |

Constant kinds: String=0 (UTF-8), Boolean=1 (one byte 0/1), Int32=2,
Int64=3, Double=4, Byte=8, SByte=9, Int16=10, UInt16=11, UInt32=12,
UInt64=13, Single=14 and Char=15 (UInt16). Scalars use exact-width little-endian
encoding; floating values must be finite. No object serialization, casts, virtual
calls, arbitrary memory operations, jumps, loops or I/O instructions exist.

Limits: 16 KiB payload, 128 instructions, stack 16, 4 KiB constant, 4,096-character
concatenation result, 256 programs/session, 32 programs/session/target, 64 active or
pending hooks/target and 4,096 total hooks. Validation binds operations to the
compiled shape before READY. An opaque value can only be forwarded to an exact
matching type. Unknown generic types cannot be reinterpreted as primitives.
Stack-only/pointer values are represented by compiler-local reference tokens;
safe matching arguments or typed default may replace them. A byref result may
only use a matching incoming byref or retain its original result.

## Execution and lifecycle

HEAD, ARGS and REPLACE run before the original body. Constructors preserve base/
delegating initialization and pre-base field initializers. Successful original
and replacement results run TAIL then RETURN. TAIL sees refreshed argument values,
including ref/out results. Async kickoff, its generated MoveNext, and iterator
methods are separate catalog entries; a kickoff hook observes its Task/iterator,
not a fabricated awaited value. Cancellation and original exceptions propagate
unchanged and skip successful-return phases.

Each session prepares inactive hooks, then publishes one atomic batch flag after
all entries are installed. Deactivation/failure/disposal clears that flag before
removing entries. Calls do not wait for unloading. Changes from an in-flight
program retired before commit are discarded. Nonfatal program faults roll back
that program and disable it for its activation. Reactivation gets fresh hooks.
Thread-local interpreter recursion protection invokes original host helpers
directly while a program is being evaluated; there is no synchronous Sidecar
round trip. Registrations in another session remain independent.

Acceptance requires inventory verification, functional execution across signatures
and phases, exception/cancellation/retirement regressions, no-program allocation/
performance measurements, and execution of published NativeAOT binaries. CI runs
the compiler fixture and verifies every owned DLL in the Desktop closure, rejecting
missing bodies, missing guards or catalogs that disagree with their assembly.
