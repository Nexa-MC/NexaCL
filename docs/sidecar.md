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
