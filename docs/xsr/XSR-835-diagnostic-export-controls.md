# XSR-835 — Diagnostic export controls

About and Privacy expose the same existing, explicit DiagnosticBundle export.
Privacy retains the independent disk-log facts exporter from XSR-787; the two
archives have distinct descriptions and do not copy raw personal log text.
No new persisted setting, upload or exporter is introduced.

The Settings controller owns one diagnostic-export operation per controller.
Starting it disables all of that controller's diagnostic export controls and
shows a cancel action. About/Privacy navigation preserves the operation and its
busy state. Leaving Settings or disposing the controller cancels the operation;
canceled or retired work cannot announce success. Failure and canceled pickers
release admission for a retry. A second click, including a queued source, cannot
start another operation while the first is still active.

Cancellation sources belong to individual operations and are retired only when
their callback completes. Completion wakes the existing presentation state.
Only the UI frame modifies controls; busy/cancel changes do not rebuild Settings,
replace focused inputs or reset scrolling. Reduced motion uses the same immediate
feedback. Existing DiagnosticBundle limits, redaction and atomic non-overwriting
publication remain authoritative.

Contract tests cover explicit dispatch, cross-page admission, cancellation,
failure/retry, teardown and late completions. This replaces the abandoned
pre-sync XSR-783 privacy draft; that ID now belongs to launch hooks.
