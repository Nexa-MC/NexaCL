# Native module distribution

The launcher retains NativeAOT. The desktop composition root, renderer and in-process
services form one native Host module: `nexa-host.dll` (Windows), `nexa-host.so`
(Linux), or `nexa-host.dylib` (macOS). Names of Nexa-owned distribution modules are
lowercase `nexa-<module>`. Upstream native dependencies retain their supported names.

`Nexa.Desktop[.exe]` becomes a small NativeAOT bootstrap with no Host project
references. It loads the Host from its own directory using an absolute path and
calls the versioned `nexa_host_main_v1` C ABI on the original OS main thread.
Arguments are UTF-8, caller-owned, valid for the duration of the call. Only integer
exit codes and unmanaged pointers cross this boundary. Exceptions are caught by
the callee. The Host is never unloaded. There is no managed assembly loading or
shared CLR-object ownership between the two NativeAOT runtimes.

The module owns its embedded credentials, resources and version metadata. It
initializes its base directory and Windows apartment before starting the existing
entry point. Existing workers, protocol arguments and CLI validation commands
continue through that entry point. Direct managed development builds remain valid.

This is a distribution boundary, not a claim that Renderer and Services have
independent native ABIs. Such a split would require explicit state, callback,
resource lifetime and buffer contracts before removing project references.

Publish builds both binaries for the same RID/version. Packaging rejects a missing
or empty Host library. Existing signed complete payload manifests and differential
packages cover the library like every other runtime file; modules must not be
updated independently of their matching launcher release. Native smoke tests must
exercise the distributed bootstrap, including a working directory outside the
installation and a missing-module failure.

## Current release inventory

The publisher emits the bootstrap, `nexa-host`, and eight independent native engines.
Extensions are `.dll` / `.so` / `.dylib` for Windows / Linux / macOS respectively.

| Engine | Extracted implementation |
| --- | --- |
| `nexa-pac` | PAC interpreter |
| `nexa-crypto` | OpenPGP verification |
| `nexa-network` | Connection/socket/TLS engine |
| `nexa-updates` | Streaming compression and delta codecs |
| `nexa-media` | Image metadata and wardrobe PNG validation |
| `nexa-minecraft` | Library/classpath/rule and asset metadata algorithms |
| `nexa-markup` | NXML syntax and bounded XML parsing for SVG, Maven and events |
| `nexa-artwork` | Product icons, brand artwork and controller sprite images |

The names identify ownership groups; the service lifecycle portions of network,
updates and Minecraft remain in Host. UI and Accounts have not yet become
independent native modules. The complete business-module acceptance criteria below
remain open.

## Stable engine boundaries

PAC interpretation (`nexa-pac`) and OpenPGP verification (`nexa-crypto`) are independent
native engines. They have their own fixed module version and no Host/Core reference;
changing the launcher version must not stamp a new version into these binaries.
The engines live under `native/` and do not participate in Host function patching.
Host policy, asynchronous network reads, proxy route validation and update admission
remain in their existing services. Managed development uses the same engine sources;
release publishing substitutes bounded C ABI adapters and removes the engines' managed
references from the Host compilation closure.

PAC passes UTF-8 strings and a bounded DNS cache, returns either a proxy-list string,
a required DNS name, cancellation or an error. DNS stays asynchronous in the Host.
A cancellation callback exposes only an integer result, never a managed object.
The existing statement, memory, recursion, time, regex and CLR-access restrictions
remain mandatory. GPG uses opaque per-verification handles: create, feed bytes,
finish, release. The same key fingerprint, revocation, expiration, digest and signature
policies apply in managed and native builds. Failed or missing native engines fail
closed; there is no signature-bypass or automatically embedded fallback engine.

Complete signed payload manifests include every module. Unchanged engine files must
be reused by differential updates. The first migration necessarily adds new files;
future Host-only changes are measured separately from this one-time transition.

The privileged update helper retains a statically linked verifier: its protected
installation and macOS helper-root layout must not acquire a new dynamic dependency
from an application update slot. The interactive Host uses the external engine.

## Business-module split: target, not current implementation

The engine extraction above does **not** complete the requested business-module
split. The target distribution is a bootstrap plus the following ownership groups:

| Module | Owns | Boundary requirements before extraction |
| --- | --- | --- |
| `nexa-host` | Composition, lifecycle, module admission and dispatch | Versioned module registration and shutdown; no duplicated service ownership |
| `nexa-ui` | XSR UI tree, controllers, renderer, Avalonia and native window lifecycle | Revisioned state input and intent output; retain the OS main thread and coalesced render invalidation |
| `nexa-accounts` | Account storage access, login flows, session refresh and wardrobe | Async command/query completion, cancellation and a launch-identity contract; credentials never enter UI state |
| `nexa-minecraft` | Version discovery, install planning, modpacks and game launch | Download/task ports, account identity port, settings snapshots and game-process lifecycle events |
| `nexa-network` | HTTP pool, network policy, downloads and resume state | Async request/stream operations, bounded buffers, cancellation and progress; PAC remains a separate stable engine |
| `nexa-updates` | Update discovery, admission, staging and apply coordination | Download/settings/task ports and signature verification through the crypto engine; privileged helper stays independent |

These are native ABI boundaries, not one DLL per existing C# project. The present
`FoundationHost` passes concrete services and a single `XsrStateStore` into the
runtime composers. `AccountOnboardingRuntime` exposes concrete routers and the
launch-identity resolver, while the Desktop composition root also owns UI
controllers and asynchronous callbacks. Those relationships must be replaced at
both ends before removing their managed project references.

Independent NativeAOT libraries cannot exchange CLR objects, `Task`, `Stream`,
managed delegates or exceptions. Each module must own its objects and expose
versioned C entry points using integers, bounded byte buffers and opaque request
identifiers. An identifier is meaningful only to its owning module. Completion,
cancellation, progress and state publication need explicit asynchronous messages;
the render thread must never wait for service work. State replication must retain
revision ordering, collection deltas and invalidation coalescing. Each module's
callbacks must be quiescent before its owner is disposed; native images stay loaded
for process lifetime. Managed development and native release paths must exercise
the same business implementations.

### Acceptance for the complete split

- Each module is absent from the Host's managed compilation closure; a renamed or
  copied assembly with the same code still linked into Host does not count.
- Native builds exercise login/refresh, install/cancel/retry/resume, launch/exit,
  update admission/apply, UI startup/state rendering and Sidecar integration.
- Missing modules, ABI mismatches, cancellation and shutdown fail deterministically
  without bypassing signature policy, leaking credentials or crossing GC ownership.
- All six supported RID builds retain NativeAOT and lowercase owned module names.
- Two real publication payloads differing in one business implementation are
  reconstructed through the existing delta builder. Record total payload size,
  compressed delta size, unchanged module hashes and new bytes per module.

### Measured engine checkpoint

A Linux x64 publication comparison changed one Host diagnostic string and the
launcher alpha sequence while keeping engine source/toolchain constant. The actual
signed-update payload format's delta reconstruction passed (the benchmark itself
is local and unsigned, not a releasable update):

| File/measurement | Result |
| --- | ---: |
| Native Host size | 83,575,608 bytes |
| PAC size / new delta bytes | 9,452,072 / 0 |
| Crypto size / new delta bytes | 4,274,480 / 0 |
| Complete compressed delta | 33,220,139 bytes |
| Reused payload bytes | 42,210,346 bytes |

Both engine hashes were identical, but the Host contributed 72,621,210 uncompressed
new bytes. This is evidence that stable engine extraction works **and that the
remaining Host is still too coarse**; it is not acceptance of the full split.
Separate NativeAOT runtimes also increase full-payload overhead. Do not describe
this checkpoint as a smaller complete installer or completed business isolation.

## Network connection ABI

The first network extraction owns DNS/DoH caches, address ordering and asynchronous
socket connection in `nexa-network`. HTTP framing, TLS and client certificates stay
with the caller's `SocketsHttpHandler`. Its existing `ConnectCallback` accepts a
`NetworkStream` over the transferred OS socket. This preserves specialized mTLS
configuration, streaming uploads, redirects and response leases without transferring
CLR objects or serializing private keys. This is the connection boundary, not yet
the entire network/download service boundary in the target table above.

Each transport owns one resolver cache. Connect requests acquire a transport lease;
retiring a transport prevents new requests but cannot dispose a resolver still in
use. Requests have unique native IDs and cancellation sources. Completion happens
exactly once, asynchronously, through a C callback containing scalar error metadata,
a bounded UTF-8 diagnostic and, on success, an OS socket handle. The OS handle is duplicated with close-on-exec/non-inheritance semantics, then the
original Socket is disposed normally to unregister its native runtime epoll/IOCP
context. Invalidating the original async Socket handle is forbidden: it bypasses
that cleanup. The duplicate relinquishes ownership before callback invocation. The callback returns whether it
accepted ownership; rejection closes the handle in the native module. Once accepted,
the Host owns disposal even if cancellation wins before publishing the result.
No callback pointer or context may be used after completion returns. Libraries stay
loaded until process exit. Cancellation must also complete the request and release
its transport lease; it cannot merely remove a pending callback.

Managed development uses the same connection engine. Native publications use an
adapter, and the Host must not contain the resolver/connection implementation after
AOT linking. Existing managed DNS tests remain available; native integration tests
must cover real loopback transfer, cancellation, failure and retirement while leased.

The external-adapter regression runner sets `NexaNetworkTestInternals=true` so the
existing injected-client DNS unit fixtures can also compile. Publication explicitly
sets it to `false`: the Host excludes the DNS implementation source entirely, and
has no managed reference to `Nexa.Native.Network`. The module itself links the same
resolver source used by the managed fixtures; there is no alternate resolver policy.

## Reproducible engine identity

Fixed assembly versions alone are insufficient: SDK SourceLink can embed the
repository-wide Git revision and cause unchanged engines to differ on every Host
commit. `native/NativeEngine.props` disables SourceLink/SCM queries and untracked
source embedding for these standalone engines, and maps repository source paths to
`/_/`. Debug symbols remain available, but do not carry a changing repository URL.
The full release manifest retains product provenance and binds the engine hashes.
Reproducibility checks must vary Git/SourceLink revision inputs as well as the Host
version, while keeping engine source, RID, toolchain and native dependencies fixed.

### Network extraction verification

The local Linux x64 publication passed bootstrap/foreign-working-directory checks,
Xvfb shell startup, and the managed-caller regression suite against the published
native libraries. A separate NativeAOT executable in `tests/Nexa.Native.Network.Tests`
reuses the same socket transfer/cancellation/retirement/mTLS fixtures, and passed
against the actual shared library as well. Both caller modes are release CI gates.

`eng/release/verify_engine_reproducibility.py --rid <rid> --payload <published-dir>`
cleans and rebuilds every engine with different source revision, repository commit
and launcher sequence inputs. The Linux x64 run produced identical SHA-256 hashes
for all three engines; release CI applies the check to every supported RID. This
checks fresh compilation rather than an incremental cache hit.

The measured network library is approximately 5.8 MiB; independent AOT runtimes add
full-payload overhead. The remaining Host is still approximately 79.7 MiB. Connection
extraction and reproducibility fixes do not satisfy the business-module target above.

## Module-local automatic instrumentation

Standalone engines compile the shared patch runtime source into their own runtime,
under `Nexa.Native.Instrumentation`, without referencing Host assemblies. Their IL
is woven before NativeAOT compilation and verified with the same catalog/body checks
as Host assemblies. Runtime infrastructure is inventoried but never recursively
instrumented. The native ABI marker is a boundary assertion, not a coverage exemption.
Managed objects, delegates and patch frames remain module-local. Native diagnostic
exports may copy bounded data into caller buffers; they must not expose CLR objects.
Host patch activation does not automatically activate engine-local hooks.

The module exports `nexa_instrumentation_target_v1(index, buffer, capacity)`,
`nexa_instrumentation_probe_v1(index, enabled)` and
`nexa_instrumentation_hits_v1(index)`, all cdecl. Index -1 queries catalog size;
other indices enumerate UTF-8 `id<TAB>status<TAB>signature` records. The returned
size excludes a terminator; short buffers are not modified. Enabling a probe is
idempotent, disabling retires its local hook, and re-enabling resets the count.
Invalid indices, infrastructure targets and invalid enable values return -1.
A probe observes entry only; it does not export arguments, credentials, pointers,
or arbitrary Host callbacks. There is no cross-runtime replacement-patch API yet.
The NativeAOT caller regression verifies all three engines, actual entry hits,
short-buffer behavior, infrastructure rejection and retirement.

### Instrumented binary reproducibility

The IL weaver writes atomically via a random staging directory, but the CodeView
record must contain only the stable PDB basename. Including the temporary directory
changes Cecil's deterministic MVID and consequently the final native library hash.
Portable PDB contents and associations are preserved. A regression test weaves the
same symbol-bearing input in two staging locations and compares both DLL and PDB
bytes. The build-only compiler also uses a fixed version and mapped source paths,
because its implementation fingerprint is embedded into the instrumented modules.

Windows links engines with `/Brepro` and a basename-only PDB reference: captured
failed binaries contained wall-clock PE/debug timestamps and an incrementing PDB
age despite unchanged source. Native debug symbols remain enabled.

The Windows linker retains and increments an existing native PDB age even with
`/Brepro`. Before an actual relink, delete only that engine's generated native PDB;
use the same input/output freshness condition as `LinkNative`, so an incremental
publish that skips linking retains its symbols. A clean rebuild must then produce
identical managed input, native object, DLL and debug identity.

macOS likewise produced identical managed and object inputs while the final UUID
and its ad-hoc code-signature hash changed. Use ld64's `-reproducible` mode to zero
object modification times in the debug map before UUID generation. Keep UUIDs,
dSYM generation and code signing enabled; do not patch signed output bytes.

### Windows transferable connection ownership

Windows duplicates retain the underlying socket's IOCP association. A socket
connected with the engine runtime's `ConnectAsync` therefore cannot subsequently
be bound to the Host runtime's completion port. For native transfers only, perform
nonblocking connect and zero-timeout `Poll` before duplicating the handle.
No IOCP association is created in the engine. Pending connects yield via cancellable
25 ms timer delays rather than occupying a worker or the UI thread, and preserve the existing four-second per-address deadline. Restore blocking
mode before transfer; the receiving Host owns the first asynchronous I/O binding.
Managed same-runtime connections retain the normal `Socket.ConnectAsync` path.

The macOS network engine additionally exposed ld64's duplicate `objc_msgSend` GOT
slots: identical object files linked to two equivalent pointer slots in different
orders, changing three ARM64 stub instruction bytes, UUID and code signatures.
On ARM64, use `-objc_stubs_small` to reuse the ordinary message-send stub instead of emitting
that second GOT slot. This is a linker-supported stub form, not binary rewriting.
The focused macOS CI gate performs three independent clean rebuilds to catch
intermittent identity changes, then executes the NativeAOT socket/mTLS fixtures.

### Updates calculation engine (2026-10-30)

`nexa-updates` owns gzip/zstd streaming decompression and RFC 3284 VCDIFF
calculation. Host retains discovery, signature admission, raw size/SHA-256
verification, staging and apply orchestration. This is not extraction of the
whole Updates service. Managed development and the privileged helper use the
same engine statically; the helper must not load app-slot native libraries.

The streaming ABI passes copied packets of at most 64 KiB, opaque session IDs,
and a wake-only callback with a numeric context. No CLR Stream, Task, exception
or retained caller buffer crosses runtimes. Capacity-one input/output queues plus active 64 KiB packets provide bounded
backpressure without buffering entire blocks. Host performs source reads asynchronously;
source exceptions keep their original type and identity. Cancellation and
release wake pending reads and stop the worker without blocking the UI. Native
codec errors become InvalidDataException. VCDIFF results have explicit copy and
release ownership. All engines retain local instrumentation and stable build
identity; publishing must reject ZstdSharp and native engine assemblies in the
external Host dependency closure.

The `nexa_updates_*_v1` exports use cdecl, signed 32-bit lengths/status and
signed 64-bit session/result IDs (not platform-dependent C `long`):

| Suffix | Contract |
| --- | --- |
| `create(codec, wake, context)` | Codec 1 = gzip, 2 = zstd; returns a session ID, or zero. `wake(int64 context)` can run before create returns and after close. Host registers the context first and ignores retired contexts. |
| `feed(id, bytes, length)` | Copies up to 65536 bytes only after read requests input; zero length is EOF. Returns zero, or -1 for invalid state/input. |
| `read(id, output, capacity, error, errorCapacity)` | Copies up to 65536 output bytes. Nonnegative = bytes/EOF; -2 = input requested; -3 = wait for wake; -4 = codec failure with bounded UTF-8 diagnostic; -1 = invalid call/session. |
| `close(id)` | Idempotent retirement and cancellation; never waits for the decoder worker. Numeric contexts and permanently loaded callback code prevent callback use-after-free. |
| `delta(deltaBytes, deltaLength, sourceBytes, sourceLength, size)` | Borrows spans only for the call, returns an owned result ID and exact size; zero means decode failure. |
| `delta_copy(id, output, length)` | Copies only when length matches the owned result; zero success, -1 invalid input/result. |
| `delta_release(id)` | Idempotently retires the result, including when caller allocation/copy fails. |

A wake is a coalesced hint, not a data packet. Callers recheck state after every
wake; native code never waits for callback work. Per-read cancellation does not
discard a packet already read from the source, so a later read may continue.
Terminal source failures abort the native worker and remain terminal with the
original Host exception. The callback registry uses weak references so pending
read continuations cannot root abandoned streams; SafeHandle finalization retires
their native sessions. Native modules remain release-coherent files covered by
the signed payload; this ABI does not permit independent unsigned module updates.

Local acceptance for this extraction: the Release solution and Linux x64
external Host publish passed; 78-project architecture checks, 10 codec/delta/GPG
regressions, 100 release-tool tests, the real NativeAOT caller (including async
fragmentation, cancellation, disposal, original network errors and streaming
before EOF), and Host ABI/missing-library smoke passed. Automatic coverage
verified 41,422 executable bodies and four standalone engines. A clean rebuild
with different release/commit metadata produced byte-identical `nexa-updates.so`
(3,037,968 bytes; SHA-256
`2ac4a7fad80ce114de5541256609fe00b4061bf6d81a741f3ce74bc87a1a4154`).
Windows/macOS and other RID acceptance remains in the updated native/release CI
matrix; local Linux results do not stand in for those platforms.

### Account-to-launch ownership boundary

Minecraft launch consumes the account launch-access contract, not `AccountService`
or the concrete identity resolver. The account owner supplies profile reads and
its default identity resolution; an explicitly composed refresh resolver still
wins. This removes the Minecraft.Launch -> Accounts implementation dependency
before native extraction. These unpublished in-process assemblies are rebuilt
and distributed together; Sidecar wire contracts and the profile persistence
format do not change. The account launch-access port is privileged and must not
be published as UI state: profile credentials remain confined to account/launch
operations. This dependency change alone is not a native account module.

Account state topology is declared by the Accounts.Contracts assembly, including
profile selection and skin collections. Implementation methods remain forwarding
entry points for callers, with identical semantic IDs, owners, key selectors and
initial availability. This lets Host construct its topology without loading an
account implementation merely to discover its state schema.

### Native media boundary

`nexa-media` owns bounded PNG/JPEG/WebP metadata inspection and complete wardrobe
PNG validation. The Host retains content-addressed media carriers and account
state; no CLR image, stream, exception or account object crosses the boundary.
The synchronous ABI borrows a caller-frozen byte buffer for the call only (at
most 16 MiB for screenshots, 1 MiB for icons/wardrobe), returns dimensions as
integers, and writes bounded UTF-8 diagnostics to a caller-owned buffer. It never
retains pointers or calls the Host. Mode 0 is PNG, 1 screenshot PNG, 2 resource
icon, 3 skin upload, 4 skin preview, 5 cape preview. Slim is an explicit 0/1 flag.
Status 0 means valid; 1 means invalid image; -1 means malformed ABI arguments or
engine failure. Both dimensions are zero on failure. Existing nullable metadata
factories and throwing wardrobe validation retain their public semantics.

The source algorithms are shared between ordinary managed tests and the native
engine; external Host builds exclude their implementation and use the ABI client.
The standalone engine has no Core/Services project reference and retains local
instrumentation. Its name, payload requirements, native caller regression and
reproducibility checks participate in the same release gates as other engines.
This extracts media algorithms, not the entire Accounts service or renderer.

### Native Minecraft metadata boundary

The `nexa-minecraft` boundary owns library resolution/classpath ordering,
Mojang rule evaluation and asset-list path planning. Storage discovery, downloads,
account identity and game processes remain with their current owners. Manifests
cross as their original UTF-8 JSON document bytes inside a bounded binary envelope;
commands and typed results use numeric operations and length-prefixed binary fields.
No CLR manifest, collection or feature dictionary crosses the ABI. Host snapshots
feature lookups for the names actually referenced by each manifest, preserving
custom/case-insensitive dictionary lookup behavior instead of silently changing it.

The engine returns an opaque result ID and exact byte count; Host copies and releases
it in a finally block. Results contain an explicit success/error discriminator and
exception category (not an exception object), preserving caller error classification.
Input/output budgets are 64/128 MiB. Buffers are borrowed only within each call;
retained results are engine-owned and release is idempotent. The library exposes
local instrumentation and receives the same payload/reproducibility/native-caller
checks as the other engines. This is metadata extraction, not game lifecycle migration.

### Native markup parsing boundary

`nexa-markup` owns XML/NXML grammar parsing and its structural budgets. Host retains
component expansion, compiler IR, theme application, UI state and the OS main thread.
The parser accepts at most 1 Mi UTF-16 characters (4 MiB UTF-8 transport ceiling),
returns a bounded binary DOM (16 MiB maximum), and preserves parser error messages
as `NxmlParseException`. Host reconstructs its own elements/properties from a
versioned binary response; no DOM object or XML reader crosses runtimes. DTD, node,
attribute, depth, namespace and binding checks remain in the existing parser.

The transport uses copied opaque results with an unconditional Host-side release,
shared with the Minecraft engine. Host validates response node/depth/count/string
budgets before allocating the DOM. Modules load only from the executable directory.
This boundary moves the syntax parser, not the renderer or full NXML compiler.

All seven engine names are declared once in the publisher's `ENGINES` inventory;
publishing, clean reproducibility, payload validation and installation smoke derive
their required files from it. The small native result client/store implementation is
shared by Minecraft and markup. A borrowed request is read only during execute;
engine-owned result backing arrays are retained until release, copied once to Host,
and never exposed as a managed object or retained Host pointer.

### XML consumers and the markup engine

NXML alone does not remove Host's XML implementation: SVG rendering/resource checks,
Maven catalog parsing and Windows event correlation also consume XML. Their shared
boundary is a small immutable XML snapshot (qualified names, attributes, ordered
text/element/processing-instruction nodes, parent traversal). It carries data only;
rendering, resource grants, network calls and process correlation stay with their
owners. Namespace comparisons, text order and PI detection must preserve current
behavior. DTD and external resolution remain prohibited. Each caller supplies its
existing character/element budgets, with a finite depth and response budget before
Host allocation. Native and ordinary test paths share the parser/snapshot codec.
This lets external Host builds stop referencing XML reader/DOM implementations
instead of keeping a duplicate copy merely to inspect returned documents.

The XML snapshot entry points use the separate `nexa_markup_xml_*_v1` prefix.
Byte inputs preserve the declared encoding/BOM; text inputs retain StringReader
semantics, including declarations naming a different byte encoding. Responses
preserve ordered mixed text, namespace-qualified attributes, parent identity and
processing-instruction presence. Parser failures remain XmlException; explicit
structure/transport budget violations are InvalidDataException. Windows event
correlation treats either as unavailable diagnostic data.

The transport is bounded to 64 MiB input and 128 MiB output, at most 65,536 elements,
1,000,000 nodes, 4,096 attributes per element and depth 128. Callers tighten these:
SVG rendering uses 4,096 elements/depth 25; theme validation uses 8,192 elements;
Windows event XML uses 32,800 characters. Maven metadata has a 16 Mi-character cap.
All parsing disables DTDs and external resolution. Resource authorization, SVG
geometry and event filtering remain in their original owners.

Runtime packaging rejects CoreCLR/JIT and hostfxr/hostpolicy files rather than silently
stripping them from a mixed publish directory. This keeps an accidental managed
publish from producing an incomplete or runtime-dependent launcher payload.

### Content-engine verification checkpoint (2026-10-30, Linux x64)

The default solution builds with zero warnings/errors. Managed and external-engine
paths both pass 163 selected account/launch/install/metadata service regressions,
29 theme service tests, 88 NXML tests and 46 Avalonia backend tests. Architecture
checks pass for 81 projects; release tooling passes 101 tests. An independent
NativeAOT caller covers all seven engines, local instrumentation, buffer ownership,
concurrency, malformed ABI input, XML encoding/namespaces/budgets, cancellation and
TLS identity. The published bootstrap passes foreign-working-directory/missing-Host
checks and `--validate-shell`; the payload contains no CoreCLR runtime files.

All seven engines pass clean metadata-only reproducibility checks. After the XML
consumer extension, markup was rebuilt and rechecked separately; the other six
final binaries remain byte-identical to their verified checkpoint. Markup SHA-256:
`2ba163a1beb5e2d9468625b2a1c4d41920a97719aecef4c9b8188ccf1dd27ff6`.
The existing upstream Jint IL2098 AOT warning remains; no new engine warning was
introduced. Cross-platform execution is enforced by the native-engine and launcher
CI matrices; these local measurements do not establish Windows/macOS results.

A real local delta reconstruction compares the six-engine checkpoint before markup
extraction with the current seven-engine payload. It is an unsigned benchmark,
not a release. The independent JVM/update helpers are outside these supplied
publisher directories; debugging artifacts and installation state are excluded.

| Measurement | Bytes |
| --- | ---: |
| Previous supplied runtime payload | 135,799,160 |
| Current supplied runtime payload | 142,804,077 |
| Current bootstrap | 2,218,840 |
| Current Host | 82,420,984 |
| Current markup engine | 7,955,992 |
| Compressed transition delta | 42,330,794 |
| Reused bytes | 48,664,404 |
| New Host data in transition delta | 79,353,380 |

PAC, crypto, network, updates and media require zero new delta data. Minecraft
changed its result transport in this comparison and markup is a new file, so both
correctly enter this transition delta. Reconstruction matches the target payload.
The total payload grows by 7,004,917 bytes at this checkpoint: independent AOT
runtime overhead is real. This proves reuse and reconstruction, **not** a smaller
complete installer or completion of UI/account/service lifecycle isolation.

The expanded macOS service gate exposed discovery-cache rejection of Apple's
root directory aliases (`/var`, `/tmp`, `/etc`). Link checks admit only those exact
macOS paths resolving to the corresponding `/private` directory, rechecking the
target path. Links inside a game/cache directory remain rejected; the offline
snapshot regression also exercises this rejection on Unix.

### UI backend ownership port

Before the native window runtime can be isolated, the backend must stop holding
`XsrUiShell`, `XsrUiTree`, `XsrUiStage` and `XsrUiStateBridge` directly. A renderer-owned
backend port exposes immutable scenes, input/presentation operations, text metrics,
window observations, a read-only content-transition flag and invalidation subscriptions. The local shell implements it;
the backend uses only the port, retaining existing UI-thread coalescing and explicit
subscription retirement. Tree/state ownership never transfers to the backend.
This managed port is a prerequisite, not a native ABI or permission to pass its
interface object across native runtimes. A native implementation must encode these
operations and callbacks with bounded data and independent lifetime ownership.

Desktop input polling and game-window dispatch also belong to the backend. The
backend owns the input-priority dispatcher timer and returns a disposable lease;
its local callback selects the next interval without exposing Avalonia types.
Disposal retires the callback before posting timer shutdown, including disposal
before queued startup. This is a local ownership boundary; a native adapter must
replace delegates with callback IDs and provide equivalent callback quiescence.

Validation of this ownership step: the complete solution and Desktop test project
built without warnings; all 46 Avalonia backend scenarios passed in both managed
and linux-x64 NativeAOT executables, including alternate shell-port consumption,
100 invalidations coalescing into one render, subscription retirement, and input
polling retirement before startup/from within its callback. Nine gamepad flow
regressions and the 81-project architecture gate passed. These results validate
the local boundary and AOT dispatch; they do not demonstrate a separate native UI
runtime or remove the remaining Host lifecycle implementations.

macOS cache reconciliation also requires instance metadata reads to use the same
fixed system-alias policy. Otherwise discovery returns a metadata error and
intentionally declines to save a snapshot even after cache-path validation succeeds.
Both metadata and snapshot guards share this policy; installation-internal links
remain rejected and metadata failures retain their existing user-facing message.
The two real-snapshot reconciliation regressions pass locally on Linux after this
change; macOS system-alias execution remains a platform CI check.

### Independent product artwork

`nexa-artwork` owns only the launcher icon, brand PNGs and controller sprite PNGs.
The 31 source images total 3,629,309 bytes (about 3.46 MiB); it is not the entire Desktop asset
folder. The Windows native application icon resource is retained for the OS.
Credentials, signing trust, version metadata and UI markup stay in their
existing owners. An explicit resource list is shared by managed development and
native publishing; no directory-wide resource glob may include credentials.

The UTF-8 resource-name request is bounded to 256 bytes. A result contains one
presence byte followed by at most 16 MiB of copied image bytes, using the existing
opaque result/copy/release ABI. Unknown names return absence; there is no filesystem
fallback or resource enumeration across runtimes. The native catalog never loads
Host assemblies. The renderer artwork adapter exposes read-only streams; release builds exclude the catalog
implementation and embedded images from Host. Backend splash/icon and Desktop
controller callers use the same adapter. Stable artwork is version-independent,
while complete signed payloads still govern installation and differential updates.
This extracts resources, not the native window/render lifecycle; total installer
size may increase because this independent NativeAOT runtime has its own overhead.

The same verified macOS root-alias predicate is shared in `PathIdentity` by
metadata caches and recovery storage, including offline readiness. Only exact
OS-owned aliases with the expected target and link-free target ancestors qualify.

Linux-x64 artwork extraction checkpoint: managed solution build, 83-project
architecture gate, nine gamepad flows, 46 backend scenarios, four real snapshot /
offline-readiness regressions and 101 release-tool tests passed. The distributed
bootstrap passed `--validate-shell` outside its installation directory. An independent
NativeAOT caller matched all 31 image hashes against embedded test fixtures and
verified Host-adapter resource exclusion, read-only streams, missing/private names,
invalid requests, concurrent calls and result retirement. The payload has no
CoreCLR/CoreLib runtime files. A clean artwork rebuild with different release and
repository stamps retained SHA-256
`12e396ef4ee33caa1f437c271feff16acb40af70df10ba1fc4790ad063f5c68b`.

Compared with the earlier seven-engine supplied checkpoint, Host changed from
82,420,984 to 78,845,512 bytes; artwork is 5,872,520 bytes. Total runtime payload
changed from 142,804,077 to 145,105,261 bytes. The measured first-migration delta is
43,477,415 bytes with a verified reconstruction; PAC, crypto, network and updates
are identical and contribute no new data. This comparison includes intervening
code/formatting changes, not only artwork extraction. A18/A19 are measurement
fixture labels required by the update format, not newly published releases.
The initial migration increases total size; reproducible unchanged artwork can be
reused by later Host updates without carrying its bytes again.

### Window-runtime transport boundary

The native window runtime consumes a versioned binary scene, input and text-layout
protocol, not CLR scene/tree references. Serialization is generated ahead of time
from an explicit DTO closure; reflection is limited to the development generator.
The wire schema fingerprint changes when that closure changes. Frames have bounded
strings, collections, total bytes and image resources. Encoded images are addressed
by their content hash and retained only across consecutive accepted scenes; rejected
frames cannot partially replace the resource table or current scene. Equal scene
versions are permitted for viewport/presentation changes, while session sequencing
prevents stale packets from overwriting a newer scene. Native callbacks and their
retirement remain a separate lifecycle contract; a codec alone is not a native UI
module and is not introduced as a per-frame local round trip in production.

The input operation table is generated from `IXsrUiBackendRenderer`, including
properties and overload signatures. A matching fingerprint is required before any
operation executes, and the entire argument buffer is admitted before dispatch.
Text-provider installation is an explicit local callback: the provider itself must
remain in its originating runtime and the opposite runtime receives a byte proxy.
Unbounded text-layout width is permitted; NaN and non-finite scene geometry are not.
The code generator runs only during development (`python eng/xsr/generate_ui_wire.py`);
CI's `--check` compares generated sources with the built contract. No runtime
reflection dispatch is added. These channels are preparation for the window-module
lifecycle migration, not a claim that Avalonia has already left Host.

macOS publication regression: the clipboard/resize scenario assumed the custom
maximize button existed in the visual tree. macOS deliberately uses native traffic
lights, so indexing the second custom button failed on both architectures. The
scenario now asserts the platform-specific button count and keeps resize/clipboard
checks on every platform; it does not change window behavior or skip the scenario.

Transport checkpoint validation (Linux x64): all 129 renderer scenarios passed in
both managed and published NativeAOT executables; 46 offscreen/native backend
scenarios and the 83-project architecture gate passed. Schema regeneration matched
both checked-in generated files. The backend timing run initially exceeded the
tray-animation deadline while build processes were active; the isolated rerun after
build-server shutdown passed. macOS CI is still required to verify the corrected
traffic-light expectation on both architectures. No new native UI payload is
advertised or distributed at this checkpoint.

### Duplex native UI call lifetime

The UI boundary uses a versioned C table containing a numeric context and three
function pointers (execute/copy/release). Each runtime registers and retains its own
managed endpoint; only byte buffers and numeric result IDs cross the table. Calls
may reenter the opposite runtime (for example scene construction measuring native
text). No registry lock is held while invoking an endpoint. Exceptions are caught
inside the originating runtime; the opposite runtime receives a bounded failure
marker, not a CLR exception or its potentially sensitive message.

Retirement first rejects new calls. Existing calls and result buffers remain valid
until their execution and copy/release leases finish. Only then may the owning UI
session dispose its window resources. Retirement is asynchronous and must not be
synchronously awaited from a callback it is draining. An independent NativeAOT
probe is test infrastructure, not a distributed window module; its purpose is to
exercise reverse calls, reentrancy, result ownership and shutdown before routing
production startup through this boundary.

Duplex gate checkpoint: an independent Linux-x64 NativeAOT shared peer and NativeAOT
caller passed actual scene/input transport, reverse text measurement, nested calls,
400 concurrent-result round trips, contained endpoint/logging failures, malformed
tables, and retirement with both active calls and outstanding result buffers. The
85-project architecture gate passed. The probe payload was checked for CoreCLR
runtime files. Native engine CI repeats this probe on Linux, Windows and macOS;
the probe libraries are not included in launcher packages.

### Shell projection over the window boundary

The window runtime receives a local `IXsrUiBackendShell` projection, with window
facts and complete scenes encoded separately from generated input operations.
The fixed shell-v1 envelope covers snapshot/render/acknowledge, rail progress,
activity, and native safe-area metrics. A malformed envelope is rejected before
mutating the Host shell. Scene acknowledgement is separate: an abandoned response
forces a full image baseline on the next render. Metadata becomes visible only
after the complete scene response is admitted and acknowledged.

Shell invalidations are notifications, not permission to reenter rendering. The
window dispatcher coalesces them and refreshes its snapshot on its owner thread;
Host event handlers only enqueue the notification. Shell endpoints reject nested
rendering and detach subscriptions after their native call gate drains. This
projection does not move application controllers or pretend to own their state.

Shell projection checkpoint: 130 renderer scenarios passed, including real shell
safe-area/fullscreen changes, lost-ack recovery, reject-before-mutation and event
detachment. The independent NativeAOT peer now owns a full `XsrUiShell`; its caller
uses `XsrUiRemoteShell` over the duplex C table, receives queued invalidations and
checks safe-area/fullscreen geometry. That probe, the 85-project architecture gate
and generated-schema check passed. Production window startup still needs to adopt
this projection together with platform-action and dispatcher lifetime ownership.

### Asynchronous platform operations

Startup, pickers and theme preparation use session-owned numeric operation IDs.
Their `Task` and cancellation sources remain inside their originating runtime.
Start/cancel messages and terminal success/cancel/failure messages use a bounded
binary envelope. The caller installs its completion before starting the remote
operation, so synchronous completion is valid. Cancellation and disposal must not
hold a registry lock while calling the peer. Closing the server rejects new work,
cancels admitted work and waits for every admitted handler and terminal delivery
before its owner releases native resources. Closing the caller cancels its local
waiters; this is not evidence that the server has drained. Session shutdown must
await both the server drain and the duplex call-gate drain.

The call table distinguishes temporary admission exhaustion (`result = 0`, output
length `-2`) from retirement/failure. This status is emitted only before invoking
the endpoint. Async start, cancellation and terminal delivery may back off on this
status; admitted operations are never replayed. This avoids losing a completion
when another runtime temporarily has all call/result slots occupied. Async queues
bound both operation count and retained request bytes. Call registrations can
install an owner-local synchronization context for a callback and restore the
previous one afterward; the actual window adapter must supply a Host-owned
context, never pass Avalonia's context object between runtimes.

Async checkpoint: the Linux-x64 independent NativeAOT probe passed completion
before start returns, delayed completion, cancellation, caller retirement with
pending work, server draining, failure isolation, and three forced busy responses
on both admission and terminal delivery without replaying the operation. It also
verified real call-table exhaustion is reported before endpoint invocation, and
reverse text callbacks install/restore the caller runtime's own context. The
85-project architecture gate and 46 backend regressions (including the merged
Windows tray changes) passed. These helpers are not yet a production startup path.

The next macOS publication run reached the final close/shadow scenario, where a
second test still selected a nonexistent custom close button. All three caption
fixtures now share the platform-specific inventory assertion; macOS invokes the
same shell close command directly, while Windows/Linux retain the pointer-driven
custom-button route. Shadow and backdrop assertions still run on every platform.

Async resource results require an explicit one-byte completion acknowledgement:
`1` means the caller accepted the result into its task; `0` means it retired or
canceled first. A successful result rejected with `0` is returned to an
owner-local rejection callback before server drain completes. Resource-producing
handlers retain provisional ownership until that acknowledgement; a transport
failure or malformed acknowledgement is ambiguous and must remain owned by the
session until explicit claim or session cleanup, not be eagerly freed. This
prevents cancellation from silently losing newly prepared native theme resources
and avoids freeing a result already accepted before an acknowledgement was lost.

Window-owned resources use session-scoped monotonic handles. A newly registered
resource is provisional; only an explicit claim allows subsequent borrowing.
Definite completion rejection retires provisional handles only, so a delayed
rejection cannot invalidate a handle already claimed by the caller. Release and
session shutdown close admission immediately but defer disposal until every
native borrow has returned. Disposal executes outside registry locks and is part
of the session drain. Count and byte budgets include retired-but-borrowed objects.
The wire carries handles only; the registry, disposable objects and borrow guards
remain in the owning native runtime. Unacknowledged provisional resources remain
bounded and are collected at session shutdown.

Resource-ownership checkpoint: the Linux-x64 NativeAOT caller and independent
native peer passed explicit handle creation/claim/read/release, canceled and
retired caller rejection, ambiguous acknowledgement preservation, borrow-delayed
shutdown, retained-resource budgets, monotonic handles, and disposal/logging fault
containment. The 85-project architecture gate remains the dependency check. This
is transport/lifetime validation, not evidence of an extracted production window
module or a new reduction in Host size.

Resource retirement must follow both call-gate and async-operation draining.
Closing the resource registry alongside those drains is too early: an already
admitted synchronous call may not yet have acquired its resource. The independent
peer therefore closes call admission, drains admitted calls and operations, then
retires resources. Its regression holds an admitted call immediately before the
borrow, begins shutdown, and verifies the call can still acquire/read its handle.

### Window value contracts

Window appearance/startup/media/diagnostic DTOs are canonical source contracts
under the backend's `Contracts/` directory. The ordinary backend still defines
the same public types in the same assembly; no public assembly identity moves.
The external Host adapter and native window runtime compile those value sources
locally and exchange generated binary values, never the objects themselves. A
contract-only caller must compile without Avalonia to prove this distinction.
The same development-only generator emits the window profile; runtime reflection
and JSON dispatch remain forbidden. Window schema drift is checked separately
from scene/input schema drift.

A theme already permits 64 MiB of authorized resource content, excluding metadata.
The window control envelope and native call gate therefore allow 72 MiB, while
scene/image limits remain unchanged. This preserves existing theme admission
rather than silently reducing it by the envelope overhead. The call gate's
128 MiB retained-result and active-request budgets still bound concurrency.
Window decoding validates the complete envelope before invoking existing domain
snapshot validation; malformed or oversized candidates cannot mutate appearance.
Memory observation source is nullable because an unobserved/default struct has
no source; this does not imply a measurement has been obtained.

The semantic architecture compiler must not inject test executables/fixtures as
production references. Contract-only native probes intentionally compile local
copies of the same public value names; adding those unrelated test assemblies to
Desktop's semantic model produces ambiguous types and false logging diagnostics.
Test output directories may supply third-party dependency metadata, but test
project assemblies themselves are excluded from production semantic references.
This does not waive any service/presentation or project-reference rule.

Window-value checkpoint: 31 generated shapes passed independent Linux-x64
NativeAOT round trips, including all theme icon slots, startup PNGs/colors/motion,
tray policy, media generation/frame hashes, default and measured diagnostics,
and a full 64 MiB resource candidate with envelope overhead. Both probe dependency
graphs reject Avalonia/backend references and their publications reject CoreCLR.
Malformed schemas, trailing/truncated data, invalid enums, duplicate resource IDs
and over-budget appearance candidates were rejected. The existing 89-shape scene
schema fingerprint remains unchanged. The 85-project architecture gate passed
with production semantic references isolated from test fixtures. This moves value
ownership and encoding only; platform effects and native window startup have not
yet switched to a separately published window module.

The complete 85-project Release solution subsequently built with zero warnings
and errors. Both generated-schema drift checks and all 46 backend regressions
passed after the source-contract extraction.

### Runtime-local dispatcher continuations

The Host owns its synchronization context and continuation delegates. The window
runtime receives only monotonic continuation IDs and posts them to its native
UI dispatcher; a reverse callback consumes each ID at most once. Host operations
that return tasks are explicitly tracked until their actual completion, including
cancellation cleanup, rather than treating caller cancellation as resource drain.
Async-void context operations are tracked through OperationStarted/Completed.
Admission closes before context retirement; continuations for already admitted
work remain admissible until that work and all callbacks finish.

Dispatcher delivery may back off on pre-admission busy responses. An ambiguous
or permanent delivery failure retains the continuation and reports a session
failure; it must neither replay its body nor discard asynchronous finally blocks.
The session owner can switch to a cleanup dispatcher only after the old native
dispatcher and its call gate have stopped. Pending IDs are resubmitted, but a
consumed ID cannot execute twice. All callback bodies remain serialized under the
Host context. The cleanup dispatcher runs Host cleanup only; it does not revive
closed native resources. Context retirement completes only after operations,
queued callbacks, active callbacks and the delivery worker drain. The window
adapter must perform this ordered handover when native shutdown precedes Host
cleanup; the context alone does not prove native dispatcher shutdown.

Dispatcher checkpoint: the Linux-x64 independent NativeAOT peer owns a dedicated
UI dispatcher thread. Host callbacks and their delay/yield continuations resumed
on that same thread with the Host-owned synchronization context installed and
restored afterward. Retirement waited for both Task and async-void finally blocks;
new operations and synchronous Send were rejected. Local fault fixtures passed
three pre-admission busy responses, acknowledgement loss after consumption, and
failed-delivery handover to a cleanup dispatcher without executing an ID twice,
including a throwing diagnostics callback. This is dispatcher protocol validation;
the production window startup still needs to install the context and own the
native-shutdown/cleanup handover.

The dispatcher probe also canceled an already running callback while its async
finally block was suspended, and verified the returned operation did not complete
until cleanup finished on the native UI thread. The 85-project architecture gate
passed after adding the runtime-local dispatcher.

### Window lifecycle adoption order

The real window module needs distinct UI and control call tables in both
directions. UI shutdown retires UI dispatch/render/text calls first; async
completion, diagnostics and main-entry completion remain on the control plane
until Host cleanup finishes. A single gate cannot provide this order because
retiring completion delivery while awaiting Host bootstrap would deadlock exit.
On macOS the native module's main-entry export calls the existing Cocoa main-thread
wrapper; only after that wrapper is installed does its local callback request the
Host's asynchronous bootstrap. No managed bootstrap delegate crosses runtimes.

The Host holds a synchronization-context lifetime operation across composition
and teardown. Incoming UI callbacks execute inline under that context's serialized
scope, while async callbacks use the tracked queue. After native UI shutdown and
UI-gate drain, the owner switches to the cleanup dispatcher; only explicit owner
cleanup operations can then be newly admitted. The lifetime operation is released
only after controller disposal, and context retirement still waits for admitted
continuations. The native runtime similarly owns its async UI context so platform
finally blocks do not depend on a stopped Avalonia synchronization context.
Normal UI admission cannot be reopened through the cleanup API.

Owner-lifetime checkpoint: the independent NativeAOT suite passed serialized
inline/queued callbacks, reentrant inline scopes, context restoration on exception,
cleanup admission after the native-dispatcher handover, continued retirement while
the composition lifetime is held, and rejection of normal UI admission during
cleanup and all admission after retirement. Source formatting verification passed.

The next adoption unit is a real `Nexa.Native.Window` shared library, initially
exercised by an isolated native caller before inclusion in launcher payloads. It
owns Avalonia startup, splash stages, shell preparation/presentation, native text
layout, and GUI shutdown. Host-owned shell/input state remains behind the existing
binary projections. Its instrumentation uses the referenced Core patch runtime,
so exported probes cover the actual backend rather than a disconnected local
catalog. The production Host reference and `ENGINES` list remain unchanged until
the remaining platform facade and end-to-end launcher behavior are implemented.

The real-window contract runner publishes an Avalonia-free NativeAOT caller and
`nexa-window`, paints Host-owned shell content through reverse text calls, and
checks native scene/paint counters before closing. UI and control gates retire
separately; Host cleanup runs only after the native UI stop acknowledgement and
Host UI gate drain. Desktop policy is passed explicitly, so the test can disable
the tray without changing production defaults. The runner also enables an entry
probe on the actual backend `AvaloniaUiStartupSession.ReportStage` target and
requires a hit. Linux uses Xvfb; Windows and both macOS architectures run in CI.
This checkpoint proves native window composition, not production Host adoption
or completion of all platform operations.

Local real-window checkpoint: Linux x64 independently published NativeAOT caller
and library passed actual scene painting, reverse text measurement, orderly close,
and a backend entry-probe hit. The full solution compiled without warnings/errors,
architecture checks passed for 86 projects, and both generated wire schemas retained
their previous fingerprints (89 scene shapes / 31 window value shapes). NTP service
regressions passed 35/35 after separating the built-in/imported fixture identities;
macOS OS-alias assertions remain platform-specific. Other-platform native-window
results are supplied by the new CI matrix, not inferred from the Linux run.
