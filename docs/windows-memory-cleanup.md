# Windows memory cleanup

The home widget supports manual cleanup and automatic cleanup above a chosen
physical-memory threshold. Ordinary mode trims eligible application working sets.
Deep mode requests a single-use UAC worker and additionally executes these operations:

| Operation | Windows interface |
| --- | --- |
| System working sets | SystemMemoryListInformation / MemoryEmptyWorkingSets |
| System file cache | SystemFileCacheInformationEx, minimum/maximum SIZE_T_MAX |
| Volume cache | Volume GUID enumeration and FlushFileBuffers |
| Modified pages | SystemMemoryListInformation / MemoryFlushModifiedList |
| Standby pages | SystemMemoryListInformation / MemoryPurgeStandbyList |
| Low-priority standby | SystemMemoryListInformation / MemoryPurgeLowPriorityStandbyList |
| Registry cache | SystemRegistryReconciliationInformation |
| Physical-page combining | SystemCombinePhysicalMemoryInformation |

The worker enables available debug, profiling and quota privileges, restores their
previous state, and returns each operation's native status, duration and observed
available-memory change. Unsupported or denied operations are reported individually.
After successful global working-set cleanup, it avoids repeating the per-process
pass. If that operation is rejected or game protection prevents it, the worker falls
back to identity-checked per-process trimming.

Global operations cannot exclude one process. The worker checks live game names
and registered game exclusions before every operation, and skips global work while
any protected game is present. Ordinary trimming still protects those games and
their descendants. This protection means cleanup while playing intentionally differs
from a utility that globally trims the game too. No process is terminated; paging-file
configuration is unchanged. Lower working sets do not establish lower private commit,
and standby pages are already reclaimable by Windows.

## Before launching a game

The independent **Deep clean before launching a game** switch defaults off. Enabling
it runs deep cleanup after launch preflight, before JVM creation, regardless of the
manual deep-mode or threshold settings. Each cleanup requests UAC; the main UI stays
unelevated. Overlapping launches share a cleanup attempt. Canceling a waiting launch
leaves another launch's cleanup alone. Refusing UAC or a cleanup failure records its
status and continues launching; explicitly canceling the launch prevents game creation.
Other running games still cause global operations to be skipped.

## Verification and comparison

Implementation is independent. The feature uses OS interface contracts; no Mem Reduct
implementation, dependency or asset is included in the product. The reference behavior
is [Mem Reduct 3.5.2](https://github.com/henrypp/memreduct/tree/2f5c55d15f69a2c88859e036b16c6f4c971e1968).

Service tests cover scheduling, mutual exclusion, live game protection, cancellation,
UAC rejection, native process identity and operation outcomes. Windows NativeAOT CI
also exercises real system calls and retains native statuses and measurements.

The explicit test-only comparison command is:

```powershell
Nexa.Services.Tests.exe --memory-compare C:\fixtures\memreduct.exe
```

Run it on an elevated disposable Windows test host. CI downloads the pinned upstream
binary into its temporary directory, checks archive and x64 executable SHA-256 values,
and never adds that binary to a release artifact. Six samples per cleaner alternate
order, each with a freshly populated 64 MiB child process. NexaCL runs its actual
authenticated worker; the reference uses `-clean:full` in a temporary portable copy. Its result dialog is
dismissed only for the process created by the test; timeout kills that owned process.
The pinned release also returns exit code 575 after successfully handling CLI commands,
so measured cleanup validates the reference. Logs include working sets,
available-memory deltas and elapsed time. Acceptance requires the median target
working-set reduction to be within five percentage points of the reference, and a
reference reduction of at least 75% so an ineffective baseline cannot pass.

This comparison establishes behavior for that controlled workload. It does not prove
universal physical-memory or game-performance parity. Machine-wide availability can
change during either run; retain its signed measurements without turning noise into
an asserted improvement. Interactive UAC and real application impact need Windows
device acceptance in addition to automated CI.

## Recorded Windows evidence

[Windows NativeAOT CI, commit `29e676b8d`](https://github.com/Nexa-MC/NexaCL/actions/runs/41767521760)
completed all eight real system operations with native status zero. Artifact
`windows-memory-cleanup` retains the individual statuses and the comparison log.
The direct system pass observed available memory rise from 12,714,315,776 to
15,850,754,048 bytes while its touched child working set fell from 77,979,648 to zero.

Across six fresh-child samples per cleaner, the median target working-set reduction
was **100% for NexaCL and 100% for Mem Reduct 3.5.2**, satisfying the controlled
comparison criterion. Median machine-wide available-memory changes were 125,523,968
and 153,532,416 bytes respectively; those values include background activity and do
not establish equal physical-memory gains. One reference sample's availability
change was negative despite a 99.8% target working-set reduction. This is why the
report keeps target working sets and machine-wide availability separate.
