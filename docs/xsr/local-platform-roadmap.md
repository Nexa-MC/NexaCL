# Local platform integration roadmap

2026-10-26 (Asia/Shanghai). This roadmap implements the approved account-free platform
plan. No NexaMC account or cloud backend is required. Public package/model downloads
remain possible. Stages are independently accepted; this document is not a completion claim.

## Boundaries
Platform abstractions describe OS operations; Platform.Runtime reads native facts and
performs native actions. Services own policies and lifetimes; Composition supplies adapters.
Desktop emits commands through NXML. Renderer never controls processes or Sidecars.
Unsupported, dependency-missing and access-denied outcomes remain distinct. User settings
appear only after their consumers exist. No system-wide security/service changes are allowed.

## Delivery and acceptance
1. Startup/resource governance: native architecture preflight; owned process lifetimes;
   explicit CPU Sets/QoS; user-level Linux resource quotas; cache pressure reclamation;
   cooperative Sidecar quiescence; measured disk concurrency and metered-network policy.
2. Native desktop integration: Windows progress/share; macOS indexing/intents/preview;
   explicit Linux Portal operations.
3. Local copies/snapshots: APFS clones, Btrfs/ZFS snapshots, verified portable fallback.
4. Hardware/handheld policies: topology, GPU budgets, power/thermal, input/display adapters.
5. LAN: explicit device pairing, verified resumable transfer, conflict-safe sync, restart-based
   handoff, separately authorized capture/streaming. Never migrate live JVM memory.
6. Local AI: CPU first, then independently validated GPU/NPU runtimes and bounded model life.

Every slice needs contract/regression tests, architecture checks, AOT where applicable,
recovery behavior, and an explicit record of hardware validation limits. Cloud CI cannot
certify HDR, thermal behavior, controller sensors or topology unavailable on its runners.

## First slice: Windows native architecture preflight
Production composition supplies an architecture validator to the private JVM process boundary.
It runs before bootstrap serialization and OS process creation. Test/custom private transports
can supply a different validator without being required to contain a real JDK.

On Windows, read bounded PE header fields from the actual Host, Java executable and selected
jvm.dll. Compare against the Host binary architecture, not the OS architecture: a matching x64
Host/JDK on Windows ARM64 remains valid. Inspect immediate DLLs in the already extracted
native directory, not arbitrary recursive mod folders. Unknown/malformed required PE data and unsupported hybrid ARM64EC/ARM64X combinations
are explicit errors, never guessed compatible. DLLs extracted from game-native archives are
reported as bounded warnings: legacy bundles contain both x86 and x64 siblings and the
launcher cannot determine which names arbitrary Java/mod code will load. Required Host,
Java and JVM mismatches block spawning; unused game-native siblings do not.
Other operating systems retain their existing native-loader behavior in this slice.
This preflight is diagnostic, not an integrity/security guarantee against later file changes.

User clarification: memory analysis remains disconnected from startup preflight. Do not
reactivate the suspended estimator, block launch on its output, or automatically allocate
heap from it. OS memory-pressure cache management is a separate capability.

### First-slice acceptance
Implemented: Windows required-binary preflight at production composition and benchmark
entry; bounded advisory inspection of extracted game DLLs. Other roadmap slices remain
pending. Regular Release build and six targeted service regressions passed, including
private bootstrap/cancellation and the suspended memory-analysis contract. Architecture
checks passed for 72 projects. Linux x64 NativeAOT publication and three focused regressions
passed (PE fixtures exercise x86/x64/ARM64 headers without claiming Windows ARM hardware
coverage). Publication reports the existing Jint ReflectionAccessor IL2098 trim warning;
this slice does not change Jint. Windows ARM64 physical-device validation remains pending.

## Second slice: explicit per-launch scheduling
Settings `game.cpu-sets` (empty = OS default, decimal IDs/ranges, machine-local/nonexportable)
and `game.process-quality` (default/efficiency/background) support instance overrides and
apply only to the next launch. Domain owns immutable captured policy; platform owns native
calls. Linux uses sched_setaffinity/nice; Windows uses supported CPU Sets/EcoQoS APIs.
Unavailable OS versions hide the controls. No topology-based automatic selection is claimed.
Default performs no native scheduling call. Explicit policy failures abort and cancel the
newly owned JVM before detaching hooks; no unrelated processes or global policy are changed.
CPU Sets apply after spawn, not before JVM initialization, and do not claim a tree quota.
Memory analysis remains suspended. Process termination releases these process-local settings.

Explicit scheduling currently requires direct JVM Host launch: custom wrappers can fork
before the policy is applied, so they are rejected before starting when a nondefault policy
is requested. This avoids claiming the wrapper's affinity necessarily governs the actual JVM.
Windows background and efficiency both use EcoQoS; Linux maps them to nice 10 and 19.
Priority is applied before QoS so it cannot immediately overwrite the Linux nice policy.

Linux affinity/nice are per-thread. The adapter scans `/proc/<pid>/task` with a 4096-thread
budget and up to four stabilization passes, applying to every observed thread. New threads
inherit their creator's settings. Exiting threads are tolerated; denial or failure to stabilize
is reported and the launch owner cancels the process. This is not ongoing enforcement against
game code that later changes its own scheduling. Native regression uses an owned multithread
child and inspects every thread, not only the process leader.

### Second-slice acceptance
Release build passed with no warnings/errors. 45 relevant Services regressions passed,
including an actual owned Linux multithread child, instance override/reset behavior, wrapper
rejection and process/hook cleanup after refused CPU/QoS requests. 41 Desktop settings
tests and two localization tests passed; architecture checks cover 72 projects. Linux x64
NativeAOT publication and four focused regressions passed, with the existing Jint IL2098
warning. Windows native/hardware validation remains pending; no automatic topology,
cgroup quota or memory-analysis activation is claimed.

The previous CI failure was a theme test assuming global Busy becomes permanently false
when one ImportAsync completes. A committed settings revision can queue another appearance
refresh. The test now waits for idle while retaining persistent-selection assertions; the
previously failing case passed 20 consecutive executions without changing production theme
behavior.

## Third slice: shared cache pressure admission
Use the existing native five-second OS memory-pressure sampler for both raster retention and
shared query-cache admission. Desktop bridges immutable observations to the Services cache;
neither Services nor the renderer resolves the other. This is not the disabled Minecraft
memory estimator, and never controls heap sizing or startup admission.

Shared cache Elevated/Critical budgets are one-half/one-quarter of configured entry and
estimated-byte limits (minimum one). Shrinking removes least-recently-used cache references,
never disposes values already held by readers, cancels producers, removes in-flight coalescing,
or deletes disk records. Completion admission always uses the current budget. Three consecutive
Normal observations restore normal admission without eagerly reloading anything; Unknown
retains the existing budget and breaks recovery streaks. Critical-to-Elevated also requires
three lower-pressure observations. Disposal makes late pressure reports harmless.
The interface ISharedStateCache remains source-compatible; pressure management is a separate
optional contract. Reported sizes remain caller-estimated payload sizes, not RSS or guaranteed
reclaimed memory. Other independent caches and cooperative Sidecar quiescence remain pending.

### Third-slice acceptance
Desktop Release build passed with zero warnings/errors. Seven targeted Services tests passed
in both managed and Linux x64 NativeAOT execution, including pressure recovery, coalesced
producers, bounded completion admission and the suspended memory-analysis contract. All 45
Avalonia backend tests passed, including worker-thread observation forwarding, repeated owner
attachment and detachment from the previous owner. Architecture checks passed for 72 projects.
NativeAOT publication retains the existing Jint IL2098 warning. These are cloud Linux tests,
not physical Windows/macOS pressure measurements; payload estimates do not prove RSS savings.
Sidecar quiescence and Linux resource quotas are still separate pending roadmap slices.

## Fourth slice: cooperative Sidecar memory quiescence
Desktop reuses the shared-cache pressure decision (including its recovery hysteresis) to
request Sidecar quiescence only at Critical pressure. Runtime owns a single latest-value
worker, serialized with discovery/restart/stop/reload; sampling never waits for IPC. Existing
binary DEACTIVATE/ACTIVATE controls retire activation leases and restore the same registered
session. In-flight exchanges/streams become unavailable under the existing deactivation
contract. This is cooperative inactivity, not OS process suspension or a guarantee of RSS
reduction. It does not invoke the Minecraft memory estimator.

Only children deactivated by this policy may be automatically reactivated. Explicitly stopped
packages remain stopped. Children discovered, restarted or recovered while constrained stay
Ready without activation until recovery. A distinct Quiescent package status exposes this
state. A failed transition closes that session and uses existing bounded recovery; shutdown
cancels and joins the pressure worker. Late observations after disposal are ignored. No new
wire messages, SDK changes, forced process freezing or cloud dependencies are introduced.

### Fourth-slice acceptance
All 183 Runtime regressions passed in managed and Linux x64 NativeAOT execution, with no
skips. The new physical-child scenario verifies same-session recovery, preservation of manual
stop, restart without activation under pressure, rapid observation replacement and shutdown
while quiescent. Desktop Release build passed with zero warnings/errors; architecture gates
passed for 72 projects. NativeAOT publication succeeded. These cloud Linux tests verify Host
policy and protocol behavior, not third-party peers' actual memory reduction or physical
Windows/macOS pressure behavior. Linux resource quotas remain pending.

## Fifth slice: explicit Linux CPU quota
A per-launch `game.cpu-quota-percent` setting (0 = disabled, 100 = one CPU,
maximum 102400) uses a transient systemd user scope on cgroup v2. Platform owns
busctl invocation and checks the actual Host cgroup membership and cpu.max before
Services writes private JVM bootstrap bytes. The scope contains the Host and its
future descendants; it is not a machine-wide policy. No root/system manager,
automatic delegation, heap estimator or memory hard limit is introduced.

Only the direct bundled JVM Host path supports this option; custom wrappers and
unmanaged transports are rejected before spawn. Explicit quota failure cancels
the newly owned Host, keeping launch cleanup ownership. The default does not call
systemd. Unit names are random and arguments never pass through a shell. User
manager/cgroup-v2/controller availability and access denial are reported as launch
failures, not silently accepted. Empty scopes are collected by systemd. The cloud
container lacks a user bus; native success must remain explicitly unverified here.

### Fifth-slice acceptance
Eight targeted Services regressions passed in managed and Linux x64 NativeAOT execution,
covering scoped setting overrides, disabled defaults, private transport, failed-quota Host
cleanup and unchanged suspended memory analysis. Actual busctl invocation against an owned
waiting child returned quota_refused in this container, as expected without a user manager;
positive kernel-limit parsing and successful admission sequencing use fixtures, not a claim
of native systemd success. All 374 Desktop regressions and 72-project architecture checks
passed. Desktop/Services Release builds had zero warnings/errors. NativeAOT publication
retains the existing Jint IL2098 warning. The quota setting is machine-local/nonexportable.
A real systemd user-session success run, memory/IO quotas and disk/network policies remain
pending; this slice only adds explicit CPU bandwidth limits.

### Fifth-slice CI follow-up
GitHub XSR CI run 40290441575 confirmed native CPU quota admission (`CPU quota native
adapter: ok`) against its systemd user session. This verifies Host scope membership and
cpu.max, not CPU throughput benchmarking. That run subsequently exposed two stale catalog
cardinality assertions (522 instead of 523). Both now include the new setting; the catalog
contract also verifies its page, instance override and next-launch timing.

Launcher Build run 40290441432 failed on Intel macOS while creating the DMG with hdiutil's
`Resource busy`. Packaging now retries only this error, at most twice after the first attempt,
using fresh temporary images. Each successful image must pass hdiutil verify before atomic
publication. Other errors and verification failures still fail immediately. No mounted disks
are forcibly detached and an existing final artifact is preserved on failure.

## Remaining delivery batches (2026-10-26)
The user requests continuous delivery across the remaining scope, not a confirmation after
each small commit. Retain independently reviewable commits and acceptance records.

1. Resource governance and hardware policy: explicit memory/IO quotas, measured disk
   concurrency, metered-network behavior, topology-aware scheduling (heterogeneous cores,
   X3D, NUMA), unified-memory budgets and power/thermal policies. Existing hardware probes
   are observations, not implementation of automatic policy.
2. Native integration and local storage: Windows taskbar progress and system share UI;
   macOS indexing, App Intents/local Siri actions, Quick Look and APFS clone;
   Linux GameMode, explicit Portals, Steam/Gamescope integration, Btrfs/ZFS snapshots.
   Existing DesktopJumpList, OS account key protection (including macOS Keychain) and
   native notification adapters are existing paths, not new work. Screenshot sharing
   currently copies a file to the native clipboard; it is not a system share panel.
3. Independent modules: local pairing, verified resumable LAN transfer, conflict-safe sync,
   restart-based instance handoff, explicitly authorized streaming, local AI backends,
   and actual HDR/VRR/OLED/multi-display/controller touch/gyro integration.

Cloud/NexaMC-account requirements remain excluded. Minecraft memory analysis stays disabled
and disconnected from startup preflight. Unsupported public APIs and absent physical
hardware must be recorded as unavailable or unverified, never simulated as a completed
integration. Remaining Windows GameBoost-style behavior requires confirming a supported
public API; do not invent an OS-wide optimization switch.

CI follow-up local acceptance: all 826 Services tests, all 88 release-tool tests,
72-project architecture gates and source formatting verification passed. Services Release
build reported zero warnings/errors. The DMG contention cases are deterministic tool fixtures;
the next macOS CI packaging job is the native end-to-end verification.

## Sixth slice: explicit process memory and device IO limits
Extend the existing pre-bootstrap user scope with optional MemoryMax and device bandwidth
limits. All defaults are zero/unlimited, machine-local, per-instance and next-launch. Memory
is a hard process-tree cgroup limit (the kernel may terminate an over-limit game), not JVM
heap sizing or the suspended memory estimator. IO bandwidth applies to the block device
hosting the game's working directory, not every device a mod might access. Unsupported
filesystems/controllers must fail explicit requests. Verify memory.max and io.max before
sending bootstrap. Existing CPU-only adapters retain compatibility through a default overload.

### Active release objective
User explicitly approved completing batches 1 then 2 before publishing the next Alpha;
batch 3 is postponed. Current latest GitHub release is v2.0.0.alpha.13, so target Alpha 14
unless another release appears before publication. Do not publish early or treat the local
AI/LAN/display/controller batch as part of this release. Existing memory-analysis exclusion
remains binding. CI fix commit 2621ffa5 precedes these additions.

## Seventh slice: measured disk admission
Shared work admission consumes measured successful download-write latency. Writes under
32 KiB are ignored; latency is normalized per 64 KiB. A slow sample (40 ms or more) lowers
new disk admission to one without cancelling existing leases. Sixteen consecutive healthy
samples (10 ms or less), at least ten seconds after the latest slow write, restore the
configured maximum. Recovery never exceeds configured concurrency. This is application
write-latency feedback, including filesystem/cache effects, not a physical SSD/HDD benchmark
or per-device classification. HTTP and CPU budgets are unchanged. Old scheduler adapters
need not implement the optional feedback contract; no synthetic benchmark runs at startup.

## Eighth slice: metered-network policy
Platform observes Windows NLM cost, Linux NetworkManager cost for default-route devices,
and macOS NWPath expensive/constrained state through a bundled Swift helper using public
APIs. Unknown stays Unknown; Wi-Fi is not assumed unlimited. Foundation owns one cached
observation worker, bounded initial sampling and 60-second refresh; it joins cancellation
before dependencies retire. Auto policy prevents new background/idle download connections
on known costly networks; allow overrides cost, restrict blocks optional background work.
Explicit foreground installs remain permitted. Admission is rechecked after scheduler waits.
The helper is built on macOS during publish and included in the signed update payload;
missing development helpers report Unknown. No cloud account or remote discovery is used.

CI follow-up confirmation: Launcher Build 40299741593 completed both osx-x64 and osx-arm64
packaging successfully; XSR CI 40299741757 passed. Windows packaging jobs were still running
at observation time. This validates the DMG fix on native runners before the new resource
policy/helper changes.

## Ninth slice: opt-in topology selection
Default remains OS scheduling. Performance, largest-cache-domain and single-NUMA-domain
preferences select only observed eligible logical processors before JVM bootstrap, through
existing CPU Sets/affinity adapters. Manual CPU indices and automatic preferences cannot be
combined. Wrappers and non-private Host transports are rejected for automatic selection.
Windows reads bounded SDK CPU-set/cache records; Linux reads current affinity plus bounded
sysfs capacity/CPPC, cache and NUMA facts. Missing required facts cause an explicit unavailable
result, never a model-name heuristic. Cache preference is topology-based, not an AMD-only
brand switch. NUMA preference selects CPUs, not an allocator/memory-placement guarantee.
No Minecraft memory estimator or automatic heap allocation is involved.

### Resource-policy tranche acceptance (slices 6–8)
All 830 Services regressions passed; 41 Desktop settings regressions passed after keeping
advanced quotas in a trailing dedicated group. Architecture gates passed for 72 projects.
Linux x64 NativeAOT publication and seven focused quota/disk/network/catalog regressions
passed. The existing Jint IL2098 trim warning remains. Native network cost is Unknown and
systemd quota is refused in this container; no native memory/IO enforcement is claimed here.
Windows NLM and the bundled macOS Swift helper require the platform CI runs added with this
tranche. Swift helper publish targets and workflow syntax were parsed locally. No Alpha
release is authorized until the remaining batch 1 and batch 2 work has been completed.

## Tenth slice: battery and thermal work admission
Platform projects existing battery facts and the public macOS thermal state into a compact
local observation. Foundation owns one cancellable sampling worker and a reversible quiet
lease: known discharging battery at or below 20%, or serious/critical macOS thermal state,
pauses new background/idle work. Interactive installs and running games are unaffected.
Three healthy samples restore admission; unknown observations retain a constraint for at
most three consecutive samples, then release it to prevent permanent starvation on a broken
probe. This does not change system power plans, JVM nice/priority, Sidecar lifecycle, or the
disabled memory estimator, and does not reintroduce a low-power-mode setting.

## Eleventh slice: raster memory admission budgets
Existing native pressure sampling also bounds new raster retention to one sixteenth of known
physical/container headroom (including shared-memory Macs). Windows Platform enumerates
up to 32 DXGI adapters and reports the minimum remaining local-segment budget among adapters
with actual current-process allocations. Desktop supplies that optional value to raster
admission; it is not labelled dedicated-memory capacity or texture residency. Unknown GPU
budgets never invent a value. No active bitmap lease is invalidated, no Minecraft heap changes,
and no GPU allocator or display policy is altered. Linux retains its measured DRM residency
pressure signal where per-process dynamic budget is unavailable.

## Native-integration slice: Windows taskbar progress
Desktop projects the existing task-center summary into a neutral window progress state.
The native backend uses ITaskbarList3 on the window dispatcher, with balanced COM ownership;
active tasks show their aggregate progress and an empty queue clears the indicator. No new
task execution or download state is introduced. Missing window handles retry on a later frame,
and periodic refresh recovers Explorer restarts. Other platforms do not claim this API.

## Native-storage slice: verified copy-on-write files
The existing content-addressed backup/restore owner accepts an optional exclusive-create clone
adapter. Platform uses APFS clonefile or Linux FICLONE; unsupported/cross-filesystem operations
fall back to the existing streaming copy. Both capture and restore hash the resulting file
before publication, retain existing size/change/session checks, and never share mutable hard
links. A failed/cancelled attempt removes only the private staging file. This optimizes files
inside existing logical world snapshots; it does not claim Btrfs subvolume or ZFS dataset
snapshot management, which remains separate native integration work.

Topology follow-up: Windows selection also intersects explicit inherited default CPU-set IDs,
not only process group/affinity masks. Taskbar COM calls retry on a five-second refresh after
native failure instead of retrying every frame. SDK ITaskbarList3 vtable order was checked
against the public Windows SDK header before implementing the unmanaged calls.

### Hardware-policy and native-integration checkpoint
Managed acceptance: 833 Services cases, 45 renderer-backend cases and 41 Desktop settings
cases passed; the solution built with zero warnings/errors and architecture checks covered
72 projects. The subsequent native-copy tranche passed five focused clone/topology/power/
catalog cases. Native Linux CoW is unavailable in this workspace; fallback was observed and
native independence was tested only conditionally. Object-byte accounting remains logical
file lengths, not a measurement of shared physical extents. NativeAOT is still being checked.

Resource-policy commit 925cbe18 passed XSR CI 40306116753. Native launcher packaging for
Linux x64/arm64, Windows arm64 and macOS arm64 passed at this checkpoint; other jobs remained
running. The latest public version is still Alpha 13. No Alpha 14 release/tag was created.

Batch 2 is not complete: actual Windows share UI; macOS Spotlight/App Intents/Siri and
Quick Look integration; Linux GameMode, explicit Portal integration, Steam/Gamescope flows;
and native Btrfs/ZFS snapshot management still need implementation/verification. APFS and
Linux per-file CoW integrate existing logical snapshots and do not close the native
subvolume/dataset snapshot item. Existing Keychain, Jump Lists and native notifications are
retained. LAN/cloud/AI/display/input items remain postponed under batch 3.

NativeAOT acceptance: Linux x64 publication succeeded and all five focused clone/topology/
power/catalog regressions passed, including the explicit Windows CPU-set filter fixture.
The existing Jint IL2098 trim warning remains. Source formatting completed successfully.
The final Desktop build passed with zero warnings/errors. Cross-platform NativeAOT CI now
runs topology/power/network/clone fixtures using its existing Windows, Linux and macOS jobs;
this adds native CoW coverage on APFS-capable runners without a new publishing pipeline.
Final architecture recheck passed for 72 projects. Resource-policy Launcher Build 40306116646
has now completed successfully for all six target architectures, including both macOS helpers.

## Native-integration slice: opt-in Linux GameMode
A per-launch machine-local GameMode preference registers the directly owned JVM Host before
private bootstrap. Platform uses GameMode's public RegisterGameByPID/UnregisterGameByPID
D-Bus methods with the game PID and this launcher as requester, bounded by busctl timeouts.
The launcher does not install the daemon or edit GameMode/system configuration. An explicit
request may activate the performance adjustments already configured in the user's GameMode daemon.
Session ownership unregisters on cancellation, exit and disposal. A timed-out registration
also attempts cleanup because the server might have accepted it before the response was lost.
Missing/refused services fail an explicitly requested launch rather than claim optimization.
Default performs no calls; wrappers cannot claim control over the eventual JVM and are rejected.
The public client protocol was checked against FeralInteractive/gamemode client_impl.c and
its daemon D-Bus method definitions; no upstream implementation is copied.

## Native-integration slice: macOS Quick Look and Portal ownership
Screenshot preview exposes an explicit macOS Quick Look action. The existing screenshot
query/identity checks precede a bundled helper's QLPreviewPanel; a bounded ready handshake
must succeed before the UI reports completion. The panel is user-owned after opening and
closes through native controls, without changing or exporting the image. The native helper
uses public AppKit/QuickLookUI APIs, not AppleScript automation permissions. Linux file
selection/save already goes through Avalonia's DBus-enabled storage provider and document
streams; preserve that portal boundary rather than replacing portal results with guessed paths.

## Native-integration slice: Windows screenshot share panel
The screenshot Share intent uses the owning HWND's public DataTransferManager interop.
An AOT-safe COM delegate supplies a bitmap package from an exclusive-read native random
access stream only after checking the selected screenshot identity again. WinRT owns the
stream reference after package construction. Delegate/event/manager references are balanced
when another share replaces the session or its window closes; failures stay explicit.
No uploaded image, external helper window, shell verb guessing or hidden cloud transfer is
introduced. ABI slots/IIDs are checked against the public SDK ShObjIdl_core.h and generated
Windows Runtime metadata; the typed-event IID follows the standard WinRT signature hash.
Screenshot regression note: the overscan test made steady progress (76/78 images loaded)
but exhausted its five-second fixture deadline while other builds consumed CPU; isolated
rerun passed. Only this high-card-count test now allows 15 seconds. Its no-repeat-read,
complete-load and unchanged-IO assertions remain intact. Quick Look routing tests verify the
exact selected file identity. Windows CI exercises real WinRT bitmap package construction
and native stream release in addition to the cross-platform delegate ownership regression.

## Added Alpha 14 task: launched-version and measured-runtime telemetry
User added collection of the launched version's data and actual runtime observations.
Existing diagnostic.run/diagnostic.mods events already carry bounded loader/version/mod
inventory and windowed process samples. Extend these existing state/telemetry contracts;
do not create a second sampler or invent measured values from configured limits (especially
MaxFps versus observed FPS, or heap limit versus actual memory). Preserve the existing
consent/prerelease policy and redaction boundary. Include safe version/component/Java/mod
metadata, captured launch policy and actual available process counters/exit outcomes, with
explicit completeness/unknown information. Credentials, account identities, raw JVM/game
argument strings, absolute user paths, save contents and raw unredacted logs are excluded.
The website/ingress validation and regression fixtures must accept the same versioned schema.
Implementation and acceptance evidence are recorded below; Minecraft memory analysis and startup gating remain unchanged.

CI follow-up: topology display terms lacked English/Traditional Chinese entries; those and
the GameMode label now have explicit translations. The macOS CoW probe itself succeeded,
but its backup fixture used /var's linked temporary root, rejected correctly by CheckLinks.
That fixture now uses a unique directory under /private/tmp. Production link protections
remain unchanged. NativeAOT renderer publication and all 46 backend tests passed locally.

Native-integration local acceptance: the solution built with zero warnings/errors; GameMode
ownership/catalog cases passed, all 11 screenshot regressions passed, and 46 backend cases
passed in both managed and Linux NativeAOT execution. Seven language regressions passed after
the CI translation correction. GameMode's real daemon, WinRT share UI and the new Swift
Quick Look branch remain subject to platform CI/device availability; no positive native
claim is made from Linux fixtures. Launcher Build 40310302202 succeeded on all six targets
for preceding commit 7ed10738; its XSR CI failures are addressed above.

### Runtime telemetry lifecycle contract (Alpha 14)
Add diagnostic.launch and diagnostic.run-summary, schema=1. Both link only by the existing
random per-run ID; the existing diagnostic.mods pages retain version/component inventory
and diagnostic.run retains periodic samples. Launch carries a closed numeric projection of
the captured plan (Java/counts/configured limits/policy enums), never serialized arguments
or paths. Summary projects only already-collected counters, nullable native measurements,
coverage flags, elapsed/launch timing and exit status. Unknown is -1; a real observed zero
remains zero. GPU peaks are not summed into a fabricated simultaneous peak. Configured FPS
remains an option, not a measured frame rate. No new sampler, JMX request, preflight or model
activation is introduced. Ingress validates exact keys/schema and numeric bounds. Separate
idempotent per-run lifecycle rows have the same 30-day retention as run samples. Admin detail
returns launch and summary separately; old clients/events remain accepted unchanged.
Inventory schema 2 adds SHA-256 (when available), nested-candidate flag and provided mod IDs.
Legacy six-field inventory remains accepted. Per-mod dependency/provided maps are bounded
and explicit completeness becomes false if entries are omitted; per-page 4096-character and
129-page limits remain. These are installed metadata declarations, not proof of runtime loading.

Telemetry acceptance checkpoint: launcher build completed without warnings/errors and all
14 targeted telemetry/inventory regressions passed. The actual generated 18-event wire
fixture is accepted by both active and legacy backend validators. Backend legacy tests (37),
active API suite (100 before syncing unrelated remote billing changes), lifecycle/storage
regressions and website tests/build passed. Server commit 983d9cb and website commit 780f7dc
are pushed; production API deployment is queued in Actions 40325037259. Metadata timeout
retains an incomplete inventory and captured launch profile instead of losing the run context.
The previous platform integration commit 087cfa1e passed both XSR CI 40315792633 and all six
Launcher Build targets in 40315792680. Alpha 14 has not been tagged or released.
Production telemetry API deployment 40325037259 succeeded with migration 0034 and preserved
bindings/schedules; website deployment 40325047890 also succeeded. Server CI and Nexa API CI
passed on 983d9cb. The client reports an unknown terminal exit as unknown, retaining a separate
exit_observed flag so a real exit code of -1 is distinguishable from missing evidence.
Final client verification: zero-warning/error service test build, 14 targeted tests,
72-project architecture gate and required formatting verification passed. Website follow-up
marks missing inventory pages incomplete and includes telemetry paths in management deploy
triggers (839bd46). NativeAOT/cross-platform verification continues in the normal client CI;
no new public launcher version is claimed at this checkpoint.

## Completion batch: native snapshot capture
Native Btrfs/ZFS snapshots are capture leases behind the existing content-backup boundary.
Only an exact Btrfs subvolume or exact mounted ZFS filesystem, without descendant datasets/
subvolumes, is eligible. No sudo, mount, dataset creation, writable rollback or recursive
snapshot deletion occurs in the product. Platform owns bounded tool invocation and native
lease cleanup; Setup consumes a read-only source and still hashes every object. Existing
logical backup list, retention, restore-to-new-directory and corruption verification remain
canonical. Unsupported filesystems, missing tools or undelegated privileges fall back to
existing checked capture; an acquired lease must be released after success/cancellation.

## Completion batch: Steam and Gamescope entry points
Linux offers explicit Steam registration through a user-selected userdata/<id>/config
folder while Steam is stopped. Preserve unrelated binary VDF entries, reject unknown/corrupt
formats, retain a backup and atomically replace only after rechecking original bytes. Managed
entries launch NexaCL (optionally inside installed Gamescope), not a wrapper masquerading as
the owned JVM. Steam supplies Steam Input through its ordinary non-Steam game entry; existing
controller recognition remains authoritative. No Steam account credentials are read, no
settings are applied to other profiles, and no compositor/system package is installed.

## Completion batch: macOS local actions
A bundled accessory application owns Core Spotlight indexing and native App Intents metadata,
so the NativeAOT launcher does not impersonate a Swift runtime. Its fixed Launch/Resources/
Settings actions open only existing nexacl:// routes; Spotlight continuation uses the same
allowlist. The helper is registered locally with LaunchServices and has no network, account,
file-content indexing or arbitrary command execution. Shortcuts/Siri discovery is supplied
by macOS; voice recognition availability remains an OS/device setting, not a cloud service
implemented by NexaCL. Compile/extract metadata for both architectures and package the helper
inside Contents/Helpers before signing. Startup indexing is bounded and failures are explicit.
Native capture writes a bounded ownership marker before snapshot creation under the existing
content-store lock. A later capture reaps only matching managed read-only Btrfs snapshots or
exact named ZFS snapshots. Uncertain permissions/identity leave the marker intact and fall
back instead of guessing absence or deleting unrelated data. Native temporary snapshots are
not retained as an alternative backup catalog; verified CAS manifests remain the recovery UI.
Release closure: publishing a prebuilt signed draft no longer triggers a second build that
could overwrite the same-version source assets. Empty published releases retain the initial
build trigger, and tag/manual builds remain available. This preserves the update repository's
immutable-byte contract when Alpha 14 is promoted after verification.

## Alpha 14 candidate closure (2026-10-27 Asia/Shanghai)
The implementation of approved batches 1 and 2 is now wired through production entry points.
Windows includes architecture checks, CPU Sets/QoS, taskbar progress and native share;
macOS includes native local Spotlight/App Intents, Quick Look, APFS clone, network/thermal
probes; Linux includes quotas, GameMode, portal pickers, selected-profile Steam/Gamescope
registration and native Btrfs/ZFS capture leases. Shared policies cover pressure reclamation,
cooperative Sidecar admission, topology, GPU budget, disk feedback and metered/background work.
No unsupported OS-wide GameBoost switch is invented. ZFS requires an exact dataset mount and
existing delegation; absent capabilities use the verified fallback. Voice control and Steam
Input remain dependent on the user's OS/client/device configuration, not cloud accounts.

Local solution build succeeded with no warnings/errors. All 838 Services tests, 374 Desktop
composition tests and 88 release-tool tests passed. Architecture checks passed for 72 projects.
Native platform CI 40430197927 passed on Intel macOS and Apple Silicon: Xcode emits all three
App Intents, signed companion verification succeeds and native launch smoke exits cleanly.
The final integrated six-target packaging and isolated Btrfs native fixture remain release
gates, not assertions of physical-device testing. Batch 3 stays postponed, and the Minecraft
memory estimator remains disabled. Runtime telemetry API/UI are already deployed.
