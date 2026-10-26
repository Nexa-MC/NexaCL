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
