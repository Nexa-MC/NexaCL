# Platforms and processes

`src/platform/Nexa.Platform.Abstractions` defines portable ports and `Nexa.Platform.Runtime` implements OS behavior. Windows Explorer integration is in `src/platform/Nexa.ExplorerCommand`; `eng/xsr/Xsr.PlatformHelper.targets` builds the macOS Swift helper.

Official targets are Windows, Linux, and macOS on x64/ARM64. Native windows obey OS main-thread and lifetime requirements. A visible setting is not proof of OS availability or successful application; use real probes and permission results.

`Nexa.Jvm.Host` owns controlled Minecraft launch/session lifetime. Explicit contracts carry arguments, Java runtime, environment, and policy. Creation, exit, resource leases, and window closure follow cancellation and asynchronous retirement. Platform ports supply system credentials without exposing secrets in UI state.

Platform implementations and integration suites verify installers, protocols, file associations, tray, and OS actions. User/system scope require different permissions. Never turn arbitrary downloaded content into an elevated execution entry.

OS pressure policies and Minecraft memory estimation are separate. The estimator remains disabled and does not participate in preflight or automatic JVM allocation. Cloud CI cannot replace real-device SmartScreen, Gatekeeper, Steam, or filesystem-permission acceptance.

The bottom-right home widget includes Windows memory cleanup. Manual cleanup trims eligible third-party working sets. Automatic mode defaults off, samples every five seconds, triggers above a configurable 50–95% physical-memory threshold (default 80%), and waits at least two minutes between runs. Deep cleanup defaults off and requests UAC for a one-operation worker. Denied authorization reports not executed; the UI stays unelevated and no persistent service is installed.

Per-process trimming in both modes excludes registered games/descendants, Java/Bedrock, foreground applications, launcher processes, critical system processes, and other sessions. Deep mode additionally requests system working-set cleanup, file-cache cleanup, volume-cache flush, modified-page writeback, standby and low-priority standby purges, registry reconciliation, and physical-page combining. Each system operation is skipped if live game names or registered game exclusions are present, because global operations cannot exclude individual processes. Neither mode terminates applications or changes paging files. The report distinguishes successful, protected, unsupported, denied and failed operations, with native status codes, duration and measured available-memory deltas. Counts and observed working-set reductions do not establish private-commit release or FPS improvements. Independent Windows API code is used, without Mem Reduct source or binaries. Interactive UAC and third-party application effects still require Windows acceptance.

See [Windows memory cleanup](windows-memory-cleanup.md) for operations and the controlled comparison protocol.
