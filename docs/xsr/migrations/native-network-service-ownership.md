# Native network service ownership (in progress)

This decision precedes extraction of the complete network/download lifecycle. The
existing `nexa-network` socket/TLS exports remain compatible. The same image gains
versioned asynchronous HTTP, policy, diagnostics and download ports; another shipping
network DLL is not introduced. Its runtime owns `NetworkHttpClientPool`, connection
generations, DNS/PAC decisions, client-certificate policy, response streams and active
downloads. Presentation assemblies and concrete UI composition are forbidden in this
owner. Native publication builds its service dependencies with external engines so
`Nexa.Services.Network` cannot project-reference its own native owner recursively.

The account boundary's 16 MiB buffered HTTP helper is not a general network transport.
General HTTP uses bounded upload/download chunks with backpressure and opaque request
handles. Request creation is provisional until claimed; canceled/rejected replies
retire unclaimed resources. Reads and writes borrow a live handle. Cancellation must
stop the underlying operation before retirement waits for its borrowers, and terminal
cleanup drains streams and callbacks before releasing contexts. No managed streams,
HTTP objects, delegates, tasks, certificates or exceptions cross the boundary. HTTP
payloads remain opaque bytes; transport metadata uses numeric binary contracts.

Every new request receives the committed network-preference snapshot. Existing
connection generations keep their current leases until response disposal. Preserve
provider admission, redirect policy, proxy authentication, DNS-over-HTTPS/IP policy,
request tracing and diagnostic-probe markers. Specialized mTLS pools receive bounded
PFX/certificate configuration and retain the existing Windows UserKeySet, macOS
DefaultKeySet and Linux EphemeralKeySet choices; credential material never enters UI
state. Certificate and handler resources share explicit native lifetime ownership.

Download state becomes a source-revision replica. The native owner keeps destination
coalescing, independent waiter cancellation, resumable temporary files, source
failover, 416 restart, segmentation/integrity checks, bandwidth generations, durable
commit ordering and session caches. Managed development retains the existing custom
connection/writer factory API. Native production turns known connection and writer
configuration into explicit value descriptors and creates the implementations inside
the owning runtime; passing delegates or adding synchronous reverse factory RPC is
not an acceptable substitute. FileDownloadWriter constructors currently only capture
paths, so describing them need not perform UI-thread file IO.

There remains one canonical host work scheduler. Native downloads acquire/release
opaque asynchronous host leases with their captured priority/resource flags; they do
not create an independent scheduler and multiply the host's HTTP/disk/CPU budgets.
The current IWorkScheduler contract already provides asynchronous acquisition. Shared
Common contains both LogService and DownloadService, so separating its download
implementation must avoid a Common/client project cycle. Client contracts depend on
Common.Contracts; managed/native composition differences preserve development APIs.

Acceptance requires independently compiled AOT callers, large streamed bodies,
cancellation during upload/headers/read, early response disposal, resource-budget
recovery, proxy/DNS/mTLS parity, download resume/failover/deduplication, ordered state,
physical shutdown, dependency isolation, full launcher smokes and fresh image
reproducibility. Accounts must retain the committed HTTP policy when its temporary
reverse bridge starts using the extracted native transport.

To keep the source project graph acyclic, the native owner links the current network
implementation sources under its private implementation namespace. Managed development
keeps the existing service project, public APIs and native connector reference. The
native service build uses its connector directly in the same runtime as its HTTP
pools, avoiding a self-loading socket ABI or differently configured service builds
writing to the same managed output directory. The public NetworkConnector and old
socket-transfer exports remain available. No legacy implementation is imported.

`Nexa.NativeEngineAbi` continues to mark the native ABI. A separate
`Nexa.NativeEngineKind` distinguishes standalone engines from service owners which
share the instrumented Core runtime. The coverage verifier validates Network and
Accounts service owners without classifying them as standalone engines. It still
requires their native exports and Core reference, rejects presentation/composition
dependencies and verifies every executable body and function catalog. Unmarked older
ABI-1 engines retain the original standalone dependency rule.
Entry verification selects the runtime namespace from the actual local Patch type,
so static native clients and shared-runtime owners verify their Core calls. Native
probe types remain infrastructure in both modes.

The first implemented stage adds `nexa_network_service_*_v1` admission and asynchronous
streaming HTTP ports to the existing network image. The independent caller references
only its static client and contracts. Uploads and reads use 64 KiB credits, provisional
request handles, explicit claims and physical retirement. Automatic diagnostic probes
now expose a physical drain; disposing their pool cannot dispose its cancellation
source while an older probe is still retiring. Manual probe coordination and bounded
credential-free trace snapshots execute within the network owner.

PFX import uses explicit certificate resources, with a maximum of 32 per client session.
Each certified handler disables redirects. Resources remain owned by that session,
including after an individual handler is disposed. Response-content leases keep their
certificate pools alive until the corresponding HTTP response retires. Closing the
client first drains every handler, then retires imported certificates and the owner.
No exported certificate object or live private-key object enters another runtime.

This stage does not switch production Foundation HTTP composition yet. Download
descriptors, canonical work leases, transfer-state replicas and their managed API
compatibility remain required before the network/download module is complete. The
existing socket/TLS ABI remains in production during that transition. Certificate
import, specialized transport use and retirement are covered by the new AOT caller;
the existing independent socket caller retains the actual mutual-TLS handshake gate.

Local validation covers the 95-project architecture gate, a warning-free Release
solution build, 854 service tests and 107 release-tooling tests. The independent Linux
AOT service fixture uploads and reads 20 MiB, preserves redirected methods/addresses,
queries native traces and probes with disabled providers, imports and retires PFX
identities, and recovers after 33 rejected certificate imports. It cancels upload,
headers and body reads, retires more requests than the resource budget, preserves a
local upload exception and drains an active body read during module shutdown. The
existing eight-engine independent AOT caller still passes socket transfer, mutual TLS,
instrumentation, media, artwork, Minecraft algorithms, XML and compression contracts.
The network image also passes a clean rebuild with different Git/release metadata.
The four-platform native CI now includes the service fixture. Full production HTTP
adoption and the download lifecycle remain open as described above.

## Production HTTP and download adoption

The next stage completes production adoption. Foundation creates one native network
session for HTTP, PFX identities, diagnostics and downloads. Production no longer
references the concrete Network assembly. Common compiles a descriptor facade in
place of its download execution/cache sources; the native owner compiles the existing
engine and uses the canonical host scheduler through asynchronous work leases.
Minecraft, Java, Authlib, modpack and resource adapters preserve origin/host policies,
default headers, header timeouts, resume, size limits and temporary-file extensions.
The managed development APIs retain their existing implementations.

The 29-shape generated schema and independent Linux AOT caller cover real resume and
404 failover, 416 recovery, coalesced destinations with one canceled waiter, verified
cache reuse, bounded lengths/hosts, revision-preserving transfer state, host scheduler
priority/budgets and asynchronous disk feedback. Download logs and operation samples
cross bounded worker queues and drain on shutdown. Existing streaming, cancellation,
certificate and resource-retirement cases also pass against the distributed image.
The complete native payload passes headless shell validation, command listing, and
real X11 window startup followed by OS-requested closure with exit code zero.
The detailed lease and descriptor contract is in `native-download-work-leases.md`.

PAC evaluation has a ten-second overall budget including interpreter setup, helper
initialization and asynchronous DNS replay. Each interpreter execution retains its
two-second timeout, memory, statement and recursion limits. Expiration of the
internal overall budget is a configuration failure; only cancellation requested
by the caller propagates as caller cancellation. This avoids treating cold native
interpreter initialization as user cancellation on macOS runners.
