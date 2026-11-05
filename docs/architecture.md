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
