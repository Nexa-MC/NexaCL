# Window and direct pointer input

Native window movement and resizing remain backend effects. The renderer supplies
the authoritative hit target; native chrome must not infer a draggable caption from
an underlying title rectangle hidden by a dialog or another input target.

On Windows and macOS the custom title projects a native `TitleBar` hit region.
Interactive title children and native window buttons remain client input. Windows
therefore receives `HTCAPTION` for exposed title content and can own mouse/touch
movement and snapping. The client pointer route remains a fallback. Both startup
window forms share primary-contact header dragging. Secondary contacts must never
enter a native move/resize operation.

The eight 8-DIP edge/corner grips explicitly project the corresponding native
resize role; they must not depend on coinciding with a platform's physical frame
thickness. Only a resizable Normal window exposes these hit regions. Maximized,
fullscreen, minimized and non-resizable states retire both the role and client
hit interception, allowing underlying caption/content input to remain reachable.

The scene accepts one pointer gesture at a time. Only the admitted pointer may
move, release or cancel it; another finger cannot replace that pointer or activate
its pressed control. Losing capture, closing the window or disposing the scene
retires the gesture and capture. A later contact can start a fresh gesture.

Scrollable product lists must author direct scrolling as well as wheel input.
Vertical list panning uses the renderer's threshold, nested-input arbitration and
existing inertia; a tap still activates its original control. Horizontal panning
must be verified separately rather than inferred from a vertical gesture flag.

Evidence must use Avalonia's real `TouchDevice` and platform raw touch events for
clicks, scrolling, ranges, multiple contacts and capture cancellation. Headless
window movement is a no-op and cannot establish native movement. The Windows
native chrome probe additionally checks actual HWND non-client hit results,
including interactive controls and retained-window tray restoration. Physical
touchscreen behavior remains separate from injected-event evidence.

## Horizontal and nested direct scrolling

Direct scrolling is enabled by the existing `XsrUiScrollGesture` component on
either overflowing axis. A press records the eligible scrolling ancestors without
activating a click or choosing an axis. Each candidate inverse-maps the current
pointer into its own layout coordinates before comparing it with the captured
local press. After movement reaches 8 local DIPs with a clear dominant local
axis, the nearest eligible ancestor overflowing on that axis owns the gesture.
This preserves the existing vertical threshold under theme scale and rotation;
rotating a vertical list by 90 degrees makes its visible horizontal pan scroll
its local Y offset. Ancestors with different transforms evaluate their own local
axes and thresholds rather than inheriting a child's decision.
A horizontal collection inside a vertical page can therefore pan
horizontally while a vertical movement scrolls its page. A container overflowing
on both axes locks to the first admitted axis until release or cancellation.

A captured scrollbar, range control, text edit or segmented thumb retains its
existing priority. Once a content scroll commits, it cancels an armed pager and
segment reveal motion; the same movement cannot both scroll a collection and
switch its page. An axis unsupported by the scrolling ancestors remains available
to the pager. A committed scroll clamps at its bounds and keeps ownership until
release, matching the existing vertical behavior.

The motion snapshot keeps its existing three-argument constructor and adds a
direction fact defaulting to vertical. Direction, velocity and the presentation-offset
get/set methods refer to that admitted local direction. The backend uses the same axis
for the release target and inertia writes. Cancellation, reduced motion, hidden
or retired content and a new press stop released motion on either axis.

## Validation

Linux Release validation on 2026-10-16 passes all 124 renderer tests and 42
Avalonia backend tests. The latter use actual raw TouchDevice input for taps,
horizontal/vertical scrolling, ranges, competing contacts, cancellation, hover
retirement and disposal. Renderer cases cover nested ownership, pager arbitration,
axis-specific inertia and local thresholds under rotation and scale. Architecture
checks pass for all 72 projects.

The Windows native-corner smoke additionally queries the actual HWND's
WM_NCHITTEST results for exposed caption, interactive content, native window
buttons and all eight resize grips, both initially and after tray restoration.
That platform smoke remains distinct from headless routing and physical-device
touchscreen verification.
