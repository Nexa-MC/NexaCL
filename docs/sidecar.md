# Sidecar

This repository owns `src/sidecar/Nexa.Sidecar.Protocol`, Transport, and XSR Host supervision. Separate repositories maintain the execution engine and SDK. An `.nsc` is a native executable, not an in-process managed DLL.

Host verifies source and signature, creates an authenticated local connection, and supervises the process. Lifecycle phases include handshake, discovery, verification, load, registration, readiness, activation, deactivation, unregister, and shutdown. Registration validates counts, payloads, and deadlines before publishing complete state; failures cannot leave partial registrations or mirrors.

The data plane uses generated numeric IDs, binary frames, and extensible fields. Unknown fields follow skip rules; existing message IDs and field semantics cannot be reused. Command/query completion, state revisions, events, credits, cancellation, and session termination are explicit. Synchronous IPC, JSON hot paths, reflection dispatch, and cross-process CLR objects are prohibited.

Protocol versions and feature bits negotiate separately: shared NXML uses bit 16, AutomaticFunctions 32, and Pages 64. Source UI packages currently use schema 3 while retaining public decoding compatibility. Large registrations use ordered contiguous chunks; verify the complete reassembled digest and budget.

Shared UI renders locally in Host Renderer. Sidecars provide admitted semantic declarations and resources, never direct manipulation of Host UI objects. Resources and function extensions belong to sessions. Shutdown closes admission, completes pending requests, and retires associated resources.

The distributed Market Sidecar comes from pinned inputs with verified signatures and provenance. Host boundaries continue to enforce updates, publisher trust, regional rules, and account eligibility.

## Registering an update entry

Send a Resource declaration with semantic ID `nexa.update.registration`, flags and
codec zero, the payload below and its SHA-256 content hash. Use
`SidecarUpdateRegistration.Encode()` from Protocol or equivalent binary TLVs:

| TLV | Type | Value |
| --- | --- | --- |
| 1 | UInt32 | Schema `1` |
| 2 | String | Display name, 1–128 characters |
| 3 | String | Independent runtime version, 1–64 characters |

Control characters and duplicate required fields are invalid. The Host binds the
entry to the authenticated package; it supplies no arbitrary package ID or update
URL. Registration is inert and performs no network access. Old Hosts treat it as
an ordinary cached Resource. Disconnection retires the declaration; recovery must
register it again. The installed-package fallback keeps failed and older Sidecars
visible. Downloads still require the existing pinned-key signed release catalog.

The update center lists launcher and Sidecar versions with status on the right.
**Download and install all** serializes admitted Sidecars before the launcher,
continues after individual failures, and leaves failed entries retryable. Changing
channels discards pending offers and queued work. **Restart to finish updating**
becomes available when all operations finish; it does not restart automatically.

## Reverse Host requests (ABI v0.2 transport migration)

`SidecarFeatures.HostRequests` is negotiated independently of binary payloads.
An admitted Sidecar may then send the existing binary CommandRequest/QueryRequest
frames to routes installed through `SidecarSupervisor.HostRequests`. Route IDs
are explicitly owned by Host; capability registration cannot create a Host route.
The Host supplies package/session identity and checks the route's caller policy,
request kind and codec before invoking its handler.

Requests do not block the receive loop. The session bounds pending calls, and the
shared router bounds actual work across sessions. Cancellation or a deadline does
not release a non-cooperating handler's work slot until it actually completes.
Cancelled commands with an uncertain outcome return `xsr.operation_outcome_unknown`;
callers must use their business idempotency/query contract before retrying. Health
traffic remains available while application work is pending. Retired sessions
cannot issue new work or publish a late result.

No Desktop business or plugin permission route is installed by this migration.
The plugin broker must additionally validate its load permit, context, stage,
capability, target grant and operation identity. A Sidecar session alone is not a
plugin authorization token.

Validation: 188 runtime tests passed, including reverse routing, policy/codec
rejection, negotiation, cancellation, health and shared cross-session backpressure.
The 101-project layout gate passed. Runtime AOT/trim analyzer build passed with
zero warnings; build-time automatic instrumentation was disabled for that analyzer
run because its separate compiler is a managed build tool. This is analyzer
evidence, not a full Desktop NativeAOT publication claim.
