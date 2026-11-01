# Host composition and service retirement

Host owns admission and lifecycle of Accounts, Network, Minecraft and Updates
sessions. A generated fixed catalog pins numeric module IDs, lowercase image
names, complete service schemas and entry points. Host has no project references
to service or presentation implementations. UI-local typed adapters retain their
own state replicas and host resource callbacks; there is one business owner for
each service, and the existing canonical settings/work ports are not duplicated.

Production UI enters with Host admission required. Each typed client registers its
unmanaged peer table and a bounded retirement poll callback before receiving the
owner's call table. Only IDs, UTF-8 identity bytes, opaque leases and unmanaged
function pointers cross this cold admission ABI. Business commands, queries,
streams and state retain their existing asynchronous numeric protocols. Managed
development and independent owner tests keep their direct admission path.

Host leases are distinct from module-local contexts, which can overlap between
independent runtimes. Closing an owner does not release its Host lease. The client
must also physically drain its local HTTP/work/state callbacks before acknowledging
completion. The client retirement callback queues work and returns a poll status;
it never blocks admission on business work. Images remain loaded.

On UI entry return, Host stops new admission and retires remaining clients in
dependency order, with Network last. This includes the process-local default HTTP
client used during first-run and Sidecar discovery, which previously outlived the
UI's explicit scopes. Host joins retirement only after the UI dispatcher has
exited. Failure retains handles and returns a failed process exit; it never claims
quiescence or unloads an image. Cocoa entry still executes on the original main
thread.

The UI identity becomes `nexa-ui/3/<catalog fingerprint>` while retaining the v2
entry argument layout. Old Host/UI pairs fail identity admission before entering
the new UI. Complete signed payloads continue updating matching modules together.
Acceptance requires independent AOT admission, schema/missing-image rejection,
opaque-context separation, held-HTTP physical retirement, production UI startup
and shutdown, architecture isolation and complete-payload delta reconstruction.

## Independent runtime and production evidence

The generated catalog fingerprint is
`ADEB028D03887D9AB0AA1AB926B24031EAFC94D736630D0DA3912F282F8F67B7`.
The publisher checks the catalog against all four current generated schemas before
building a payload. The native-engine CI matrix runs the independent Host caller.

Linux x64 independent Host/caller AOT runs passed actual typed Network/Updates
admission, deliberately overlapping owner-local contexts with distinct Host
leases, schema/unknown-module rejection, premature retirement acknowledgement
rejection and physical shutdown while HTTP completion was held. Network remained
admitted until its dependent client retired. New admission failed after shutdown.
Separate processes rejected a missing Host, a missing Network image and a loadable
Crypto image substituted for Network; none fell back to direct service admission.

Production UI AOT passed the Host/UI identity check, foreign-working-directory and
missing-module smokes, 61-node headless projection and a real Xvfb product-window
close with exit code zero. The Host retained no service/presentation project
references; the UI publication gate continued rejecting Accounts/Network/Updates/
Rollouts/Minecraft/Resources implementations and heavy engine dependencies.

A CI coverage failure in the preceding Updates extraction identified the new codec
library's missing automatic instrumentation. The codec and Updates owner now use
the shared Core patch ABI. The existing strict coverage verifier passed all 63
default UI assemblies and 43,838 executable bodies; its 51 automatic instrumentation
assertions passed. Service classification adds Updates' existing export prefix
without exempting any body or allowing a presentation dependency.

The rebuilt Updates owner's managed AOT input closure independently passed strict
coverage for 22 assemblies / 9,432 executable bodies, classified as service owners
rather than standalone engines. Its signed-update and Sidecar suites passed again
after this instrumentation correction.

Complete real runtime payloads of 197,215,470 bytes passed bootstrap/module smokes
and delta reconstruction after a private Updates timeout change. The compressed
delta was 610,504 bytes; all ten unchanged module hashes matched and contributed
zero new data bytes. Per-image hashes, sizes and new bytes are recorded in
[the complete-payload evidence](../evidence/2026-11-01-native-split/measurement.json).
The experimental source edit was restored and no payload was published.

The final independent native engine caller also passed all eight images' automatic
instrumentation probes, gzip/zstd streaming and fragmented input, VCDIFF, network
exception identity, cancellation/disposal, resource retirement and mTLS. Existing
media, artwork, Minecraft metadata and bounded markup/XML compatibility passed.

Final architecture validation passed for all 101 projects with the intended
reference graph. Generated Host catalog checks, all seven native packaging/delta
tooling tests and complete-solution formatting verification passed. The final
production UI and independent privileged helper both published as NativeAOT; the
helper retained its static trust path and passed its validation entry.
