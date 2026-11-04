# NXML, NSS, and presentation

`src/rendering/` contains the semantic Renderer, NXML compiler/runtime, platform contracts, and backends. Product pages, dynamic fragments, components, styles, and localization reside in `src/app/Nexa.Desktop/Ui/`. Managed development and the released `nexa-ui` use the same product interface.

NXML defines structure, identity, roles, bindings, and intents. NSS defines styling. Renderer reads versioned state without resolving concrete services. Controllers project state, bind intents, and observe asynchronous completion; render paths never synchronously wait for IPC or IO.

The compiler checks component declarations, catalogs, and generated tables. Runtime control types are not inferred through reflection. Host-private IR and public extension UI contracts have separate versions; internal object layouts are not stable ABIs.

Layout, virtualization, and hit testing share generation-matched metrics and geometry. Scroll anchors, clipping, keyboard order, accessibility, and visible bounds must agree. Resources, input, animation, and native windows have explicit scopes. Closing a window promptly cancels work and retires resources asynchronously without waiting on the UI thread.

Distribute UI resources with the runtime. Verify light/dark themes, narrow windows, keyboard/controller navigation, failures, and actual native windows. See [themes](themes.md) and [Sidecar](sidecar.md).
