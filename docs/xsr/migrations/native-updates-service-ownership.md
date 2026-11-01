# Native Updates service ownership

Updates owns release discovery, rollout policy used by discovery, signed package
verification, portable slot transactions, authorized protected-update coordination,
high-water journals, rollback
and Sidecar update inventory/install flows. Existing codec exports remain available
on the same image. The interactive UI uses typed asynchronous clients;
local platform authorization and restart effects retain explicit ports.

The service image references Updates with external-engine bindings so it never
forms a managed reference back to its own engine project. A dedicated client
references pure contracts, the common asynchronous transport and canonical Network
HTTP/work ports. Managed public APIs remain available in their default build.

Configuration copies installation/build identity, pinned public verification key,
data/cache directories and bounded immutable policy values. Keys do not become
caller-controlled signature bypasses: production uses the existing embedded public
key and the existing verifiers. Private signing keys, certificate provisioning and
public release publication are separate work and are not created by this migration.

UI state, Sidecar progress and telemetry facts cross as generated numeric binary
messages. Retiring an owner stops admission and background rollout work, cancels
and drains active HTTP/verification/transaction operations, then releases callbacks
and module-local resources. Loaded native images remain loaded. Elevated helpers
retain their existing independent protected transaction and OS authorization rules,
including their statically linked verifier and protected macOS helper-root layout.
They do not load application-slot modules. Protected transaction mutation remains
exclusive to that helper; no alternate privileged mutation port is exported.

Acceptance requires actual signed fixture verification, stale/invalid signature and
rollback/high-water rejection, real portable transaction/recovery, cancellation and
physical retirement from an independent NativeAOT caller, plus production UI/helper
packaging, architecture checks and CI for supported platforms.

The existing Updates-owned community announcement service and durable startup/seen
state also remain in this owner. Pure announcement DTOs retain forwarded managed
type identities; UI receives typed snapshots and sends asynchronous acknowledgements.

The bounded transaction journal framing remains a shared contract utility. Its
read-only activation selection runs before service admission so a bootstrap can
select an installed slot without opening the update owner. Transaction mutation
policy remains in Updates; existing managed journal APIs are type-forwarded.

The shared managed codec algorithms have an intentionally separate non-shipped
`Nexa.Native.Updates.Codecs` project. Both the default managed Updates service and
the native image consume it. This removes the reverse service-to-owner project
edge entirely; conditional cycles are not accepted by the architecture gate.
The original native assembly forwards the existing public decoder types.

## Verified production adoption

The production UI references only Updates/Rollouts contracts and the typed native
client. Publication rejects the concrete Updates/Rollouts implementations and the
non-shipped codec project from the UI closure. The owner retains existing codec C
exports while adding the 29-shape generated service schema
`1058A4F4B6C8F760BA7179FA5665DB7A245A22856F7FEC7A072066D8508E78AE`.

Linux x64 owner/caller NativeAOT runs exercised genuine detached signatures,
invalid signatures before payload transfer, corrupted payloads, signed portable
activation, original-baseline rollback, persistent high-water replay rejection and
unknown settings preservation. Separate signed Sidecar catalogs/images covered
truncation, atomic activation, progress replicas, installed-byte revalidation and
repair. A held HTTP operation proved retirement cancels admission but waits for
physical HTTP completion. The caller dependency closure contains no update owner
implementation. Fixture private keys were discarded; checked-in public keys and
inert three-byte archive members allow the same verification on all six RIDs.

The managed compatibility suite passed 38 focused cases, including differential
fallback, offline recovery, every durable commit interruption, signature policy,
rollout cohorts and community acknowledgements. Architecture validation passed for
100 projects with an acyclic project graph. Complete production UI NativeAOT
publication passed dependency isolation, 61-node headless rendering and an actual
Xvfb product-window close with exit code zero. The native-engine CI matrix now
runs the independent Updates suite; elevated runners explicitly assert portable
admission rejection and require ordinary-user runners for mutation cases.

Desktop compatibility validation passed 13 update-related cases, including Sidecar
publication and bootstrap slot selection. The independent privileged helper
published as Linux x64 NativeAOT and passed `--validate-helper`; formatting and
publication tooling checks passed. This preserves the helper's static trust path.

CI's complete automatic-function coverage gate also applies to the non-shipped
codec library. It explicitly uses the shared Core patch ABI and automatic body
instrumentation. Updates is now classified as a service owner, as Accounts,
Network and Minecraft already are; it does not claim standalone-engine isolation
after acquiring the business implementation closure. Service verification keeps
the existing forbidden presentation references and full body coverage checks.

The repaired default Desktop build passed strict automatic coverage for 63
assemblies / 43,838 executable bodies, including all 19 codec algorithm bodies.
The automatic instrumentation self-test passed 51 assertions. No coverage gate
was disabled or exempted.
