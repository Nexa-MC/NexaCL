# Sidecar

This repository owns `src/sidecar/Nexa.Sidecar.Protocol`, Transport, and XSR Host supervision. Separate repositories maintain the execution engine and SDK. An `.nsc` is a native executable, not an in-process managed DLL.

Host verifies source and signature, creates an authenticated local connection, and supervises the process. Lifecycle phases include handshake, discovery, verification, load, registration, readiness, activation, deactivation, unregister, and shutdown. Registration validates counts, payloads, and deadlines before publishing complete state; failures cannot leave partial registrations or mirrors.

The data plane uses generated numeric IDs, binary frames, and extensible fields. Unknown fields follow skip rules; existing message IDs and field semantics cannot be reused. Command/query completion, state revisions, events, credits, cancellation, and session termination are explicit. Synchronous IPC, JSON hot paths, reflection dispatch, and cross-process CLR objects are prohibited.

Protocol versions and feature bits negotiate separately: shared NXML uses bit 16, AutomaticFunctions 32, and Pages 64. Source UI packages currently use schema 3 while retaining public decoding compatibility. Large registrations use ordered contiguous chunks; verify the complete reassembled digest and budget.

Shared UI renders locally in Host Renderer. Sidecars provide admitted semantic declarations and resources, never direct manipulation of Host UI objects. Resources and function extensions belong to sessions. Shutdown closes admission, completes pending requests, and retires associated resources.

The distributed Market Sidecar comes from pinned inputs with verified signatures and provenance. Host boundaries continue to enforce updates, publisher trust, regional rules, and account eligibility.
