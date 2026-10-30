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
