# Architecture and dependencies

Windows Explorer integration is optional. Its installer may import a dedicated, pinned, non-CA code-signing certificate into LocalMachine/TrustedPeople only after explicit selection. No private key ships. An administrator-owned registry journal records whether Nexa created the trust and which installation roots use it; rollback/removal drops that reference and deletes only Nexa-created trust after the final reference. This trust enables the sparse MSIX locally, not public publisher reputation. The elevated installer owns machine trust; user-context registration owns package identity. See `eng/release/windows.iss`, `windows.wxs`, and the native ShellRegistration helper.

English | [简体中文](architecture.zh-CN.md)

XSR separates execution coordination, business capabilities, and semantic presentation. NativeAOT distributions consist of in-process native modules and independent Sidecar processes.

```text
Bootstrap -> native Host -> UI main thread
                     |-> Accounts / Network / Minecraft / Updates
UI -> state replicas and intents -> typed clients -> native async ports
Services -> Contracts, Domain, Core and explicit platform interfaces
Host <-> Sidecar Protocol / Transport <-> independent extension process
```

Commands request actions; acceptance does not imply completion. Queries return cancellable results. State contains versioned facts, and events represent transient observations. Renderer reads state and emits intents without querying services or launching Sidecars. Controllers do not access concrete service implementations. Event ordering and resource lifetime have separate scopes.

Dependencies point inward. Domain depends on Core; business contracts depend on necessary lower-level contracts and XSR abstractions. Implementations do not depend on Desktop, Avalonia, renderer internals, or service locators. Composition binds implementations and platform ports. Process creation, credentials, probes, and restarts enter through explicit interfaces. Sidecar.Protocol is independent of product Core, Contracts, and XSR; Transport depends only on Protocol.

`eng/architecture/project-references.json` locks permitted direct references. `projects.json` records project locations. Architecture checks validate identities, references, cycles, assembly metadata, and semantic boundaries. Directory changes cannot broaden dependencies to conceal an ownership error; names alone do not establish isolation.

NativeAOT modules exchange versioned C ABI integers, bounded bytes, buffers, and opaque handles. CLR objects, Tasks, Streams, managed delegates, and exceptions cannot cross the boundary. Owners define callbacks, cancellation, completion, release, and shutdown. See [native modules](native-modules.md).

Update architecture documentation before changing a public contract or owner, then update implementation, consumers, and verification together. External extension engines remain in their own repositories, without adjacent-workspace source references.

## Independent plugin Sidecar

### Ownership and release boundary

PluginSidecar owns plugin package admission, SDK negotiation, permissions,
lifecycle, offline restoration, business adapters and management UI. The launcher
must not reference the plugin SDK or contain plugin-specific dispatch routes.
Sidecars consume the same versioned, asynchronous binary Host ABI; missing domain
operations are added as general Host capabilities, not plugin forwarding methods.
The Host remains the authority for its accounts, instances, settings and platform
effects, while PluginSidecar enforces each guest's narrower grants. Account
credentials remain in their owning service and are never exported as generic state.

PluginSidecar is an optional signed distribution component. Its absence, failed
admission, startup failure or later exit cannot prevent the launcher from starting
or operating its core features. Safe mode suppresses extension execution. Release
validation must exercise both component presence and absence. After SDK Beta publication, subsequent guest contract additions use immutable
EXP_IDs. Source cutover does not itself publish Beta or establish production
Market compatibility.

Plugin behavior and offline admission are specified and tested in the independent
[PluginSidecar repository](https://github.com/Nexa-MC/PluginSidecar). Host builds
consume no guest SDK packages. The coverage verifier has no SDK package-name
exemptions; a dependency manifest cannot exempt a product assembly identity. Automatic instrumentation continues to cover
Host-owned source assemblies except rendering; see [instrumentation](instrumentation.md).
The renderer reads state and emits intents. It never calls the extension process.

## Protected Windows update paths

Platform.Runtime owns admission of the update helper's Windows directory chain.
Owners remain restricted to SYSTEM, Administrators and TrustedInstaller. Parsed
conditional allow ACEs are accepted only when their access mask contains known
read/execute rights; this admits the ACEs added by sparse MSIX registration
without trusting their condition. Conditional write grants, unknown rights and unsupported ACE
types remain rejected. Checks retain ancestor handles and never repair permissions.

## Account file upgrades

Accounts owns normalization of persisted profiles. Schema-1 documents written before
offline textures were introduced may omit optional fields or contain null values; JSON
ingress restores declared text defaults and empty textures before service validation. Reading
an encrypted profile does not rewrite its bytes. Oversized or invalid stored values
leave the roster unavailable and preserve the source instead of aborting startup.
Native account failures report type, status and stack through the existing log bridge;
raw exception messages and credentials do not enter diagnostic state. Failed native
retirement keeps callbacks retained and reports its cause once.

## Windows memory cleanup

Common owns scheduling, thresholds, cooldown, and state. Platform.Runtime samples physical memory and applies Windows working-set APIs to individual processes. Composition supplies protected game identities. Desktop uses commands/state in the home widget and does not enumerate or manipulate processes. The implementation is independent of Mem Reduct source and binaries.

Ordinary cleanup never terminates or suspends applications and never invokes global working-set/standby operations that cannot exclude games. Protect registered Minecraft PIDs and descendants, Java/Bedrock, foreground applications, launcher processes, critical system processes, and other sessions. Use creation times and retained handles against PID reuse; skip inaccessible or uncertain identities. Report working-set observations and system availability separately, without equating either to private committed memory released.

Automatic cleanup defaults off. The default threshold is 80%, adjustable from 50–95%, sampled every five seconds. Trigger only above the threshold and enforce a two-minute cooldown. Manual/automatic work is mutually exclusive; shutdown cancels and drains work.

Deep cleanup defaults off and requests UAC for each operation through a separate same-version worker, dispatched before normal UI initialization. A one-time named pipe admits the creating user and local administrators and verifies both PIDs. It accepts only a bounded game-exclusion PID set and returns counts/measurements, never arbitrary executable paths or commands. Game exclusions remain effective after elevation. Parent exit, disconnect, and timeout cancel further work. UAC cancellation is reported as not executed. The UI never elevates or installs a persistent service; the worker exits after cleanup.

## Launcher feature parity additions

The current implementation scope and acceptance cases are tracked in
[launcher-feature-parity.md](launcher-feature-parity.md). Minecraft.Install owns
archive format normalization and per-file optional selection; Minecraft.Management
owns bounded LAN discovery, NBT/content inspection and portable export preparation.
Minecraft.Launch owns executable launch planning. Accounts owns offline identity,
offline texture data and Nide8 authentication facts; Desktop owns explicit user
consent, file pickers, tool views and renderer state projection. Native clients and
Composition expose the same typed contracts in managed and NativeAOT deployments.
New native operations use unused numeric IDs and generated binary DTO codecs; the
private service schema fingerprint changes with the coherent module build. Existing
operation IDs and public Sidecar protocol/SDK contracts remain unchanged.

LAN discovery is a cancellable, bounded multicast listen for Minecraft LAN
advertisements. The server page starts discovery while visible and projects
unique discovered endpoints alongside saved servers without writing them to
`servers.dat`. Generic file/NBT viewer controls, standalone inspection navigation
and server-requirement editing are not exposed in this workspace. Internal
archive/NBT inspection retains its entry, byte, recursion and text budgets.

Instance management admits only macOS system root aliases `/var`, `/tmp`, and `/etc` when Core verifies their exact `/private` targets and target ancestry. Custom directory links remain rejected; content reads and mutations use the same admission rule.

User-selected local file and archive paths resolve existing file/directory links
through Core's bounded path resolver before opening, hashing or preview. Source
revalidation still applies at commit; archive entry and destination link checks
remain strict. Named launch-profile and temporary-override controls are shown
only in instance development mode.

Exports stage output before atomic publication and never include account/session
stores. Launcher-mediated launch scripts refresh identity through the launcher
rather than embedding account tokens. Embedded launcher/runtime selection must be
explicit, bounded and reproducible; failures cannot produce a successful export.

Online modpack inspection uses the existing Resources owner before installation;
the confirmed archive SHA-256 and optional-file IDs travel in typed private
contracts and are rechecked for installation and retry. Offline texture bytes use
bounded binary fields rather than protocol strings. Accounts copies these bytes
on ingress/egress; Minecraft.Launch owns the random-path, loopback-only texture
endpoint, releases it with its runtime, and never redirects online identities to
that endpoint. The generic inspection tool is read-only and the instance launch
link preserves its target through the existing single-instance and first-run
handoffs.

Java policy supports an explicit version-local strategy. `java.runtime` retains its
existing Auto/absolute-path representation and additionally accepts the reserved
Custom value `@instance`; it resolves against the launched version directory, so
global/profile choices remain portable. Minecraft.Java owns bounded local runtime
discovery and compatibility selection through the existing inspection port. This
strategy never scans global runtimes or silently downloads a replacement. Desktop
exposes the strategy as “Instance Java” without showing the stored token. Native DTO
shapes and operation IDs remain unchanged.

## Complete launcher parity (2026-11-04)

The completion checklist is [parity-completion.md](parity-completion.md). Each row
requires a reachable Desktop entry, authoritative service behavior, managed/native
parity and failure/cancellation evidence. Public upstream repositories are behavior
references only; implementation is independent.

Minecraft.Java owns bounded whole-instance discovery and explicit relative/major
preferences; Launch intersects identity-specific Nide8 requirements before probing.
Management owns region NBT decoding, resource-pack options transactions, schematic
facts and script artifacts. Install owns complete normalized modpack recipes,
reviewed launch settings, nested portable archives and server pack updates. Accounts
owns edits to existing offline appearances and their typed private texture sources.
Resources owns verified server downloads and remote document retrieval. Desktop
projects these tools, local/remote home documents and searchable help through XSR.
Remote documents are declarative data, never arbitrary XAML/code execution. Online
sessions own bounded child-process lifetimes and explicit room credentials; provider
installation must verify bytes and expose actual readiness. Native boundaries use
the existing generated numeric/binary mechanisms. Platform coverage is admitted only
after compiler/runtime and native dependency evidence, never by adding untested RIDs.

## Concurrent startup

Distinct canonical instance directories may prepare and start concurrently. Every
attempt owns its cancellation, Java decisions, progress and identity; duplicate
attempts for one instance are rejected. Shared mutable game directories retain their
existing execution/overlay leases. Native clients preserve attempt identifiers.

## Deep Windows system cleanup

Deep Windows cleanup, explicitly enabled and UAC-authorized, adds the eight native
system operations used by Mem Reduct: system working sets, file cache, volume cache flush, modified
pages, standby, low-priority standby, registry reconciliation, and page combining.
This extends the former process-only deep mode. Ordinary mode retains per-process
protection. System operations cannot exclude individual games, so they are skipped
when game/exclusion identities are present, with a live check before each operation.
Report individual native outcomes and measured available-memory changes; unsupported
or denied operations are not counted as successful. Independent implementation uses
OS API contracts, without copying or linking Mem Reduct code. No benchmark parity
claim is made without Windows measurement evidence.

The opt-in pre-launch deep cleanup runs after launch preflight and before JVM
creation, independently of manual/threshold cleanup settings. Host owns UAC and
memory cleanup; the native Minecraft owner calls it through numeric operation 2403
with the launch cancellation token. Concurrent launches share an overlapping cleanup
attempt; canceling a waiting launch does not cancel another launch's cleanup. A UAC
rejection or cleanup failure is recorded and the game may continue; explicit launch
cancellation still stops creation. Other running games retain global-operation guards.

## Unified update center and Sidecar declarations

The Desktop update center projects launcher and Sidecar updates into one list with
per-item status and a shared check/install/restart toolbar. A single UI-owned queue
serializes user-requested installs (Sidecars first, launcher last); a failed item
does not discard the other queued updates. Existing update services retain ownership
of downloads, signature checks, durable activation, rollback and recovery. Restart
is explicit and unavailable while checking or installing. Channel changes retire
queued offers; already-started installations complete on their admitted channel.

A Sidecar proactively declares update presentation during the existing binary
REGISTER transaction: Resource `nexa.update.registration`, flags/codec zero, a
SHA-256-bound TLV payload (1: UInt32 schema = 1, 2: String display name, 3: String
runtime version). Names are 1–128 characters, versions 1–64, without control
characters. Unknown TLVs are ignored; duplicate required fields are rejected.
The resource is backward-compatible with older Hosts and requires no new transport
or registration kind. The Host binds it to the authenticated, signature-admitted
package, never an ID, path, URL or trust key supplied by the Sidecar. Metadata is
session-owned and is retired on disconnect, including across recovery. Quiescent
sessions retain their declaration. Older or failed Sidecars remain visible through
the installed-package inventory. Missing catalogs do not hide registered packages.
Only the existing pinned-key signed release catalog can authorize an update; a
registration cannot supply executable bytes, redirect downloads or grant install
permission. Desktop consumes these declarations without calling Sidecars from the
renderer; the same projection works with managed and native update services.

### Authenticated reverse Sidecar requests

The existing binary CommandRequest/QueryRequest and result frames may flow from an
admitted Sidecar to Host only after negotiating the append-only HostRequests
feature. Frame numbers, binary payload encoding and existing outbound semantics
remain unchanged. Host installs an explicit numeric route table; a Sidecar cannot
register Host routes or select a CLR service. Each call receives Host-derived
package/session identity and is checked against that route's admission policy.
Guest-level permits remain a separate obligation of the owning Sidecar.

The one receive loop dispatches reverse calls asynchronously. Bounded concurrency,
per-route deadlines, cancellation and activation/session retirement apply. Wrong
codec/kind, absent routes and denied identities never invoke handlers. This
mechanism does not grant native execution or authorize a plugin merely because
its parent Sidecar was admitted. Concrete business routes require explicit registration and validation; the
transport itself has no guest ABI or plugin identity model.

NXML owns extension composition and mounting. Generic prepared documents may compose
already-compiled fragments and mount into explicit Dynamic slots; Desktop supplies
owned documents and contribution modes, never constructs presentation IR or mutates
input components. Drawing entities come from external Host templates. This keeps
extension rendering subject to the same presentation ownership checks as product UI.

## Generic Host business contracts

Host service owners expose typed business operations through generated numeric
binary catalogs. Sidecars interpret their own extension contracts locally. The
Host never forwards guest ABI calls or resolves guest capabilities. Accounts,
processes and committed settings remain owned by their native services; public
projections exclude credentials. [The catalog reference](sidecar-host-business.md)
documents operations, discovery fingerprints, ownership and limits.

General Sidecar business DTOs may exceed the legacy 65,535-byte TLV field limit.
The optional `LargeGeneratedDtos` feature (bit 256) permits up to 8 MiB only for
codec 6. Existing message numbers, primitive limits, and fields remain unchanged:
field 6 gives the complete byte length and fields 7 onward contain consecutive
continuations of the existing first bytes field. Both peers must negotiate this
feature before sending or accepting a large value. Missing, extra, or out-of-order
continuations are invalid. This transport extension is independent of frozen
plugin ABI 100; it does not add a guest SDK operation.

### General Host effects catalog

The base business catalog (discovery 255) and its DTO fingerprint remain stable.
An independent effects catalog (discovery 511, operations 512–522) exposes
credential-free account eligibility and refresh, Java inspection/selection and
requirements, power projections, composed preflight reports, and public settings
metadata/validation, process cancellation, and world snapshot listings. Each catalog has its own immutable version and fingerprint;
adding another catalog does not invalidate clients of the base catalog. These
operations are general launcher capabilities and contain no guest or SDK types.
Account refresh resolves a revision-checked profile entirely inside the Host and
returns no identity or token. Capability objects and settings validator delegates
remain inside the Host; only their plain results cross the generated binary ABI.

### Session-owned dynamic presentation

A separate presentation catalog (discovery 767, revision query 768, replace
command 769) lets any admitted Sidecar replace its complete NXML document set
after activation. The caller is the authenticated transport session, never a
package/session identifier supplied in the request. A nonnegative expected
revision prevents concurrent writers from overwriting each other. The initial
revision is zero; each successful replacement increments it once. Cancellation
after publication has an unknown outcome and must be resolved by reading the
revision, not by blindly replaying a mutation.

The existing NXML compiler, page negotiation and source budgets apply unchanged.
All candidate documents compile before one atomic publication; an invalid
candidate leaves the previous document set and revision intact. Replacement
retires old exposures and their command leases. An empty set removes all of the
session's documents. Deactivation removes presentation, reactivation restores the
latest committed set with new exposures, and disconnect retires everything.
No guest SDK models, plugin identities or permission decisions enter this API.

The independent view-state catalog (discovery 1023, operations 1024–1026) extends
this boundary without modifying the presentation catalog. It accepts existing
binary NXML bundles, including source files larger than the generic string codec's
32 KiB limit, and document-local UTF-8 model values. Model updates compile before
publication and retain exposure handles, navigation and control identity. Source
replacement still retires exposures. Both operations share the presentation
revision, so model publishers and source publishers cannot overwrite one another.
Model keys and values are owned copies; a session retains at most 4 MiB of model
text, with 4,096 entries per document and 512 KiB per value. Session mirror values
remain the base; a document's explicit model overrides matching keys only for
that document. Deactivation/reactivation retains the last committed models.

### Session-owned form submissions

The optional `UiForms` feature (bit 512) lets an NXML command registered with
generated DTO codec 6 receive a typed form submission. Scalar commands keep their
existing representation. The generated, fingerprinted binary envelope contains
the action argument and an ordinally sorted list of named UTF-8 input drafts.
It carries no guest SDK types, plugin identifiers or permission decisions.

Desktop captures drafts on its UI thread and only from controls owned by the
invoking document exposure. Nested mounts owned by another exposure, other pages
and Host controls are excluded. Duplicate or unnamed inputs reject the submission;
they are never silently merged. A submission allows 128 fields, 256 characters per
key, 512 KiB per value and a 4 MiB encoded envelope. Large envelopes additionally
require `LargeGeneratedDtos`. Retired exposures cannot submit, and retirement
cancels pending commands. Sidecar owns translation into guest actions and models.

The framework-neutral renderer, NXML compiler and NXML runtime are also shared
packages. Their closure contains Core/XSR contracts and state only; it excludes
Desktop, Avalonia, Host services and the plugin SDK. The compiler package owns the
canonical component and stylesheet files and copies them into `Ui/Components`
and `Ui/Styles` for both build and publish. Sidecar uses this same compiler to
validate guest documents before publication; guest-specific policy remains in
Sidecar. Sharing the compiler does not delegate Host UI access or admission.

### Session-owned page control

The independent page-control catalog (discovery 1279, operations 1280–1282)
queries page visibility and opens or closes ordinary pages published by the
invoking Sidecar. Requests identify a document index and expected presentation
revision, never another session or a global page name. Onboarding remains under
Host control. Queries report actual UI navigation, not a requested visibility.

Desktop schedules these operations on its UI thread through a bounded queue
(64 total, 8 per session). A separate state pulse wakes rendering without
republishing a potentially stale document snapshot. Requests expire after five
seconds; cancellation, presentation retirement and UI detachment free queued
capacity. The UI frame revalidates ownership and revision before acting. Session
and presentation locks protect snapshots only; UI work and cancellation callbacks
run outside these locks. A close affects only the caller's currently visible
page and restores a live fallback. Host services depend on abstract asynchronous
ports; the catalog carries no plugin SDK types. This is a compatible general Host
extension, not a guest ABI change or an SDK Beta release.

### Session-owned confirmation requests

The independent confirmation catalog (discovery 1535, query 1536) accepts a plain
text title and UTF-8 details and returns an explicit user decision. The Host binds
each request to the authenticated Sidecar session, displays that package identity
in a Host-owned title, and supplies its own accept/cancel controls. Guest action
identities, schemas, permissions and confirmation policy remain in Sidecar.

A request cannot replace an existing modal. An unavailable UI or busy modal is a
rejection, not approval. Request cancellation, session retirement, UI detachment
and a two-minute deadline retire the exact dialog and its pending decision. The
feedback service is thread-safe and its renderer consumes snapshots; IPC workers
never mutate UI entities. Supplied text is literal and never localized or parsed
as markup. Details are bounded to 256 KiB UTF-8 and the title to 256 characters.

## General Sidecar shell operations

A separately discovered Shell catalog exposes HTTPS navigation, directory reveal,
and display enumeration to authenticated native Sidecar sessions. Desktop binds
its platform actions to Composition through asynchronous delegates; Composition
never references a renderer or a guest SDK. The binding has a cancellation lifetime,
so UI detachment cancels pending requests and prevents reuse of the old callbacks.
Input validation and session checks precede effects. Successful replies require a
still-live caller and binding; an uncertain result is never automatically retried.
Only HTTPS addresses without user information and existing absolute directory
paths are accepted. Display results are bounded and validated before encoding.
Guest permissions, file grants and confirmation remain owned by the Sidecar.
This catalog does not change existing catalogs or the guest ABI.

## General Sidecar theme options

Theme configuration mutations use a separate ThemeOptions catalog: save, reset,
and import. Desktop remains the owner of validated preparation and coherent
render/style/appearance activation; Composition binds asynchronous operations
without importing a guest SDK. Requests carry the expected current revision.
The Sidecar owns guest permission checks, confirmation, file grants and parsing
of the NTP-options file. Import crosses IPC as typed theme ID, schema version and
option values, never a JSON command. Desktop rechecks the imported header against
the actual prepared candidate before any activation and retires mismatches.
UI binding retirement cancels outstanding work; results are never retried merely
because acknowledgement was lost. Existing theme downgrade, resource admission,
safe-mode and persisted-option rules remain authoritative. Preview activation and
session-owned restoration require a separate lifecycle contract and are not part
of this configuration catalog. Existing catalogs and guest ABI 100 stay frozen.

## Session-owned theme activation and previews

Theme activation uses a separate general Sidecar catalog. Persistent activation
commits through Desktop's theme owner and is not undone on a lost reply. Preview
requests carry a positive, monotonically increasing caller lease ID. The Host
reserves it before invoking the UI and associates it with the authenticated
session's current activation lifetime, independently of its NXML documents.
An end request addresses only that caller's lease. Ending an unseen ID advances
the caller's high-water mark, so a concurrently delayed preview cannot resurrect
an already-ended lease. IDs cannot be reused within an activation.

Cancellation, session retirement and UI-binding detachment retire previews. The
Host waits for an in-flight activation before ending its exact native generation;
a late cleanup cannot revert a later user selection. If a user action supersedes
restoration, Desktop rechecks the exact lease after that action: a failed action
must not strand the old preview, and a successful replacement must remain intact.
Outstanding native operations retain bounded admission slots until their callbacks and compensation complete,
even when the request has already timed out. Cleanup has its own bounded token,
not the cancelled request token, and failures are reported. Application shutdown
cancels admission and bounds draining; Desktop remains responsible for disposing
its native theme resources if the window has already stopped rendering.

The Sidecar retains guest plans, permissions, confirmation, opaque handles and
per-guest cleanup registration. It allocates the wire lease ID before sending a
preview and sends an idempotent end request after an uncertain result. It never
retries activation automatically. Existing guest ABI and Host catalogs remain
unchanged. Production worker composition is a separate migration step.

## General Sidecar notifications

Notifications use a separate general Host catalog. The Host authenticates the
Sidecar activation, validates bounded literal text and one of its three feedback
levels, and submits to the existing thread-safe feedback owner. The Host adds the
verified Sidecar package name; callers cannot replace that source label or request
markup, localization keys, native actions or arbitrary callbacks. A successful
reply means the feedback owner accepted the notification, not that a frame has
already displayed it. Existing expiry, duplicate handling and the 200-entry cap
remain authoritative.

The local notification binding is synchronous and must only submit to the feedback
owner; IPC remains asynchronous. Cancellation, deactivation and binding retirement
are checked around submission. Once submitted, a notification is not rolled back
merely because acknowledgement is lost, and clients never retry automatically.
Guest identity, notification permissions, unsupported action checks and text
translation remain in the Sidecar. Its application awaits the asynchronous adapter
before reporting completion. Existing guest ABI 100 is unchanged; the worker is
not switched until all required application and presentation ports are composed.

## General Sidecar file selection

File selection is a general, independently discovered Host query, with no guest
SDK types. The Host authenticates the current Sidecar activation and opens one
native file, directory or save dialog. Its title includes the verified Sidecar
package name followed by caller-supplied literal text. The request allows a title
of 256 UTF-16 code units and at most 32 simple extension filters. The result is
an absolute local path encoded as strict UTF-8 bytes, or null when the user makes
no selection. A byte field preserves paths exceeding the general codec's 32 KiB
string limit without changing existing wire schemas; paths remain bounded to
32768 UTF-16 code units.

There is one outstanding Sidecar native picker across all sessions and bindings,
with no waiting queue. Its slot stays occupied until the native operation actually
returns, even if the requesting session exits, the UI binding is replaced or the
two-minute request deadline expires. Cancellation returns promptly but cannot
necessarily dismiss the OS dialog. A late selection is discarded; it never grants
file access. Native completion, including a late exception, is always observed.
The new UI binding cannot bypass an old dialog's admission slot.

Plugin identity, permissions, opaque file grants and interpreting cancellation
remain in the Sidecar. It awaits the Host response and rechecks the invocation's
cancellation before granting a path. It does not retry the picker automatically.
The guest ABI stays unchanged. Worker composition remains a separate cutover.

## General Sidecar clipboard transfers

Clipboard access uses an independent, generated Host catalog over the existing
bidirectional request channel. It requires `LargeGeneratedDtos`; the existing
stream API opens streams in the opposite direction and is not repurposed here.
Text remains bounded to 65536 UTF-16 code units and travels as strict UTF-8 bytes.
PNG data retains its 16 MiB encoded limit, 4096-pixel dimension limits and four
million pixel budget. Pictures cross the boundary in chunks of at most 256 KiB,
with declared lengths, exact append offsets and a SHA-256 digest. A single frame
never needs to contain the whole image.

Each authenticated activation owns a monotonic positive transfer ID sequence.
The Sidecar allocates an ID before sending a begin request and serializes begin
requests. Releasing an unknown ID advances the owner's high-water mark: a delayed
begin cannot recreate a transfer after its caller has abandoned it. That mark
survives native UI rebinding and retires with the activation. Releases affect only
the requesting activation; repeated release is safe. Read chunks are repeatable,
whereas appends must use the next exact offset and commits are single-use.

Admission permits eight transfers globally, two per activation and 64 MiB of
reserved transfer storage, with at most one native clipboard operation at a time.
A two-minute transfer lifetime releases abandoned storage even when no subsequent
request arrives. Cancellation, session retirement and UI detachment also retire
transfers. An operation that is still preparing a buffer or awaiting a native
callback retains its reservation until that work actually completes; cancelling
the caller cannot evade memory or native-operation admission. Native callbacks
and cancellation run outside the transfer lock.

Read operations expose an immutable snapshot after capture. Write operations
stage bytes privately and check completeness, digest and PNG bounds before calling
the native owner once. Null text differs from empty text; null text and absent
image mean clear the clipboard. A cancelled or lost write acknowledgement does
not prove the native effect was rolled back. Neither retries nor clipboard
restoration happen automatically, since another application may have changed it.

The Sidecar owns guest confirmation, permissions and blob handles. It rechecks the
guest invocation before consuming a result and releases known transfer IDs using
an independent, bounded cleanup token, including after an uncertain begin reply.
Cleanup failures are reported; Host expiry remains the final resource bound.
Existing guest ABI 100 and the older Host catalogs are unchanged.


### General Sidecar runtime context and readiness

The Context catalog (discovery 3327, snapshot query 3328) exposes the launcher
version, its absolute data root as strict UTF-8 bytes, and two independent facts:
business startup phase and native window availability. It contains no plugin
paths, permissions, activation policy or guest ABI types. An authenticated native
Sidecar may use the root for its own compatibility layout; this is not a grant
of filesystem access to a guest. The Sidecar must enforce guest storage policy.

Business startup advances from Composing to Ready only after normal startup
preparation completes. Stopping is terminal. Native-window readiness is false
while the shell is being prepared invisibly, before native attachment, and after
closure; it does not depend on foreground focus or tray visibility. A context
snapshot is advisory and cannot reserve future availability. Sidecars still
handle unavailable/cancelled operations and retire restoration with activation.
Headless shell validation never advertises Ready. Shutdown marks Stopping before
disposing controllers. A readiness query does not wait for Sidecars to finish
restoring: activation must complete independently to avoid a startup cycle.

Clients poll the cheap read-only snapshot with bounded, cancellable backoff until
the capabilities they need are ready. The Host does not schedule plugin recovery.
The production plugin restoration owner remains unchanged until Worker migration
is complete. This separate catalog preserves all existing Host fingerprints and
the frozen guest ABI 100.


### Presentation acknowledgement and observable snapshots

A successful Sidecar presentation replacement commits its session revision and
lease ownership before returning. `CapturePresentation` and command admission
read that authoritative ownership immediately, so retired exposures cannot keep
accepting input. The shared `XsrNxmlModuleSnapshot` is a coalesced UI projection:
when another publisher is already draining it, a committed mutation may return
before observers receive the new immutable snapshot. Readers must wait for the
expected projection instead of treating acknowledgement as an observer-drain
fence. Publication and cancellation callbacks remain outside ownership locks.

### General Sidecar modal documents

Modal documents are an opt-in extension of the existing NXML source bundle:
`Dialog = 9` retains schema 3 and requires the negotiated `Dialogs = 1024`
feature. Existing document values and encodings remain unchanged. Dialog roots
must be matching-key Pages, with unique standalone targets within a session.
They do not enter ordinary navigation or onboarding. The separate DialogControl
catalog uses discovery 3583, state query 3584 and open/close commands 3585/3586;
existing catalog fingerprints remain unchanged.

Commands address a document index and expected presentation revision in the
authenticated invoking session. The Host queues UI work with cancellation,
session/source retirement and a five-second deadline, bounded to 64 requests
globally and eight per session. It checks ownership and revision again on the UI
thread. At most 16 modal documents may be open globally and eight per session.
State queries report which of that session's modal documents are open; no CLR
view, guest identity, permission or plugin SDK object crosses the channel.

The Host owns modal chrome and its close control. It labels the source using the
authenticated Sidecar package name, keeps Host confirmations above Sidecar
content, and refuses to open a Sidecar modal over Host-owned modal UI or onboarding.
Input is accepted only from the current modal layer. Document-local forms retain
their existing exposure boundaries. Model updates preserve the open document and
drafts; source replacement, session retirement and UI detach dismiss its modal
and cancel queued work. Sidecars observe native closure through the state query
and decide their own application lifecycle. The Host never adapts a plugin dialog.

This additive shared-contract change advances the shared package set beyond
`0.1.0-alpha.17`. Consumers remain pinned to their existing immutable source until
the new contract, Desktop behavior and clean package feed have been verified.

Modal admission and submission lifetimes follow the presentation ancestry,
including detached component roots mounted by `NxmlPresentationCoordinator`.
The coordinator exposes `GetPresentationParent` without relying on a previous
render frame: append/prepend and replacement content lead to their mount target;
before/after content leads to the target parent. Physical document ownership
continues to delimit form fields. This prevents closed modal components from
submitting and binds their pending requests to the enclosing modal lifetime.

### Retaining documents across a source-set update

A whole-set replacement remains an explicit retirement boundary: existing
`ViewState.Replace` retires every exposure. A separate, opt-in `ViewReconcile`
catalog (discovery 3839, command 3840) adds document retention for Sidecars that
negotiate `RetainedDocuments = 2048`. It introduces no guest-specific identity.
Shared packages advance to alpha 19; existing catalog fingerprints do not change.

A reconciliation supplies an expected session revision and an ordered list of
candidates. Each candidate either retains a unique index from the previous set
without supplying source bytes, or supplies a new source with previous index -1.
Every candidate supplies its complete document-local model. The Host resolves
retained sources from the authenticated session, validates the complete source
set and models, and compiles it before an atomic commit. Reordering retained
entries changes their indices but preserves their exposure identities. Existing
source/model budgets, capability checks and standalone-target uniqueness apply.

The source-set lease and each document have separate cancellation lifetimes.
Any source-set mutation retires old control snapshots, so queued requests cannot
reuse stale indices. Only removed or replaced documents lose their action
lifetimes; retained documents keep pending submissions, input drafts, navigation
identity and open modal state. Retirement callbacks and snapshot publication run
outside ownership locks. A rejected candidate leaves the prior set intact.
Session shutdown and the original whole-set replacement still retire everything.

A Sidecar must serialize reconciliation with its other presentation operations.
The Host does not reopen any modal during reconciliation. This distinction lets
independent windows survive additions elsewhere without reopening a window that
the user has closed. Plugin view references, opaque action tokens and decisions
about guest lifecycle events remain private to PluginSidecar.

### Session-owned keyboard shortcuts

The independent ShortcutControl catalog (discovery 4095, command 4096) replaces
only the invoking Sidecar's shortcut set at an expected presentation revision.
Each binding names an owned ordinary page or mounted document, a bounded gesture,
a registered GeneratedDto command and an opaque argument. The Host resolves the
current document exposure before acknowledging a complete, atomic set. It never
receives guest command IDs, permissions or plugin scopes. Shared packages advance
to alpha 20; existing catalogs and negotiated feature bits remain unchanged.

Shortcut callbacks use the existing UiForms envelope with no form fields. The
runtime adds a distinct dispatch path for session-registered GeneratedDto callbacks
bound to a live document; it cannot call Host commands. Ordinary markup dispatch
continues requiring a command declaration in that markup. Removing a document
cancels its pending callbacks; retained sources keep their bindings across model
updates, additions and reordering. Detaching presentation removes all bindings.

Host keyboard handling runs first. Shortcuts apply only to visible content under
the current ordinary page and are suppressed during onboarding, modal UI or text
entry. Gesture matching is ordinal case-insensitive against the platform gesture
text, with surrounding whitespace removed. Conflicts are resolved by Sidecar
package name, session ID and then binding order. A running binding consumes key
repeats without invoking another callback. Limits are 64 bindings per session,
512 per UI attachment, 16 concurrent callbacks and the existing ten-second action
deadline. Registration queues retain the existing 64-global/8-session/five-second
bounds. Guest action validation and exact registration retirement stay in Sidecar.

## Generic raster presentation for native Sidecars

Native Sidecars may upload bounded PNG resources and bind them to their own
published NXML Dynamic slots. The independent RasterControl catalog uses discovery
4351 and operations 4352–4356 for begin, append, seal, complete-set replacement and
release. It uses generated binary DTOs and the authenticated session identity.
The Host receives pixels, document indices, scoped keys, logical sizes and a
registered callback. It never receives guest drawing commands, SDK handles or
plugin permissions.

An upload is limited to 16 MiB; chunks are sequential and at most 64 KiB. SHA-256,
PNG validity and pixel dimensions are checked before a resource becomes ready.
The attachment holds at most 16 resources/64 MiB per session and 64 resources/
128 MiB globally, including unfinished uploads. Unbound resources expire after
30 seconds. Session loss and UI detachment release resources independently of
incoming traffic. Bound resources cannot be explicitly released until removed
from the binding set.

A complete-set replacement must name the current presentation revision and owned
document indices. It binds at most eight surfaces per session and 64 per UI
attachment, with pixel budgets of eight million per session and 32 million per
attachment. Logical dimensions are finite and within 1–2048; their rounded-up
pixel dimensions must match the resource. Each destination is one unique owned
Dynamic slot. Validation completes before the new set becomes visible. A rejected
set preserves the previous set. Model updates and retained sources preserve
bindings; removed exposures retire their resources and pending input.

The Host creates and owns the raster nodes. Input is forwarded only for the
visible, enabled node within the current page or topmost modal. It uses an
own-session generated-DTO callback with bounded native observation fields, never
page form drafts. Each surface permits one pending callback and one coalesced
latest observation; a gap marker reports discarded intermediate observations.
Document, dialog and attachment lifetimes cancel pending calls. The existing
empty-field shortcut callback remains compatible. This adds no guest ABI or SDK
experiment ID.

## Session-owned Sidecar HTTP exchanges

The Host exposes generic, generated binary HTTP exchanges to authenticated
Sidecars. It owns the configured HTTP transport, including committed proxy/PAC,
DNS/IP and provider policy. The plugin Sidecar retains guest permissions,
confirmation, credentials supplied by a guest, quotas and plugin identities.
Neither the transport nor its catalog references the plugin SDK.

Each session owns its upload/response IDs. Requests upload bounded bodies in
chunks, send once, read responses sequentially and release explicitly. Uncertain
send/read results are never replayed automatically. Response bodies are streamed;
large downloads do not become large IPC frames or whole-response allocations.
Idle expiry and session retirement cancel outstanding work. Retiring exchanges
remain charged against quotas until their operations and resource cleanup drain.
HTTP headers and bodies are excluded from diagnostic messages. Discovery requires
HostRequests and BinaryPayloads, independently of native UI readiness.

## Plugin ownership after the standalone cutover

The Host contains no plugin kernel, package loader, guest ABI adapter or plugin
UI controller. It has no build or runtime reference to `Nexa.Plugin.*` or
`PluginSidecar.*` assemblies. The independent Plugin Sidecar owns guest admission,
permissions, business translation, storage, offline restoration and presentation.
It registers its navigation through the same generic NXML extension mechanism
as every other Sidecar. The Host has no fixed plugin page or package picker.

Missing, disabled or failed Sidecars must not prevent ordinary Host composition.
Safe mode disables Sidecar activation at the generic session boundary. The Host
exposes only authenticated, versioned generic business and presentation catalogs,
including configured HTTP; no guest SDK value crosses those boundaries. Old
embedded-plugin integration tests retire with the removed owner. Sidecar-owned
tests exercise guest behavior, while native Host tests retain generic contracts,
resource ownership, UI routing and retirement coverage.

### Independently built optional Sidecar distribution

The release pipeline can bundle multiple independently built, reviewed Sidecar
images. Each image is pinned by source commit, build run, RID, size and SHA-256,
then signed with the existing publisher key. PluginSidecar remains an optional
process: its SDK, native libraries and UI assets never enter the Host project
graph. Ordinary Host builds do not fetch it. Release catalogs describe the same
package set for all six RIDs. macOS sealing preserves every signed Sidecar image
byte-for-byte. A new component is enabled only after its complete six-RID input
lock is reviewed; an absent PluginSidecar lock does not fabricate an artifact.
