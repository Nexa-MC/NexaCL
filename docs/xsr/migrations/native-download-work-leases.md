# Native download work leases

This extends the network ownership decision before moving download execution into
the native network runtime. Each connection obtains an asynchronous lease from the
existing host scheduler. Lease acquisition is provisional until the owner claims it;
rejected/canceled responses release provisional leases. Disposal sends an asynchronous
release, and module shutdown drains every physical release before retiring callbacks.
No second HTTP, disk or CPU scheduler is created in the native module.

The current download engine reports individual disk writes to the host's learning
feedback interface. Preserve those observations, their order within each writer and
their original byte/elapsed samples. Add an optional asynchronous feedback interface
to Common.Contracts and await it in the existing download disk helper. Existing
synchronous feedback implementations keep their current path. The remote scheduler
uses an asynchronous numeric reverse operation for each sample; this bounds outstanding
feedback by the existing active writers without synchronous IPC or unbounded queues.

The native download request contains value descriptors for HTTP sources, committed
transport policy, trusted-origin and resource-host restrictions, length bounds, file
destination/temporary extension, cache identity, priority and bandwidth policy. The
native owner constructs real connections and writers. Managed development keeps its
custom connection/writer factory API; native production describes known factories
and must reject unsupported descriptors explicitly.

Transfer collections preserve source revisions through a replica. Progress callbacks
stay in their caller runtime, receive bounded binary observations and drain before
their local download call retires. Destination coalescing, independent cancellation,
resume/failover/416 handling, verification, bandwidth generations, atomic file commit
and session-cache behavior remain owned by the existing download implementation.

Production Common keeps a small conditional DownloadService facade. It depends on
the static native network client, whose dependencies remain contracts only, so the
native owner (built without the production facade) keeps an acyclic project graph.
Known local connection/writer adapters describe their configuration on workers.
HTTP registrations preserve default headers, header timeout, redirect behavior and
certificate-resource ownership. Foundation injects one network client and one work
scheduler into the facade; unsupported custom factories remain managed-development
APIs and fail explicitly in native production. Production UI no longer compiles the
download execution/cache implementation.
Native log messages and operation observations use bounded worker queues. Neither
encoding/transport nor caller log sinks run under service locks or on native callback
stacks. Shutdown drains both queues before retiring the endpoint and host logging.

Acceptance: warning-free Release solution build, 95-project architecture gate,
855 service tests, 388 desktop composition tests and 107 release-tooling tests.
The independent AOT network fixture passes against the distributed module. The
complete native launcher passes command listing, headless shell validation and real
window startup/OS closure. Clean rebuilds with changed product/Git metadata retain
identical images: Network SHA-256
`0b747f78a72e083dd1522bc5587627999c1c5d79559fbb262f7a95b5704c42bf`, UI
`fe397d8b6629b55924fda79304918b82b73779d23477f649cf6226cd8609194c`.

## Minecraft work scopes

Minecraft links the same provisional work-lease implementation under its own
protocol identity, as its credited HTTP bridge already does. Catalog prefetch,
recovery and launch use the host's existing scheduler; they do not construct a
second resource budget. Local priority scopes remain local AsyncLocal values.

Launch quiet admission needs an additive asynchronous interface. Preserve the
existing local synchronous APIs, while native launch awaits host quiet admission
before proceeding. Quiet handles use provisional claim, bounded ownership and
asynchronous release/grace operations. Confirmed-window grace and process exit keep
their current semantics; retirement drains pending releases and host timers before
callback tables can close. Never implement the old synchronous method by blocking
on a native request.
