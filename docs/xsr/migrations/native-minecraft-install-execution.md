# Native Minecraft installation execution

Production installation, modpack preview/update, archive identity, local jar and
folder imports execute in Minecraft with the existing installer, task center and
process/recovery state. Callers retain typed adapters. The managed public concrete
constructors and composer overloads remain available in managed builds.

A complete installation port uses existing immutable command/result contracts and
additive archive-identity values. ContentFingerprint is a pure shared value and
moves to Common.Contracts with a forwarder from Common. Install.Contracts references
that contract, never a Common implementation. No JSON nodes or open CLR values
cross runtimes.

Successful install/rename notifications retain order and are acknowledged before
an operation completes, so library refresh and remembered selection follow the
actual transaction. Migration receipt cleanup also executes in the owner. Module
close cancels incoming work, pauses durable installation first, then drains local
imports, task retries, HTTP and callbacks before releasing transport state.

Production installation and folder-import composers now forward to the owner.
Resource modpack identity/preview/update and launcher migration use an additive
installation port; existing concrete managed constructor overloads remain. Custom
catalog injection is shared by the catalog and actual installer, with one-time
admission and asynchronous initialization.

Validation: 202 generated DTO shapes, fingerprint
`75FCF851396B5D8A2F124A32BADA92F76BCED43B979556D21484647C5C02F1BF`;
independent NativeAOT owner/caller with real folder and jar imports, conflict
rejection, modpack content fingerprints, committed updates, stale-preview rejection
and migration receipt cleanup. Successful install/rename notifications are observed
before command completion. Full launch, canonical settings transactions, held-query
shutdown and forty quiet-lease grace periods continue to pass. The 53 focused
managed regressions and 97-project architecture gate pass. Complete production UI
AOT, headless shell and real OS window-close/exit smoke checks pass on Linux x64.

This closes installation execution adoption. Remaining instance management,
resource orchestration, Updates and Host ownership have separate acceptance gates.
