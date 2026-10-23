# XSR-826 — Startup readiness and native handoff

## Contract

The first visible window is the native startup window. Its dispatcher remains responsive
while storage recovery, composition, local state reads, controller preparation and native
render preparation run. Closing it cancels the startup attempt. An initialization failure
stays on that window and offers retry; a failed attempt never reveals the product shell.

Startup has two terminal readiness branches. First-run readiness requires the setup status,
setup controller, language/appearance projection and a prepared native setup scene. Normal
readiness requires foundation settings/profiles/accounts and state declarations, runtime
routes, the initial local instance scan, settings catalog/effective metadata, all attached
controller structures and the selected startup destination's finite initial queries. Local
recovery receipts finish before the initial instance scan. Sidecar discovery and protocol
registration finish once; local custom appearance assets, media engine validation/first
video frame, initial updater receipt/channel policy and initial system policy finish before
the last native preparation pass. The real shell remains hidden throughout this work.

A completed recovery scan with retained/conflicting snapshot receipts is an explicit
degraded outcome, not a launcher initialization failure. The shell remains available
for inspecting instances and logs, while automatic installation and Java recovery stay
paused for that attempt. No conflicting backup is deleted or forced through recovery.
Caller cancellation still cancels startup; mandatory settings/schema/native failures
still use the Splash failure/retry path. This distinction prevents a library recovery
warning from trapping users outside the launcher (observed on macOS Alpha 12).

The native preparation pass uses the actual shell window and scene controls. It measures
and arranges the initial viewport, drains state/controller projections, resolves text fonts,
decodes raster assets, resolves icon geometry and draws an off-screen frame. Handoff shows
that prepared window, transfers the main-window lifetime, then closes the startup window.
No main window may be shown by activation or preference callbacks before handoff.

The startup window's theme-aware neon identity and long-lived topmost presentation follow
[XSR-830](XSR-830-neon-brand-startup.md). Prolonged loading remains visually quiet and does
not repeatedly activate the window or schedule decorative motion.
Reduced-motion startup applies the ring policy before the card joins the native
visual tree, so an initially disabled animation never starts the shared clock.

## Finite work and reuse

Readiness names a finite set of initial facts; it does not enumerate all worlds, download
every catalog, open every wardrobe page or wait for installation/download completion.
Settings recovery, world inventories, resource detail, search, translation, skin preview,
Java detection and similar queries execute only when the actual startup destination needs
them. Queries for other destinations remain normal user-driven work.

Reusable online facts are read through the same runtime/cache path used by controllers:
fresh local snapshots are reused, retained snapshots can be displayed with a stale/offline
notice, and missing online facts reach an explicit offline/timeout result within a bounded
startup budget. Refresh uses the shared cache's single-flight path in the background;
startup must not launch duplicate provider requests or persist credentials, access tokens,
device codes or authentication results. A network outage cannot keep the startup window
open indefinitely.

Policy workers, rollout refresh, telemetry, task recovery/downloads and hint/animation
clocks have application lifetimes. Readiness observes only their initial finite policy
application or local facts, never their infinite worker tasks. Retry owns a new composition
attempt and disposes the failed attempt before creating another one.

## Ownership and checks

`DesktopStartupReadiness` orders and reports named steps. `Program.Startup` contains the
normal and first-run preparation recipes. Controller startup entry points consume their
existing query tasks and commit results into their real retained pages. The Avalonia
startup session owns hidden native preparation, cancellation, retry and the visibility
handoff. Services stay independent of Desktop and Avalonia.

Native thread ownership follows the platform. Windows retains its dedicated STA native
dispatcher, and Linux retains its dedicated native dispatcher. Cocoa must initialize and
run on the process main thread, verified by `pthread_main_np`. A synchronous executable
entry calls `AvaloniaUiStartupSession.RunApplicationOnMainThread(Func<Task<int>>)` before
async GUI bootstrap. On macOS this entry runs bootstrap on a worker and waits for either
its startup-session request or its terminal result; the entry thread then runs that
session's actual native application loop. The async session still becomes ready only
after the first splash render, and service composition never blocks the native loop.
Command-line completion, cancellation or faults before a native request return without
initializing Avalonia. Private process workers retain their existing direct entry path.

Each main-thread entry admits one native application lifetime. Session startup on macOS
without the synchronous main-thread entry is rejected before creating a dispatcher.
Bootstrap completion or failure requests native shutdown, including cancellation before
the platform is ready; this path does not post to an uninitialized dispatcher. The native
loop's completion and the bootstrap task are both observed before releasing entry ownership.
The entry seals native submission when bootstrap ends, so a late request is rejected and
retired without waiting for a loop that was never started. Rejected duplicate sessions and
native dispatch/thread-start failures clear their reservation and complete their terminal
tasks before releasing their cancellation source.

A composition root that already owns a startup session presents through
`AvaloniaUiShellHost.Run(startup, shell, platformActions)`. It never infers first startup
from the global active-session pointer: a canceled splash can already have retired that
pointer. Presentation of the owned canceled session propagates cancellation, preserves
single native lifetime ownership and disposes any opened icon stream. The existing
standalone `Run(shell, ...)` overload remains available for callers without a startup session.
Existing retry, topmost splash, hidden preparation and main-window handoff checks remain
required on the real platform.

Contract tests cover step order/deduplication, bounded optional initialization, cancellation,
mandatory failure and retained settings metadata before navigation. Native checks cover
retry, hidden activation, prepared scenes, settled missing media and lifetime ownership.
Compilation, runtime and AOT validation outcomes are recorded with the change's review.
