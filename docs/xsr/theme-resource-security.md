# Theme resource security

Decision date: 2026-10-14. Themes are untrusted declarations, even when unsigned
packages are intentionally supported. These limits constrain resource handling;
they do not introduce an application-update signature bypass.

## Admission budgets

| Input | Limit |
| --- | --- |
| Manifest | 64 KiB, JSON depth 16 |
| Archive/directory entries | 4096 |
| Compressed archive or one entry | 128 MiB |
| Actual expanded bytes | 512 MiB |
| Ordered style sources | 64 files, 1 MiB total |
| Options | 256 |
| Value text | 4096 characters |
| Option file collection | 256 values |
| Installed sources | 256 |
| Image/icon/font resource | 16 MiB; SVG also obeys the native 1 MiB parser budget |
| Native SVG structure | 4096 elements, at most 24 ancestors per element |
| Native SVG numbers and authored composed transform coefficients | Finite, absolute value at most 1,000,000 |
| Native SVG viewBox width/height | 0.000001 to 8192 inclusive |
| Native SVG transform attribute | 2048 characters, at most 32 operations |
| Prepared native resources | 256, 64 MiB combined; at most 32 font resources |
| Audio/video resource | 128 MiB, stable owned media lease |
| Directory playlist | 128 admitted top-level files, 256 MiB, 4096 enumerated entries |
| Resource leases per activation | 256, 512 MiB combined |
| Resource leases across active/candidate lifetimes | 512, 1 GiB combined |

Budget enforcement counts actual read/expanded bytes, not only ZIP headers.
Reject absolute paths, traversal, links/reparse points, duplicate names and
case-insensitive cross-platform collisions. Canonical file names cannot contain
Windows device names, drive/stream separators or trailing spaces/dots. NSS does
not import network URLs or additional source files outside the ordered manifest.
Executable files and NXML replacement pages are not theme content.

On macOS, the host may use the operating system aliases `/var`, `/tmp` and `/etc`
only when each alias is a direct link to its exact `/private` counterpart. These
closed aliases are canonicalized before source ownership, persistence, resource
grants and path comparisons. All other links, including links beneath those
directories, remain rejected; a different target at a known alias is rejected.

## Resource expressions and grants

`asset(path)` identifies an admitted file within the candidate's package/source.
`file(option-id)` and `folder(option-id)` identify explicit user selections. The
host resolves option values under grants; the expression does not itself provide
permission. Grants bind canonical path, file/directory scope, resource kind and
source identity. Selecting an Audio file does not authorize Font or Image reads.

Same-ID upgrades retain installation identity and compatible options, but identity
alone does not transfer grants. A candidate from a different origin path or source
kind cannot read its predecessor's external resources. Successful replacement revokes
those grants and the old directory watch; cancellation preserves them. Same-origin
updates retain existing grants. Imported themes cannot retire the host-owned built-in
theme, including when loading a legacy catalogue with colliding IDs.

A directory grant is bounded enumeration within the selected canonical tree;
links cannot escape it. Inputs are checked at use, not only when a settings form
was opened. A failed, replaced, revoked or absent grant returns a resource failure
and does not fall back to reading the raw path. Options import never imports grants.

The one-time legacy migration recognizes prior explicit host selections for the
built-in source only. It retains that existing host authority under source-bound
grants; invalid, missing or linked selections remain in retained legacy values and
use safe resource defaults. This compatibility path does not authorize a third-
party package or convert an options import into a resource grant.

`appearance.resources` identifies finite host slots. Each entry declares
`expression`, `kind`, `required`, and optional `fallback`. Optional resources must
have a fallback; `none` means retain the host's safe empty/default presentation.
A resource required for activation must prepare successfully before commit.
If preparation or decoding of an optional primary resource fails, its candidate
leases are retired before preparing and decoding its declared fallback. A valid
`asset(...)` fallback is bound to the same activation generation, including NSS
references to that primary expression. Failure of the fallback rejects the
candidate and preserves the last known good theme. An NSS reference to a resource
whose slot falls back to `none` remains an independent required style resource;
the empty slot does not make that reference optional.
Unavailable optional media cannot attach a resource from an older generation and
claim that it belongs to the new theme.

## Decoding and leases

Consumers receive typed payloads and resource leases, not unrestricted filesystem
authority. A lease is bound to source and activation generation. Media engines
that require a path use a host-owned stable prepared file, never an arbitrary
theme-provided process argument. Decoders and playlists retire with the generation;
revoke stops current use and releases retained bytes, handles and subscriptions.

SVG is a finite graphics format: bounded geometry, fill/stroke, gradients and
group transforms. Script, external URLs, image/use references, rendered text, CSS, filters
and foreignObject are excluded. Unsupported syntax is rejected, rather than
silently dropped. Number bounds apply to viewBox origins as well as geometry.
Transform bounds apply after each operation and after composition with ancestor
transforms; individually bounded scales cannot multiply into an unbounded matrix.
Nested SVG viewports are rejected. The native renderer prepares one normalized
`DrawingGroup` per vector resource. Tile fit repeats that prepared picture through
an Avalonia `DrawingBrush`; the host does not enumerate tiles or reconstruct each
tile's shapes when the viewport grows or the authored viewBox shrinks.

Fonts use admitted TTF/OTF data and a backend measurement/shaping
bridge; WOFF/TTC are excluded and no theme-managed native library is loaded.
Native raster formats include PNG, JPEG, WebP, BMP, ICO and static GIF under the
source/decoded pixel budgets; animated GIF is rejected. Image/video/audio retain the
host's existing format, dimension, residency and decoder budgets.

Consumers acknowledge the replacement appearance generation before old media
leases are released. Required media uses actual FFmpeg decode preflight (bounded
audio samples or one video frame), including when its playback toggle is off.
FFplay/audio output availability is a separate playback fact. Retirement releases
owner references; a resource becomes disposed after its final scene borrow ends,
with outgoing paint restricted to its exact generation.
Prepared native `Borrow`, `ReleaseBorrow` and owner `Dispose` transitions share one
lifetime lock. Retirement is idempotent, final release destroys resources exactly
once, and a disposed generation rejects new borrows. A retired generation may
remain usable while its scene/control borrows are retained. The activation actor
and UI-thread releases therefore share the same decision to admit a borrow or
destroy the retired native resources.

## Protected policies

Themes cannot lower reduced motion, safe mode, hardware-recovery controls,
password semantics, enabled/visible business policy or window/tray lifetime rules.
Window opacity/material declarations remain within platform-supported policy.
Runtime tray/window icons and splash artwork are separate from installer icons;
theme activation does not rewrite installation metadata.

The recovery UI and safe-mode startup are host-owned. Native material support is
capability checked, with an explicit fallback where unavailable. The custom chrome
and tray restore contract remain enforced even when a theme changes presentation.
