# Architecture and dependencies

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

## Windows memory cleanup

Common owns scheduling, thresholds, cooldown, and state. Platform.Runtime samples physical memory and applies Windows working-set APIs to individual processes. Composition supplies protected game identities. Desktop uses commands/state in the home widget and does not enumerate or manipulate processes. The implementation is independent of Mem Reduct source and binaries.

Cleanup never terminates or suspends applications and never invokes global working-set/standby operations that cannot exclude games. Protect registered Minecraft PIDs and descendants, Java/Bedrock, foreground applications, launcher processes, critical system processes, and other sessions. Use creation times and retained handles against PID reuse; skip inaccessible or uncertain identities. Report working-set observations and system availability separately, without equating either to private committed memory released.

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

LAN discovery is an explicit, cancellable, bounded multicast listen for Minecraft
LAN advertisements; it does not create an Internet lobby or tunnel. Archive/NBT
inspection uses entry, byte, recursion and text budgets and treats payloads as data.
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
