# Standalone plugin ownership

The launcher builds and runs without plugin SDK packages. Plugin behavior belongs
to [PluginSidecar](https://github.com/Nexa-MC/PluginSidecar); author-facing contracts
and tutorials belong to the [SDK](https://github.com/Nexa-MC/NexaCL-Plugin-SDK).

The Sidecar owns guest loading, permissions, business translation, encrypted
installation records and UI publication. Its navigation is an ordinary NXML
extension. The Host exposes the same authenticated, generated binary catalogs
to every admitted Sidecar and contains no plugin adapter or fixed plugin page.
See [the generic Host contracts](sidecar-host-business.md).

A missing, disabled or failed Sidecar does not prevent launcher startup. Safe
mode disables Sidecars at the generic session boundary. Removing the old SDK
feed preparation from Host builds is deliberate; building the launcher must not
require access to the private plugin implementation repository.

The former embedded-owner tests are preserved in Git history. Guest lifecycle
and behavior tests now belong to PluginSidecar, including real Wasm composition,
UI actions and retirement. Host tests continue to exercise native contract and
resource semantics. Linux NativeAOT checks do not replace Windows and macOS
validation or establish production Market compatibility.
