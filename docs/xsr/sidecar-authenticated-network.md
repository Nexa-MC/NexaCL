# Restricted authenticated network exchange

A Sidecar can declare query `host.network.exchange.v1`, codec 6, on the existing
binary query plane. No new wire message type or reverse RPC is needed. Desktop
explicitly grants an admitted package one profile: fixed HTTPS URI, POST method,
Host-owned no-redirect mTLS transport. A Sidecar cannot choose a URL, certificate,
headers or arbitrary method. Bearer credentials stay in transient binary calls,
never state mirrors, logs, UI or persistent Host storage.

Request to Sidecar is TLV: 1 Bytes runtime facts, 2 Str previous request UUID
(empty without receipt), 3 U32 HTTP status (0 transport failure), 4 Bytes bounded
HTTP response, 5 U32 Retry-After seconds. Runtime facts are TLV: 1 U32 active
Minecraft process count, 2 U32 observed process-start generation. Desktop reads
actual process state, not launch button presses. Response is empty or TLV: 1 Str
profile, 2 Str UUID, 3 Str bearer, 4 Bytes external HTTP request body. JSON is only
opaque external HTTP content; IPC framing and dispatch remain binary/numeric.

The pump permits one request at a time, caps requests at 4 KiB and responses at
48 KiB, and cancels at disposal. No XP, event types, badge rules or account state
are implemented in Host. The Sidecar orders login/game events, retains event UUIDs
for retry, observes server backoff, and uses server progression responses.
The existing official package verification remains mandatory. Profile grants are
explicit composition configuration, not permissions acquired by registration.

Extension NXML compilation scopes data to referenced parameters (including expanded
components and mixed text). The 256-parameter / 1 MiB per-template budget remains;
unrelated registered Sidecar state no longer exhausts an individual page budget.
The direct dynamic-template API retains its existing full-input budget checks.

## User-triggered clipboard action

Registered extension pages can bind `ui.sidecar.clipboard.copy` to a button. Host
copies its current rendered argument (1–4096 characters, no control characters)
through the existing platform clipboard callback only when that page emits an
active user intent. This is write-only, never a clipboard read or background RPC.
This presentation action is independent of the authenticated network exchange.

Both normal-shell and first-run composition supply the browser and clipboard
callbacks. `--sidecar-ui-preview <export-directory> <output-directory>` in the
Desktop test runner mounts exported binary declarations through the real Host,
renders light/dark at two widths, and injects raw native touch events for copy,
explicit browser navigation and scrolling. Data is synthetic; this test entry
does not alter production package signature checks. The active-page tests also
reject clipboard actions emitted from an inactive page.
