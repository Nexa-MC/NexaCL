# Native module distribution

The launcher retains NativeAOT. The desktop composition root, renderer and in-process
services form one native Host module: `nexa-host.dll` (Windows), `nexa-host.so`
(Linux), or `nexa-host.dylib` (macOS). Names of Nexa-owned distribution modules are
lowercase `nexa-<module>`. Upstream native dependencies retain their supported names.

`Nexa.Desktop[.exe]` becomes a small NativeAOT bootstrap with no Host project
references. It loads the Host from its own directory using an absolute path and
calls the versioned `nexa_host_main_v1` C ABI on the original OS main thread.
Arguments are UTF-8, caller-owned, valid for the duration of the call. Only integer
exit codes and unmanaged pointers cross this boundary. Exceptions are caught by
the callee. The Host is never unloaded. There is no managed assembly loading or
shared CLR-object ownership between the two NativeAOT runtimes.

The module owns its embedded credentials, resources and version metadata. It
initializes its base directory and Windows apartment before starting the existing
entry point. Existing workers, protocol arguments and CLI validation commands
continue through that entry point. Direct managed development builds remain valid.

This is a distribution boundary, not a claim that Renderer and Services have
independent native ABIs. Such a split would require explicit state, callback,
resource lifetime and buffer contracts before removing project references.

Publish builds both binaries for the same RID/version. Packaging rejects a missing
or empty Host library. Existing signed complete payload manifests and differential
packages cover the library like every other runtime file; modules must not be
updated independently of their matching launcher release. Native smoke tests must
exercise the distributed bootstrap, including a working directory outside the
installation and a missing-module failure.
