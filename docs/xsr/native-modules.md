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
