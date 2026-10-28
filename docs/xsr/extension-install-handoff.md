# Extension installation handoff

The Host accepts `nexacl://extension/<PageKey>?argument=<opaque-id>` (ASCII page key and ID, at most 128 characters each). It preserves this bounded activation through the single-instance mailbox and first-run restart. It does not accept executable names, arbitrary commands, URLs, filesystem paths, or credentials in this protocol route.

A live, uniquely named standalone Sidecar page opts in by declaring an entity named `ExternalActivation` with a Sidecar-owned command binding. The Host opens that page and dispatches the ID to that binding. No other commands become externally callable. Pending activations are bounded to 16 and expire after 30 seconds; missing or ambiguous pages fail visibly. Safe mode does not admit Sidecars.

Sidecars own catalogue lookup, authentication, ownership checks, download and digest verification. A user-activated `ui.sidecar.theme.import` button may pass an absolute local package path to the Host. Host imports it through the existing NTP prepare/validate/activate transaction, including capability grants, safe-mode rules and rollback. It never executes package code. There is no automatic import driven by a state update or an external URI. Download completion displays an explicit apply button. Host reports the outcome through its existing feedback surface.

This adds generic Desktop composition behavior, not market/account business logic or a Sidecar wire-format change. Older Host builds do not support these entry points; extensions must pin and test a compatible Host reference.

Validation: protocol parsing rejects command/query injection, paths and URLs;
real single-instance IPC preserves the extension payload. Presentation tests
require an owned opt-in command and prove theme import occurs only on an
explicit user action. The actual Cloud Market binary declarations compile and
navigate in Host; its linux-x64 NativeAOT executable completes real authenticated
bootstrap/registration. Architecture checks pass for all 72 Host projects.
