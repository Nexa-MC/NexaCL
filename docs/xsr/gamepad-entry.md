# Controller entry presentation

This unit implements controller discovery and the entry presentation only. The
future Steam-style large-screen workspace has no navigation or product content
yet. Presence alone never enables controller mode.

Desktop owns the native input lifetime and the gesture state machine. The renderer
receives an NXML modal overlay and immutable image recipes, with no native input
polling or service access. Known Sony, Xbox and Wii U Pro layouts select the three
user-supplied drawings; unidentified pads use the offset layout without claiming a
brand. Original SVGs remain editable assets. Their bounded SMIL path/opacity motion
is baked into reusable small sprite sheets, not reparsed on each presentation
frame. Each sheet fits the renderer's existing critical-memory admission budget.

After a pad is discovered, darken the complete client area and loop its inward-stick
demonstration with “左右摇杆内推以进入手柄模式”. Both axes must first be neutral,
then the same device must hold left X >= .65 and right X <= -.65 for 200 ms, with
vertical axes within .45. Different devices cannot combine halves of the gesture.
Drift, already-held input at attachment and background input cannot activate it.

Confirmation fades the prompt away and smoothly enlarges the existing window to
the current monitor's full bounds over 600 ms. The entry then follows one bounded
5.8-second timeline: hold black, reveal the **outer tile's upper-left rounded
corner** as a planet-like dark horizon, let a soft sunrise emerge from behind its
blue atmospheric rim, pull back smoothly to the center and settle,
slide left to reveal NexaCL, then lift the lockup and reveal “手柄模式” underneath.
The final lockup stays on a black canvas. Lighting falls from upper left to lower
right. The supplied brand artwork remains unchanged; reusable 512px textures
provide the final lighting treatment and soft sun, and one 1024px texture uses
the supplied SVG's plate outline for the dark silhouette and atmospheric rim
(`eng/media/bake_gamepad_entry.py`). The opening shows the rounded plate edge;
the N letter remains hidden until the camera pulls back. The sun renders behind
the opaque silhouette and rises outward across the edge. A black veil reveals
the dawn gradually, rather than displaying a cross-shaped flash over the icon.
NXML places the oversized image carrier outside the viewport; native clipping and
the existing smooth complete-image sampler perform the close-up. The immutable
source textures stay bounded (12 MiB combined native admission at full size).
All layout/opacity values remain NXML projections; no
renderer API or bitmap budget is changed. Zero-opacity stages explicitly hide the
image carrier so the black hold contains no image pixels.
No large-screen navigation is introduced here. Escape
or the pad's back/cancel button restores the original window state and focus.
Disconnect removes the prompt or exits the entry canvas. Dismissed devices do not
immediately prompt again until reattached. Reduced motion shows the final lockup
immediately, without flash or zoom, and uses a static demonstration. Native readers
and timers are disposed with Desktop.

## Motion design

The camera follows [Fluent 2's duration/easing guidance](https://fluent2.microsoft.design/motion)
(large elements and long travel need enough time; ease-in-out starts slowly and
settles slowly) and [Adobe's speed-graph guidance](https://helpx.adobe.com/after-effects/desktop/animate-in-after-effects/speed-between-keyframes/speed.html)
(control acceleration/deceleration and keep incoming/outgoing velocity continuous).
The 1.8-second camera duration is a product design choice for this cinematic
sequence, rather than a duration prescribed by either reference. The former
600ms quartic ease-out discarded approximately 87% of the scale in its first 100ms.

| Stage after confirmation | Time |
| --- | --- |
| Window expansion, blackout, sunrise | 0–2100ms |
| Camera pullback | 2100–3900ms |
| Centered rest | 3900–4250ms |
| Logo slides left; NexaCL appears | 4250–4930ms |
| Lockup rises; subtitle appears | 5150–5750ms |

The camera interpolates logarithmic scale with a quintic ease-in-out whose
velocity and acceleration are zero at both ends. The illuminated arc remains the
screen-space anchor; late recentering moves it monotonically by only its final
offset. It does not sweep sideways and snap back during the large scale change.
Brand/atmosphere crossfade and the sun's retirement follow camera progress, so
changing duration cannot leave disconnected lighting or prematurely expose the N.
The controller intro samples at the configured UI animation rate (60Hz by default)
instead of a fixed 50Hz. Input remains time-based and reduced motion still settles
immediately. NXML owns every position, size and opacity; no backend API is added.

Native readers use Windows XInput plus nonexclusive Raw Input HID, Linux's
nonblocking joystick interface, and macOS GameController extended gamepads. Sony
HID Z/Rz axes are distinguished from Rx/Ry analog triggers. HID logical ranges
are normalized before applying the gesture thresholds. Devices inaccessible to
the OS reader cannot activate entry; no guessed input or elevated access is used.

## Verification and preview

The Desktop suite passes 367 tests, including neutral/held/unready input, mixed
devices, background input, cancellation/suppression, reconnection, disposal,
layout selection, shared artwork, cinematic phase ordering, gentle camera
endpoints, monotonic rim anchoring, configured frame rate, stable final placement
and reduced motion. Native offscreen capture
under X11 with a window manager checks all three layouts in both color schemes,
decoded controller/icon/sun/horizon pixels, the black hold, dark-side occultation
and giant rounded-edge close-up, actual
fullscreen bounds/state, restoration from normal
and maximized windows, and subsequent unmaximize bounds. Input is simulated for
these deterministic captures; physical Windows/macOS controllers remain untested.

The native window must submit its normal-size layout before re-maximizing on exit,
so the window manager does not retain the fullscreen animation's final size as the
next normal restore size. Native title controls follow the client scrim, including
the macOS standard window buttons.

![Native controller entry preview](../previews/gamepad-entry/entry.gif)

The GIF includes the prompt, window expansion and complete entry timeline. Static
captures for each layout/theme, black hold, flash, giant corner, pullback, centered
icon, name and final lockup are stored beside it.

The new captures sample at 50fps rather than 20fps. `camera.gif` and `camera.mp4`
isolate the fullscreen camera sequence so capture/PNG encoding cannot compress
the native window-expansion clock into a misleadingly short clip. This Linux
offscreen run measured CPU projection/layout at 1.68ms median, 10.61ms p95 and
21.97ms maximum; these exclude native drawing and PNG encoding and are not a
hardware-independent 60fps guarantee. Metrics are stored beside the previews.
The GIF uses one shared palette across frames to avoid quantization flicker;
the MP4 preserves smoother gradients for evaluating the shot. Export an existing
native capture with `python eng/media/export_gamepad_entry_preview.py <capture-directory>`
(Pillow and ffmpeg are only media-generation tools, not launcher dependencies).

Release compilation, 72-project architecture checks, format verification and
Linux NativeAOT publication plus `--validate-shell` pass. NativeAOT retains the
existing third-party `Jint` `IL2098` warning. Already-fullscreen windows keep their
native fullscreen state while playing the entry icon animation.
