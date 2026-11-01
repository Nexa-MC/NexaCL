# Native window-owned asynchronous effects

The typed native window client now carries local media playback and validation,
system media controls, screenshot crop, world-session locks, notification actions
and native document subscriptions. The native module retains every decoder,
platform object and descriptor. Handles, copied immutable values and bounded binary
messages cross the boundary; no CLR players, callbacks, tasks or exceptions do.

Async resource retirement rejects new borrows and waits for admitted operations,
callbacks and physical disposal. Provisional results rejected by cancellation are
returned to their owner. Shutdown retains disposal failures. System media controls
receive state/volume/enabled snapshots, so native OS queries do not synchronously
pull Host state. Callback-triggered self-release remains asynchronous.

Document unsubscription uses an atomic consumer comparison and a native callback
lease. A real close test exposed a deadlock when retirement tried to post to an
already exited dispatcher; detachment now completes without such a post. The
notification fixture is supplied before process entry because independent runtimes
do not share their managed environment caches. An activation queued by the Linux
adapter is drained before its callback lease retires.

Linux x64 verification used independent NativeAOT caller, peer and window images
with no CoreCLR or embedded Avalonia backend in the typed caller. Headless and real
Xvfb window probes passed, including crop, media isolation and self-release, system
media snapshots, notification activation, subscription replacement, active
subscription shutdown, input and theme ownership, painting and text measurement.
The same window CI matrix additionally exercises macOS world-lock release/reacquire.
Cross-platform CI remains the evidence for those platform-specific implementations.
The Release solution build completed with no warnings or errors; architecture
validation covered 89 projects, and the 47 backend and 388 Desktop tests passed.

This closes the remaining platform-effect contracts. Desktop production adoption,
UI tree/controller ownership and the account/network/Minecraft/update service
lifecycle boundaries remain separate outstanding parts of the complete module split.
