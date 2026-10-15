# XSR-827: launcher-aware mod process compatibility

## Evidence and scope

In this task, CA means [Crash Assistant](https://modrinth.com/mod/crash-assistant)
(Modrinth `ix1qq8Ux`). Its declared source is
[KostromDan/Crash-Assistant](https://github.com/KostromDan/Crash-Assistant), inspected
read-only at `93fd8960ed4f9e55a9f5c34ab2f8ce2c4308fc14` on 2026-10-08.
No downloaded mod JAR is executed by this investigation.

* `common_config/.../utils/JavaBinaryLocator.java` obtains the executable from
  `ProcessHelper.getCurrentProcessCommand()`. On Java 9+, that is the operating
  system executable reported by `ProcessHandle.current().info().command()`.
  [Exact source](https://github.com/KostromDan/Crash-Assistant/blob/93fd8960ed4f9e55a9f5c34ab2f8ce2c4308fc14/common_config/src/main/java/dev/kostromdan/mods/crash_assistant/common_config/utils/JavaBinaryLocator.java).
* `common_config/.../loading_utils/JarInJarHelper.java` starts that executable with
  JVM options, `-cp`, `dev.kostromdan.mods.crash_assistant.app.class_loading.Boot`,
  and `--args-file`. The normal XSR executable is the native JNI host, whose
  previous entry point accepted only `--jvm-host`; this child exited with code 2.
  [Exact source](https://github.com/KostromDan/Crash-Assistant/blob/93fd8960ed4f9e55a9f5c34ab2f8ce2c4308fc14/common_config/src/main/java/dev/kostromdan/mods/crash_assistant/common_config/loading_utils/JarInJarHelper.java).
* CA uses the game's PID/start time, `java.class.path`, RuntimeMXBean VM options,
  and `sun.java.command` for diagnosis. It uses cwd-relative `mods`, `config`,
  `logs`, `crash-reports`, and `local/crash_assistant` directories. The old planner
  used the instance directory as cwd even when `--gameDir` selected the shared
  Minecraft root, splitting these paths for nonisolated instances.
* CA respects the existing user environment
  `DisableEntirelyCrashAssistantModOnSystem=true`; the launcher must not invent
  or override this preference. Its log discovery has no NexaCL-specific launcher
  log path contract. CA's own `logs/stderr_stream.log` remains its own output;
  launcher credentials/logs are not copied into game directories for discovery.

Read-only `origin/dev` at `4755faee0a10686917213afe57cb772ade83d77f`
inspection of `MinecraftJvmArgumentService`,
`MinecraftProcessLaunchService`, and launcher JVM request/host behavior establishes
normal manifest launcher token replacement and argv construction, but no CA-specific
flag or environment override that repairs native executable detection.

Also inspected public [CleanroomRelauncher](https://github.com/CleanroomMC/CleanroomRelauncher)
at `49c5682a3a1a6c50bb6704c403848b063427c4e1` and
[lwjgl3ify](https://github.com/GTNewHorizons/lwjgl3ify)
at `c87172bedb89256cebe5e4442dad896e1aadf482`. Both obtain game arguments from
loader-owned state, select Java themselves, and start child JVMs. Cleanroom's
`RelaunchMainWrapper` watches its parent and the original process waits/forwards
the child exit code. lwjgl3ify's `forwardLogs` path waits/forwards; its graphical
console path intentionally ends the original JVM and continues in a detached
process. The launcher cannot silently reinterpret that detached process as a
managed XSR session. Their generated command/argument-file privacy is mod-owned;
this change never reconstructs parent credentials into public argv or files.

## Locked compatibility boundary

The ordinary managed game launch continues to expose exactly `--jvm-host` as
the native host's OS arguments. Its selected runtime, JVM options, main class,
and game arguments travel through the bounded private bootstrap pipe. No game
credential is added to OS argv, environment, bridge metadata, or disk files.

After the private request has been accepted, the isolated host records only the
canonical selected Java executable in `NEXACL_JVM_CHILD_JAVA`. A mod that invokes
the current native executable as a Java command can use a bounded Java child
bridge. The bridge requires this concrete existing java/javaw executable, rejects
host recursion and invalid/oversized vectors, and preserves each argument as a
separate argv item. It forwards only the mod's newly supplied child command to
real Java. It does not borrow or regenerate the managed parent's arguments.
The Java launcher parses its own flags. The bridge admits classpath/main and
`-jar` commands; module entry points and pre-main `@argfile` expansion are not
host APIs. Unsupported host/bootstrap calls remain rejected. No shell command
is constructed.

On Unix the bridge replaces itself with Java, preserving the process PID and
inherited pipes. On Windows it starts Java, waits, and returns its exit code;
the managed parent's existing tree cancellation owns both bridge and Java child.
Natural game exit leaves the CA analysis app available as the mod intends.
Created/running/terminal snapshots, cancellation, leases, exit codes and managed
session identity continue to describe the originally launched JVM.

CA's auxiliary Java app can outlive that managed game. The bridge independently
enrolls it in the existing managed-runtime use protocol before Java starts. It
derives the component from the canonical selected executable's containment under
a `.nexa-java-jobs` root and requires existing `.nexa-java.lock` and
`.nexa-java-uses/<component>` enrollment with a GUID lease. It never creates these
ownership directories or treats a directory location as permission to remove Java.
If those managed markers exist but the parent GUID enrollment has already retired,
the bridge refuses the auxiliary Java launch rather than continuing without a lease.
CA can request immediate game exit after spawning its bridge, so the bridge may first
run after the game's parent lease is removed. This conservative refusal closes that
scheduling race without inferring new enrollment authority from an empty directory.
External Java without an existing managed enrollment structure continues to launch
without a launcher-managed lease.
All admitted paths reject symbolic links. Creation takes the existing installer
root lock and creates a fresh exclusive GUID lease in that component. A shared
internal Contracts codec retains the exact `NXJAVA01`, PID and UTC-start-ticks
20-byte record already consumed by Java removal/replacement. No service reference
is added to the isolated native host.

Unix binds the bridge's actual PID/start time before `execv`; Java retains that
identity and its durable lease survives the close-on-exec file handle. Existing
removal checks reclaim the record only after exit or proved PID reuse. Windows
binds the actual Java child, retains uncertain records, and removes its record
after confirmed child exit. Preparation/failed exec without a created Java child
can remove its own fresh record. Abrupt host exit during child handoff leaves
the same conservative partial-record exclusion as ordinary managed launches.
Unregistered external Java receives no launcher-managed lease. This auxiliary
runtime exclusion does not create a second managed game session.

Linux CLR `Process.StartTime` calibrates the boot clock separately in each
process. A native bridge and a later removal worker can report slightly different
UTC ticks for the same kernel PID lifetime. The Linux lease reader conservatively
retains every running PID; independently calibrated UTC birthdays do not prove
PID reuse. Only a confirmed exited/absent PID permits automatic reclamation on
Linux. A stale lease whose PID has been reused remains excluded while that PID
is running. Windows/macOS retain the original native birthday comparison. This
prevents clock calibration from permitting removal of the CA child's live runtime.
The persistent record stays `NXJAVA01`/20 bytes.

The platform identity reader recognizes Linux kernel `Z` (zombie) and `X`/`x`
(dead) states as exited even when the OS has not reaped an orphan PID. These
processes have ended and cannot keep using Java. It reads a bounded `/proc/PID/stat`
snapshot, verifies the PID and closing command delimiter, and maps malformed or
unreadable state to Unknown. It never infers exit from birthday differences on
Linux. This lets the existing guard reclaim the completed CA auxiliary record
in containers whose PID 1 delays reaping.

The JNI VM receives a main-class-only `sun.java.command` default when no explicit
VM option already supplies the property. This supplies CA's main class diagnosis
without materializing access tokens or any game argument as JVM metadata. Existing
manifest `${launcher_name}` / `${launcher_version}` properties and VM arguments
remain unchanged. WorkingDirectory equals resolved GameDirectory; instance,
client JAR, classpath and native paths remain absolute and separately identified.

## Windows native option encoding

JNI `JavaVMOption.optionString` uses the [default platform encoding](https://docs.oracle.com/en/java/javase/21/docs/specs/jni/invocation.html#jni_createjavavm), rather than an unconditional UTF-8 byte sequence. The host encodes Windows VM options with strict conversion, rejects substitution and best-fit conversion, and reports a fixed diagnostic without including option values. Unix options retain UTF-8. Game arguments still enter JNI through UTF-16 `NewString`; this native-option limit does not weaken that boundary.

The selected Java runtime determines the Windows encoding. OpenJDK 8 obtains `sun.jnu.encoding` from `GetLocaleInfo(systemDefaultLCID, LOCALE_IDEFAULTANSICODEPAGE)` ([source](https://github.com/openjdk/jdk8u/blob/master/jdk/src/windows/native/java/lang/java_props_md.c)); current OpenJDK 17/21/25 use `GetACP()` ([source](https://github.com/openjdk/jdk17u/blob/master/src/java.base/windows/native/libjava/java_props_md.c)). If these differ, the host reads the selected runtime's bounded `release`/`JAVA_VERSION` metadata and selects the documented path for Java 8, Java 17 before/after 17.0.5, and Java 21/25. [JDK-8272352 was backported in 17.0.5](https://github.com/openjdk/jdk17u/commit/011b96c37e694aca5ee82277d0634b843320d8f6); 17 GA and Java 18 GA still use LCID. Unknown divergent runtime behavior is refused for non-ASCII options rather than guessed; ASCII options remain valid. Java 8's unsupported `Cp65001` locale result is refused; its explicit codepage-zero UTF-8 case remains supported. Setting `-Dsun.jnu.encoding` after Java 8's native converter has initialized cannot repair the initial option decoding ([initialization order](https://github.com/openjdk/jdk8u/blob/master/jdk/src/share/native/java/lang/System.c)). No application manifest, private class-loader hook, runtime modification or alternate classpath copy is introduced.

A real external Windows `java.exe` has its own encoding limit. Its launcher converts the process command line through the Windows ANSI code page before Java receives it ([Java 8](https://github.com/openjdk/jdk8u/blob/master/jdk/src/share/bin/main.c), [Java 17](https://github.com/openjdk/jdk17u/blob/master/src/java.base/share/native/launcher/main.c)). The bridge preserves the exact UTF-16 process argv it supplies, but cannot make that separate executable accept characters outside its native encoding. Integration fixtures therefore keep real non-ASCII-and-space paths and exact non-ASCII child arguments representable by that runtime. They separately require arbitrary Chinese/emoji game arguments at the JNI boundary and arbitrary Unicode in the process-start contract. They also require unrepresentable VM options to fail without revealing the option or private credential. These checks must not be described as arbitrary-Unicode support in the external Java launcher.

## Windows Java process argument quoting

The Windows integration fixture explicitly selects `jdk.lang.Process.allowAmbiguousCommands=false` in its parent JVM. This selects OpenJDK's `VERIFICATION_WIN32_SAFE` path for native `.exe` children. Its legacy default surrounds spaced arguments with quotes without escaping their embedded quotes; `literal "quotes"` consequently arrives as `literal quotes` before the native bridge can read it. Directly starting the real Java executable has the same behavior. The safe path escapes embedded quotes and their preceding backslashes ([Java 8 implementation](https://github.com/openjdk/jdk8u/blob/master/jdk/src/windows/classes/java/lang/ProcessImpl.java), [Java 17 implementation](https://github.com/openjdk/jdk17u/blob/master/src/java.base/windows/classes/java/lang/ProcessImpl.java), [Java 25 implementation](https://github.com/openjdk/jdk25u/blob/master/src/java.base/windows/classes/java/lang/ProcessImpl.java)).

The host preserves the user's JVM properties and Java's default process behavior. It does not add quotes or infer characters missing from a child argument. The bridge continues to forward its received argument vector through `ProcessStartInfo.ArgumentList`. The fixture asserts its explicit quoting mode and exact empty, spaced, non-ASCII, embedded-quote and shell-metacharacter arguments in the actual Java child. This tests the bridge with the exact argument vector supplied by the caller; it does not claim to repair arguments already changed by an upstream Java quoting mode. Unix process creation is unchanged.

## Windows child standard handles

The Java-to-bridge process start supplies explicit inherited standard handles. A no-console Windows bridge must also supply them when it starts the real Java executable. The .NET process implementation sets `STARTF_USESTDHANDLES` only when at least one stream is redirected ([implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Windows.cs)); `CreateNoWindow` alone does not reproduce Java's inherited-handle setup ([Java implementation](https://github.com/openjdk/jdk/blob/jdk-25-ga/src/java.base/windows/native/libjava/ProcessImpl_md.c)).

The Windows bridge redirects child stdout and stderr and forwards both concurrently as raw byte streams through fixed-size buffers to its own inherited standard handles. It starts both drains before waiting for child exit, then completes the drains before returning the original child exit code. Stdin remains inherited: enabling the output redirects causes .NET to pass its existing native stdin handle explicitly too. No output text is decoded, accumulated or rewritten. Unix keeps the existing `execv` handoff. Runtime leases, natural parent exit and tree cancellation retain their original ownership.

## Windows auxiliary process ownership

The Windows auxiliary bridge joins a private Job with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` before it creates real Java. The Job handle is not inheritable, and children enter the Job atomically through normal Windows process creation. Killing the auxiliary bridge therefore closes its Job and terminates Java and descendants without depending on a process-tree snapshot. The existing .NET tree operation [requires strictly increasing parent/child creation times](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Win32.cs), which cannot be assumed for a rapid native bridge-to-Java handoff. Existing CI logs established a failed tree-stop assertion without pinned process identities or creation timestamps; they did not identify which race occurred.

Natural game exit leaves its auxiliary bridge alive. After real Java finishes and both output streams drain, the bridge explicitly clears kill-on-close before closing its own Job, preserving the real Java exit code and natural detached-child behavior. A bridge failure emits the fixed diagnostic before terminating its owned Job. Failed Job setup does not start Java. This follows the existing Windows hook-worker Job ownership pattern without adding a project reference to Platform or changing the managed game's session identity.

The fixture publishes PID and survival markers by writing a same-directory temporary file and atomically moving it into place. `Files.write` alone creates a visible empty file before its content is complete; casting that empty PID to an integer can produce zero, making an unrelated system process look like a surviving Java child. Readers require a complete positive PID.

The native fixture pins the actual Java and auxiliary bridge process identities before cancellation or orphan completion. Its orphan-survival check runs after the original game exits and uses an explicit observer-ready handshake before the auxiliary Java may finish. Cleanup waits for the real Java process and the bridge that owns the redirected output files; a survival-marker write or a PID lookup alone is not proof that those file handles are closed. Cancellation still requires the original tree stop to terminate the pinned child identities within the existing deadline. Failure cleanup cannot turn a failed cancellation assertion into a pass.

## Validation and limits

Contract tests cover shared/isolated directory identity and absolute inherited
client/classpath paths, bootstrap argv privacy, and exact argument/environment
roundtrip. The native JVM smoke fixture recreates CA's executable lookup and
child invocation using repository-owned Java source, verifies Java process
identity, stdout/stderr, Unicode/empty/spaced argv, child exit, normal parent exit,
and process-tree stop. CI already runs the native host on Windows/Linux Java
8/17/21/25 and macOS x64/arm64 Java 21.

The compatibility fixture compiles from its Unicode-and-space temporary directory
using only relative ASCII compiler arguments. On Windows, the non-ASCII directory and external Java child argument are selected by strict roundtrip through both native encodings; the Unix directory retains Chinese and child arguments include Chinese and emoji. The JNI parent separately checks fixed Chinese/emoji arguments on every platform. Windows `javac` launchers may decode
native command-line paths using the machine code page before Java starts, so an
absolute Unicode `-d` argument can become `??` independently of the JNI host. The
compiler process receives its working directory through the native process API;
the resulting classes, bootstrap classpath, game working directory, and child
arguments retain their original Unicode paths. Parent and child assertions verify
those directory identities. This avoids a compiler setup limitation without
removing the host's Unicode-path and exact-argument compatibility checks.

These source and fixture checks do not establish a live CA GUI or a complete
Fabric/Forge/NeoForge/Cleanroom/LWJGL compatibility matrix. A real game crash,
CA GUI/upload, platform window behavior, and detached relauncher session adoption
remain runtime acceptance checks. No private CA API, launcher-specific log export,
mod-generated argument redaction guarantee, or automatic second session is claimed.
