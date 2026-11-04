# Launcher parity completion

Baseline: NexaCL `73b301319`; reference commits PCL `93ede5d9`, PCL CE
`a8589e07`, HMCL `85540268`. User authorization: complete the confirmed gaps.
No alpha publication is implied.

| Acceptance | State | Required behavior |
| --- | --- | --- |
| Whole-instance Java discovery | Pending | Arbitrary nested Java homes; bounded traversal and compatibility |
| Relative Java selection | Pending | Explicit portable executable path, resolved against launcher location |
| Java major selection | Pending | Limit compatible automatic candidates to chosen major |
| Nide8 Java minimum | Pending | Enforce Java 8u141 minimum before selection/acquisition |
| MultiMC custom components | Pending | Preserve patches/JarMods and required component facts |
| Imported launch settings | Pending | Review/apply memory, JVM/game arguments and commands |
| MCBBS metadata and dual manifests | Pending | Preserve/export author, URLs, launch facts and compatibility manifest |
| Portable launcher pack formats/import | Pending | Nested pack inspection and complete verified closure |
| Independent launch scripts | Pending | Direct Java argv, usable dependencies and explicit private-data consent |
| Existing offline appearance editing | Pending | Local/remote skin/cape/model sources and cancellation |
| Region NBT | Pending | MCA/MCR chunk selection and dat_old detection |
| Resource pack game settings | Pending | Enable/order/options transaction and format compatibility |
| Schematic facts | Pending | Structure author/size, dimensions and volume |
| Server packs | Pending | Export, URL inspect/install/update, pinned file identity |
| Official server download | Pending | Verified server JAR and Windows/POSIX start files |
| Local/remote homepage | Pending | Declarative documents, refresh and bounded actions |
| Searchable help | Pending | Built-in/local/remote index and documents |
| Online rooms/tunnels | Pending | Explicit room membership, real provider readiness, lifecycle and joining |
| Additional platforms | Pending | SDK/runtime/native toolchain evidence for every claimed platform |

Completion is recorded with actual test results; a DTO, button or upstream TODO is
not acceptance. Unsupported platform toolchains and unavailable external services
are recorded explicitly rather than represented as working features.
