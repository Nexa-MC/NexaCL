# macOS startup and title-bar rendering probe

The one-off `macos-render-once.yml` workflow runs on native Apple Silicon and Intel
runners. The initial workflow-file push registered the diagnostic on this branch;
it now accepts only explicit dispatch. Ordinary source pushes do not schedule it.

The probe uses the real Cocoa startup session, shell window and renderer, and
exports `RenderTargetBitmap` PNGs plus native window, surface, title and traffic-light
geometry for light/dark appearance, resize and retained-window restoration.
These are offscreen renders of native windows, not desktop-compositor captures.

The baseline ran the published Alpha 7 NativeAOT application. The verification
workflow publishes the current source with the release client certificate and
replaces the released bundle's executable payload, then re-signs and verifies the
entire bundle. It starts isolated first-run, configured and safe-mode data roots.
Process output, visible native window
bounds, bounded process samples and crash reports distinguish product startup from
the managed rendering harness. Existing validation-only entry points cannot replace
this normal-startup check. No user accounts, game installation or existing data are used.

## Confirmed causes and compatibility decisions

The published Alpha 7 ARM64 and Intel normal-startup probes reach an error Splash in both
configured and safe-mode scenarios. Its stack identifies
`X509CertificateLoader.ValidatePlatformKeyStorageFlags` in `CloudflareApiClient`:
macOS does not support `EphemeralKeySet`. Certificate import remains owned by
Composition: macOS uses `DefaultKeySet` (a temporary keychain managed by .NET),
Windows keeps `UserKeySet` for Schannel, and Linux retains `EphemeralKeySet`.
The client identity and mutual-TLS behavior must remain enabled.

Native-window offscreen PNGs show a 73-pixel strip of page background behind the
traffic lights in both appearances. Native button geometry correctly requests a
73-pixel safe area, but applying it as TitleBar padding moves the renderer's painted
content rectangle. Reserve that space with an unpainted leading layout child instead:
the TitleBar surface continues to cover the entire window width while its contents
stay clear of the native controls. Keep renderer-wide padding semantics unchanged.

Verification must include PKCS12 private-key import, full-width title geometry and
native offscreen images, plus normal startup of the freshly published NativeAOT
application on both macOS architectures. The one-off workflow remains manually
dispatchable after the initial diagnostic and does not publish a release.
