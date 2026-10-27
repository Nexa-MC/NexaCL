# Sidecar scroll observations

The current NFP3 interpreter only transforms typed arguments/results; it has no
call, event-emission or I/O opcode. Existing granted signals cover resource search,
not scroll geometry. Instrumentation therefore cannot deliver scroll observations
to a Sidecar without another host capability.

Add ScrollCommand to a named vertical NXML scroll container. It is admitted like
any other semantic command, and receives offset|viewport|content (invariant finite
decimal numbers). Desktop observes only visible containers on the active Sidecar
page, after layout, and dispatches asynchronously with one pending observation per
container. Identical geometry is suppressed. The renderer has no Sidecar dependency.
No account, market, remote API or list business logic belongs in this integration.
The Sidecar owns retained pages, virtual row projection, append/retry and empty state.
Existing Scroll/Command behavior and the binary protocol remain unchanged.

A second composition-edge intent, ui.sidecar.link.open, accepts a bounded absolute
HTTPS URL without userinfo or control characters. It is handled only on an actual
UI intent from an admitted Sidecar node, never by the automatic scroll observer.
The existing platform browser action opens it. No authentication token is supplied
by the Host, and the user may need a separate browser session. WebAuthn and billing
remain website/platform-owned. NSC owns its canonical destinations; the Host has
no Nexa account/store routes or hardcoded domains.
