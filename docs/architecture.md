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

## Plugin Desktop integration

### Beta boundary (migration target)

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
validation must exercise both component presence and absence. The ABI 100 Beta
baseline is frozen; subsequent plugin contract additions use immutable EXP_IDs.
This boundary supersedes the in-process ownership described below; the existing
implementation must be migrated and verified before the Beta is published.

Desktop owns plugin admission, capability grants, atomic lifecycle publication,
business adapters and presentation state. The independent PluginSidecar executes
verified import-free Wasm guests. Host/guest continuations use the existing
authenticated Sidecar binary command channel, with bounded chunk transfers for
larger payloads; no JSON data plane or new transport message IDs are introduced.
Every yielded business call re-enters the admitted Host capability context.
Transport loss seals the guest instead of replaying an uncertain operation.

Plugin SDK libraries are consumed from an immutable, pinned external package feed.
Their implementation sources are never added to the Host build graph. Automatic
instrumentation covers Host-owned source assemblies. The four explicitly named
`Nexa.Plugin` SDK assemblies are external only when the emitted dependency manifest
identifies them as NuGet packages; the prefix alone never exempts an assembly. Services
publish plugin presentation state; Desktop controllers translate user intents to
authorized actions. The renderer does not call the extension process. Plugin
management/recovery remains Host-owned and safe mode disables plugin execution.
Local developer packages require explicit user admission; the production Market
source remains responsible for publisher and entitlement verification.

### Offline plugin admission and presentation continuation

Composition owns a bounded, encrypted installation catalog separate from plugin-owned
storage. Only explicitly approved package bytes and the exact approved grants may be
saved. A per-record AES-GCM key is wrapped by the existing current-user OS protector;
plaintext packages and grants are never staged on disk. Restore revalidates packages
and grants, waits for an authenticated worker, and obtains fresh load identities.
Developer packages restore only while developer mode is enabled; safe mode never
restores. Explicit disable deletes saved admission before stopping execution. Worker
loss retires active guests without replaying pending business calls. Corrupt records
are isolated and reported; they are not deleted or treated as trusted installs.

Advanced UI registrations remain atomic and owned by the admitted load. Protected
Host controls cannot be replaced. Platform interactions are supplied by Desktop ports;
services publish data and never access the renderer directly. Business adapters use
existing typed commands and public projections with load-scoped opaque handles.

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

### Plugin ABI v0.2: authenticated reverse Sidecar requests

The existing binary CommandRequest/QueryRequest and result frames may flow from an
admitted Sidecar to Host only after negotiating the append-only HostRequests
feature. Frame numbers, binary payload encoding and existing outbound semantics
remain unchanged. Host installs an explicit numeric route table; a Sidecar cannot
register Host routes or select a CLR service. Each call receives Host-derived
package/session identity and is checked against that route's admission policy.
Plugin-level permits remain a separate broker obligation inside those routes.

The one receive loop dispatches reverse calls asynchronously. Bounded concurrency,
per-route deadlines, cancellation and activation/session retirement apply. Wrong
codec/kind, absent routes and denied identities never invoke handlers. This
mechanism does not grant native execution or authorize a plugin merely because
its parent Sidecar was admitted. It supplies transport plumbing for the plugin
broker; concrete business routes require explicit registration and validation.

NXML owns extension composition and mounting. Generic prepared documents may compose
already-compiled fragments and mount into explicit Dynamic slots; Desktop supplies
owned documents and contribution modes, never constructs presentation IR or mutates
input components. Drawing entities come from external Host templates. This keeps
extension rendering subject to the same presentation ownership checks as product UI.

### Plugin application service adapters

ABI 100 business handlers are explicit typed adapters over the existing command and
query routers. Application resources (files, plans, subscriptions and opaque IDs)
are bound to the admitted load, retired on stop, bounded, and never reconstructed
from guest paths. User-selected files are granted by a native picker; package files
remain read-only. Mutations retain owner-service revision checks and Host-owned
confirmation. A guest confirmation string is not authorization. Desktop supplies
platform effects through explicit callbacks; service composition remains independent
of renderer and platform UI types. Developer mode is projected from the effective
settings query, refreshed by the policy revision cell, not an invented state key.

The Minecraft owner appends binary Java selection operation 69 (nullable instance
directory -> nullable runtime candidate) for plugin selection. Both managed and
native execution use the existing Java selection service; instance-local discovery
and executable probes stay in the Minecraft owner. Existing numeric operations
and codecs retain their wire identities.

Native window command 45 carries the bounded extension file picker request
(title, file/directory/save kind, extension filters). Both window backends use the
same native picker implementation. No guest-supplied filesystem path is accepted
as picker authorization.

Plugin world/content adapters project the existing instance-management snapshots
into load-owned IDs. A retained content identity contains only an instance ID,
page and entry name; every read resolves it again through the authoritative owner.
Mutations preserve the owner's metadata revision and session-lock checks. World
metadata is an explicit scalar projection, never an unrestricted NBT or path API.
Plans retain the exact reviewed source revision and are retired with their load.

World editing adds two append-only management messages: 237 renames the level's
public display name, and 238 atomically sets the enabled data-pack order. Both
execute under the existing recovery/session gate with the expected level.dat
revision and read-only-world policy. They never implement order as a sequence of
individually committed enable/disable operations. Their generated binary shapes
are shared by managed and native Minecraft adapters.

Window platform operations 46–48 add bounded clipboard read/write (UTF-8 text
plus PNG, including explicit clear) and display enumeration. Clipboard access
remains a Host-confirmed plugin operation. The window process owns UI-thread and
OS clipboard access; the composition layer receives only text/bytes and numeric
display facts. These asynchronous numeric messages do not expose UI objects.

Minecraft process operations 25 (interaction) and 26 (metrics) resolve a retained
session inside the process owner. No plugin supplies an OS PID. Close requests
and window reveal use the retained process object and platform window adapter;
unsupported platforms return unavailable. Metrics sample the retained process
across a bounded interval and normalize CPU usage to the machine processor count.
The managed and native command/query routes share these semantics.

Plugin Catalog reads delegate to the existing resource owner and its source
policy/cache. Public provider IDs are nexa:modrinth and nexa:curseforge; kinds use
nexa:resource/<kind> and loaders nexa:loader/<loader>. Search cursors bind the
normalized query, source page and page revision. A catalog file's SHA-256 string
is empty when the upstream does not publish that digest; it is never fabricated
from SHA-1/SHA-512. Installation continues to use the owner's published hashes.
Unpinned dependency version IDs are empty, preserving the upstream distinction.

Resource favorites now expose an owner revision, and the favorite mutation carries
an optional expected revision. The resource owner's existing mutex compares it
before persistence, so a plugin's reviewed change cannot overwrite a concurrent
Desktop change. Native resource DTOs append these fields and retain strict schema
fingerprint negotiation; existing UI callers may omit the optional comparison.

Plugin account appearance calls use the existing account runtime query/command routers. A wardrobe
identity is captured for the requested public account and must match the currently selected account;
no plugin request silently changes selection. Uploads and cape changes retain the account owner's
roster/selection generation checks. Skin preview uses a bounded owned blob and a private temporary
file passed to the existing validator, then removes that file. Account credentials never cross the
plugin ABI. Dependency-graph projections retain missing and runtime dependencies as explicit nodes,
and incomplete graphs remain unavailable rather than being reported as complete.

Plugin launch grants bind a public account roster revision and an instance to one admitted load.
The revision is checked by the account owner while holding its roster lock, when launch obtains
its privileged profile. A composition-side precheck alone is insufficient. Trusted launch access
adds an explicit revision-checked overload; implementations without that capability reject it.
Existing UI launches retain their existing overload. Native account operation 202, Minecraft host
callback 2302, and Minecraft launch operation 106 carry bounded numeric revision fields. The
revision is retained by the per-attempt coordinator, including concurrent launches.
Launch-profile projections use the policy reader's explicit profile selection, without changing
persistent selection. Auto memory is represented by zero and automatic Java by a null runtime.
Profiles containing values changed by the Host's secret redactor are not exported. Writes retain
unrepresented settings/overlays and use the policy owner's revision compare-and-swap. Authorized
launch carries the captured settings revision and explicit profile to the per-attempt owner; it
rejects changed settings before preparing the process. Temporary layers still follow owner policy.
A launch operation is recorded in the SDK task ledger. The launch adapter delegates read/cancel to
the existing task handlers only after checking that the operation belongs to its retained launch
record and the admitted load. This permits launch-specific read/cancel permissions without granting
arbitrary task access. Launch grants expire after ten minutes, are consumed once, and are retired
with the load. Their public form contains no provider credentials. Eligibility reports local
readiness; online session validation remains an explicit launch-stage operation.
Plugin sign-in owns its onboarding generation. Start accepts an optional expected generation and
rejects busy/replaced onboarding under the service lock. The default UI start behavior is unchanged.
Microsoft and LittleSkin use the existing device-code flow. Offline/third-party form responses use
ABI NamedValue with name `credentials` and a closed map (`username`, `password`, `server`); character
selection uses NamedValue `character` with the chosen UUID. Empty responses poll an owned flow.
Plugin retirement cancels only its matching busy generation through the SDK cleanup ledger.
Plugin installation drafts are load-owned edits, separate from the user's visible install draft.
Reading an instance draft captures the installation owner's edit fingerprint. Resolve/execute use
the same eligibility query and install command as Desktop, retaining fingerprint checks and normal
recovery/task handling. Creation is restricted to registered roots and safe instance names. Game
catalogue DTOs carry the upstream release timestamp; old cache entries without this field require
refresh before serving the ABI's non-null timestamp. Cancellation targets only retained plugin
install operations, never the user's unrelated installation.

Plugin diagnostic adapters consume the exact instance-scoped machine capability snapshot and its existing preflight evaluator. Public finding IDs are `nexa:findings/<code>`; diagnostic text passes through the log redactor. Repair previews bind the instance/library and settings revisions plus the selected findings and their available scoped remediation. Execution re-evaluates those findings after Host confirmation and invokes only Minecraft's existing instance-scoped repair handlers. The first supported remediation in the owner's ordered list is retained; a finding without a supported remediation remains an explicit unavailable repair, never a successful no-op.

Native resource favorite revision conflicts are expected domain rejections on the typed result channel, matching managed routing. They must not escape as transport failures that abort independent callers.

Plugin catalog installation previews bind the canonical resource dependency plan fingerprint. `ResourceModInstallCommand.ExpectedPlanFingerprint` is optional for existing product callers; plugin execution always supplies it. The resource owner recomputes and compares that fingerprint after resolving the actual execution plan and before creating staging or downloading files. Plugin content updates delegate to the existing transaction owner with exact original file size/timestamp and pinned catalog version; plans retain their public changes for review and are consumed once after confirmation.

Plugin Java requirements and selection use the launch owner's manifest/loader requirement resolver for the exact instance. Automatic and version-local preferences share those constraints; no plugin selection may substitute the unconstrained Java range for an instance. Native Java messages append a typed requirement query and an explicit version-local selection flag; no manifests or account credentials cross this boundary.

Plugin downloads use the existing streaming download owner and the configured Host HTTP pool, with load-owned records and Tasks operations. Pause cancels only the current transfer attempt and retains its partial file; the same SDK operation waits for Resume, while its download-specific state is Pending. Cancel or load retirement terminates that operation and removes its staging files. Only an explicitly granted, absent destination may be published, after bounded size/SHA-256 verification and a final grant/absence check. No response is buffered into a plugin Blob. Custom source/transfer providers still require a defined callback contract.

The download destination is created only at the commit boundary; Pause and completion arbitrate under the same load record lock. Native transfer descriptions carry the same maximum/exact length and redirect policy as managed connections. Download Verify compares the current bytes with the digest computed at publication, including when the caller did not supply a digest.

Plugin content transfers reuse the folder/JAR import owner and instance export owner. Imports inspect the explicitly granted file and pass its SHA-256-bearing artifact to the existing Mod import command for the exact instance. Export accepts the existing Mrpack, ZIP, CurseForge, MCBBS and MultiMC formats and filters the owner's preview by admitted content IDs. It stages beside the granted destination and publishes only if that destination is still absent; launcher/account files are never added outside the export owner's category policy.

Managed Java plugin offers retain the install owner's multi-file preview, registry revision, license URLs and expiration. ABI 100 adds offer listing and plan preparation without altering the legacy single-archive distribution model. Installation consumes the reviewed preview through the existing Java owner, and returns a runtime only after verified registration.

Plugin-selected content export supplies an optional exact path allowlist to the export owner. Filtering occurs inside the existing recovery operation lease after collection, so files added after preview cannot enter a restricted export. Existing callers omit the allowlist and retain category-based exports.

Content transaction listing projects the existing update journal and provides a load-owned rollback plan per record. Rollback rechecks the journal record after confirmation, then delegates hash validation and recovery locking to the transaction owner. Already rolled-back journals remain visible and cannot be replayed.

Native Minecraft task publications retain their newest immutable snapshot before
Desktop attaches its task consumer. Attachment replays that snapshot outside the
client lock; the task center's monotonically increasing revision rejects any
replay overtaken by a newer callback. This preserves completed task history across
composition order without adding a transport round trip or embedding the owner.

Catalog plugin providers use the SDK's additive BusinessProviderCall contract and
existing staged registration lifecycle. Desktop owns a closed read-method table
and validates returned resource/version identities, bounded pages, file metadata,
and dependency references. Explicit custom-provider selection dispatches through
the query-only callback boundary; built-in providers continue using their existing
resource owner. Provider cursors are authenticated for the caller and query, and
cannot be reused across searches. Mixed built-in/custom pagination is rejected
until an aggregate cursor contract is defined. Registration does not confer file
installation or favorite mutation authority; those still require domain owners.

Instance reference-name validation is a pure Library.Contracts helper shared by
Host admission and the metadata owner. Metadata keeps its existing public helper
as a forwarding method. Native-service composition must not reference metadata
implementation assemblies merely to validate a path segment.

## Plugin backup boundary (ABI 100 continuation)

Plugin backup operations remain owned by the setup content-addressed backup service.
Composition must project per-instance opaque backup identities and single-use plans,
not expose the global backup directory or accept raw source/restore paths. Capture
and restore require confirmation, revision rechecks and idle-instance fencing.
Restore creates a new destination; it cannot overwrite a live world or arbitrary
existing instance. File import/export requires existing load-owned grants. Retention
and pruning must be scoped to the selected instance rather than applying a plugin
request to all launcher backups. Compatible SDK additions may express missing
instance scope while preserving every existing operation and model fingerprint.

## Completing the ABI 100 business surface

The remaining current-catalog methods are completed as real owner adapters. New
restore destinations and trash previews are additive contracts: an opaque target
must be minted from a caller-owned writable grant or an admitted world/name pair.
Restoration creates a new destination and never silently overwrites an existing
instance. The legacy import form may use an existing backup as an explicit scope
anchor; an additive instance/file request supports the first import into a scope.

Provider registrations use the runtime's staged publication and retirement ledger.
Read callbacks return typed descriptions; the host validates them and retains
ownership of downloads, filesystem publication, credentials and process launch.
Callback kinds, methods, schemas and consumers are documented together. Registering
a provider without a working consumer is not a completed mapping. Definitions,
settings and resource handles remain load-owned; persistent plugin settings retain
revision and schema-version checks across restarts. Task retries are admitted only
for explicitly replayable work or plugin-owned failed/cancelled tasks; ambiguous
or completed effects cannot be replayed by the generic retry API.

Shared private runtime changes consume the same canonical SDK contracts and headers
as the public SDK. Original method IDs and model fingerprints remain unchanged;
new models and operations are additive. Support evidence records original and
additional methods separately while the complete catalog remains the source for
code generation. Native service composition continues through its generated
numeric command/query contracts, including any new account/instance operations.

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
