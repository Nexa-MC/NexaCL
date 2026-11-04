# Native modules and distributed files

English | [简体中文](native-modules.zh-CN.md)

Official distributions do not require .NET installation. `Nexa.Desktop[.exe]` is the small NativeAOT Bootstrap from `src/app/Nexa.Bootstrap`. It loads `nexa-host` by an absolute install path and calls `nexa_host_main_v2` with UTF-8 arguments, install location, and canonical product version. Host admits module identities and enters `nexa-ui` on the original OS main thread.

| Distributed module | Responsibility | Source group |
| --- | --- | --- |
| nexa-host | Catalog, admission, opaque leases, shutdown | src/app |
| nexa-ui | UI tree, controllers, Renderer, backends, windows | src/app, src/rendering, src/runtime |
| nexa-accounts | Protected account storage, login, refresh, wardrobe | src/modules/accounts |
| nexa-network | HTTP/TLS, proxies, connections, downloads/resume | src/modules/network |
| nexa-minecraft | Versions, Java, installation, resources, instances, game lifecycle | src/modules/minecraft, src/modules/resources |
| nexa-updates | Release discovery, trust, differential/full transactions, Sidecar updates | src/modules/updates |
| nexa-pac | Bounded PAC interpreter | src/engines/pac |
| nexa-crypto | OpenPGP verification | src/engines/crypto |
| nexa-media | Image metadata and PNG validation | src/engines/media |
| nexa-markup | NXML and restricted XML parsing | src/engines/markup |
| nexa-artwork | Branding, icons, controller images | src/engines/artwork |

Extensions are `.dll` on Windows, `.so` on Linux, and `.dylib` on macOS. Windows also provides `ShellIntegration/Nexa.ExplorerCommand.dll`. `Nexa.Jvm.Host` and `Nexa.Update.Helper` remain separate executables. `Nexa.Native.Window` is a source ABI/verification project, not a twelfth distributed module.

Host admits Accounts, Network, Minecraft, and Updates. Production UI uses typed asynchronous clients and versioned replicas. Packaging checks that UI does not embed concrete service implementations and that Host does not link UI/services. Modules are not unloaded; shutdown rejects admission, cancels work, drains callbacks/resources, and exits.

`eng/release/publish_launcher.py` builds matching-RID Bootstrap, Host, UI, and modules. Engines have independent fixed image versions: a product-version-only change must not rewrite otherwise unchanged UI/engine images. `verify_engine_reproducibility.py` and `verify_ui_reproducibility.py` enforce this. A source-path migration may cause a first-build byte change without changing ABI compatibility.

Distribute the complete module/resource closure. Copying only the executable or replacing one module is unsupported. File inventories, signatures, and differential reconstruction cover the entire installation. Startup checks include unrelated working directories, missing modules, incompatible identities, and real-window shutdown.
