# macOS startup and title-bar rendering probe

The one-off `macos-render-once.yml` workflow runs on native Apple Silicon and Intel
runners. It is triggered by its initial workflow-file push and explicit dispatch;
ordinary source pushes do not schedule this additional matrix.

The probe uses the real Cocoa startup session, shell window and renderer, and
exports `RenderTargetBitmap` PNGs plus native window, surface, title and traffic-light
geometry for light/dark appearance, resize and retained-window restoration.
These are offscreen renders of native windows, not desktop-compositor captures.

Separately, the workflow starts the published Alpha 7 NativeAOT application with
isolated first-run and configured data roots. Process output, visible native window
bounds, bounded process samples and crash reports distinguish product startup from
the managed rendering harness. Existing validation-only entry points cannot replace
this normal-startup check. No user accounts, game installation or existing data are used.

This initial diagnostic records the unfixed behavior. Product changes and executed
before/after evidence will be recorded here after inspecting the native results.
