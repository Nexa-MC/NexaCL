# Launcher parity completion

Baseline: NexaCL `73b301319`; reference commits PCL `93ede5d9`, PCL CE
`a8589e07`, HMCL `85540268`. User authorization: complete the confirmed gaps.
No alpha publication is implied.

| Acceptance | State | Required behavior |
| --- | --- | --- |
| Concurrent startup | Verified | Simultaneous window barrier, independent prompts/cancel, native attempt control and UI navigation |
| Pre-launch deep cleanup | Verified | Opt-in persisted setting, post-preflight Host callback, overlapping cleanup and cancellation before JVM creation |
| Deep memory operations | Verified on Windows NativeAOT | Eight operations succeeded; six-sample target working-set comparison matched Mem Reduct 3.5.2 |
| Whole-instance Java discovery | Verified | Arbitrary nested Java homes; bounded traversal and compatibility |
| Relative Java selection | Verified | Explicit portable executable path, resolved against launcher location |
| Java major selection | Verified | Limit compatible automatic candidates to chosen major |
| Nide8 Java minimum | Verified | Enforce Java 8u141 minimum before selection/acquisition |
| MultiMC custom components | Pending | Preserve patches/JarMods and required component facts |
| Imported launch settings | Pending | Review/apply memory, JVM/game arguments and commands |
| MCBBS metadata and dual manifests | Pending | Preserve/export author, URLs, launch facts and compatibility manifest |
| Portable launcher pack formats/import | Pending | Nested pack inspection and complete verified closure |
| Independent launch scripts | Pending | Direct Java argv, usable dependencies and explicit private-data consent |
| Existing offline appearance editing | Pending | Local/remote skin/cape/model sources and cancellation |
| Region NBT | Verified | MCA/MCR chunk selection and dat_old detection |
| Resource pack game settings | Verified | Enable/order/options transaction and format compatibility |
| Schematic facts | Verified | Structure author/size, dimensions and volume |
| Server packs | Pending | Export, URL inspect/install/update, pinned file identity |
| Official server download | Pending | Verified server JAR and Windows/POSIX start files |
| Local/remote homepage | Pending | Declarative documents, refresh and bounded actions |
| Searchable help | Pending | Built-in/local/remote index and documents |
| Online rooms/tunnels | Pending | Explicit room membership, real provider readiness, lifecycle and joining |
| Additional platforms | Pending | SDK/runtime/native toolchain evidence for every claimed platform |

Completion is recorded with actual test results; a DTO, button or upstream TODO is
not acceptance. Unsupported platform toolchains and unavailable external services
are recorded explicitly rather than represented as working features.

## Current verified increment

Release solution build completed without warnings or errors. The current managed
regression run passed **889 service cases and 394 Desktop cases**; the complete
**889-case service suite also passed as a published NativeAOT executable**. Architecture
and repository layout checks cover **101 projects**. Independent Accounts and
Minecraft NativeAOT owner/caller tests passed, including the before-process Host
callback, scoped cancellation, resource-pack settings and region inspection. The
production NativeAOT UI module also published successfully. Windows memory evidence
and the pinned six-sample comparison are recorded in
[Windows memory cleanup](windows-memory-cleanup.md).

Rows still marked Pending are not represented as completed by this increment.
