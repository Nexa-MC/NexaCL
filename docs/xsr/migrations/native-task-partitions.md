# Native task partitions

Before moving long-running Minecraft work, preserve one host task collection and
summary while keeping retry, cancellation and durable cleanup with the code that
owns the work. The native Minecraft session owns its task registrations. The host
TaskCenterService imports a bounded, immutable task partition through a numeric
asynchronous port. Each source has a reserved task-ID prefix, a source revision and
one latest snapshot; sources cannot overwrite host tasks or another partition.

The host remains the only writer of the product task collection and summary. It
merges local and imported entries through its existing serialized publication path.
Imports retain their source revisions independently of the aggregate store revision.
Local reports and remote snapshots must not remove each other's entries or produce
a summary over a different collection. Public managed APIs retain their behavior.

Commands for an imported task route asynchronously to its owner: retry, cancel,
dismiss and finished-history cleanup. Retry delegates, cancellation sources and
durable dismissal actions remain local to that owner. No delegate, task handle,
cancellation source or exception crosses the native boundary. The existing state
and intent UI continues to use the canonical task routes and collection.

Closing a native session first stops new commands, cancels and physically drains
install/launch operations and background retries, then drains final task publications
and retires callbacks. Host disposal keeps its account/network/work owners alive
until Minecraft finishes. A completed transport request alone does not establish
that background retries or their file/network resources have stopped.

Acceptance covers merged summaries, independent source ordering, duplicate/stale
imports, task-ID isolation, retry/cancel/durable dismissal and physical shutdown from
independent AOT runtimes. The complete production dependency closure remains the
final ownership gate.

Task IDs are qualified only in the host projection; commands strip the source prefix
before reaching the owner. Existing install/recovery journals retain their local IDs.
Each entry carries its attempt generation and report revision. Cleanup compares
transportable values and these stamps, including step contents; CLR collection
reference equality cannot establish freshness across independent runtimes.

The host implementation now qualifies imported IDs, rejects reserved-prefix local
registrations, coalesces pending snapshots to one per source and merges each partition
through the same collection/summary publisher as local tasks. Transported history
cleanup compares step values and attempt stamps. Existing durable dismissal actions
run in the owner; async controls preserve cancellation results. Background retries
have a separate physical retirement wait rather than treating token cancellation as
completion. Focused task/cache tests cover these behaviors; actual native install
traffic and complete production ownership remain acceptance work.
