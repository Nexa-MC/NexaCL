# Launch failure investigation, 2026-10-29

Reported launcher logs show three independent failures: asset-index repair denied by
mainland download policy, Microsoft refresh TLS interruption, and a Java 25/Fabric
26.3 process exiting while loading LWJGL. Both supplied crash reports identify an
nonexistent java.library.path directory and a missing lwjgl.dll. The directory alone
is not proof of a bad argument: modern libraries can self-extract from classpath. Pdh/Wtsapi32 failures in the report
footer are secondary diagnostics, not sufficient evidence for the primary crash.

Keep the mainland download policy and authentication ownership checks intact. Local
repair rejection must identify the missing/unverified index and direct the user to
complete the version in Microsoft Launcher or import a complete installation. OAuth
refresh retries only transient transport/timeouts or transient HTTP responses, with
bounded attempts, recreated requests, cancellation and no certificate bypass. Network
failure must preserve the saved profile and explain retry instead of demanding login.
Native-path changes require the actual manifest arguments or a reproducing fixture.

The supplied 26.3 manifest declares native JARs as `downloads.artifact` and asks
LWJGL to extract resources into `${natives_directory}/lwjgl`; `java.library.path`
points at the separate `java` directory. These artifact JARs must remain on the
classpath. The resolver previously marked them native and the classpath planner
removed them. Add an opt-in `IncludeOnClasspath` flag to library tokens for explicit
artifacts; keep legacy `natives` classifier extraction and defaults unchanged.
Preserve archive directory structure (including macOS bundles), rather than flatten
all native archives to compensate for the absent classpath resource.

Validation: Release build completed with zero warnings/errors; 36 selected services
cases passed (privileged/keyring cases reported their existing environment skips),
and architecture checks passed for 72 projects. Regression coverage includes modern
native artifacts on all three desktop platform manifests, legacy native extraction
and ARM64 exclusion, refresh retry exhaustion/cancellation/certificate rejection,
retained account state, and mainland local-index rejection without network traffic.
The existing cross-platform NativeAOT CI job now explicitly runs these regressions.

A Linux JVM smoke using the official LWJGL 3.4.3 core and natives-linux JARs and the
reported `natives/java` / `natives/lwjgl` arguments failed with UnsatisfiedLinkError
when the native JAR was absent from classpath, then loaded and allocated native
memory successfully when it was included. This verifies the resource-loading cause;
it is not an end-to-end Windows Minecraft/Fabric play session.

Linux x64 NativeAOT publication also succeeded and all 13 targeted native-binary
regressions passed. Publication emitted one IL2098 trim warning in the Jint dependency
(`ReflectionAccessor.ConvertValueToSet`); no change to that dependency is included.
