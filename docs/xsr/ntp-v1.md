# Nexa Theme Pack v1 (NTP/1)

Architecture decision, 2026-10-14. Scope: the complete NTP/1 implementation requested
by the user, including package/folder sources, dynamic NSS, theme options, resources,
advanced rendering and platform adaptation. A package-import-only slice is not NTP/1.

## Boundary

Themes are unsigned ZIP packages or development directories. They contain a bounded
manifest, NSS and resources; no NXML replacements, scripts, executable code, business
commands or state bindings. Product-update and executable signature policies are
independent. Themes services do not reference Renderer, NSS or Avalonia. Desktop
coordinates preparation; Renderer receives a validated style program and typed
resource facts. UI settings use host-authored external NXML templates.

NXML remains the authority for recipe, identity, role, commands, bindings, ownership,
modal/anchor/dismiss semantics, enabled/visible/selected/password state. A dedicated
style-only transaction applies theme presentation. User reduced motion, safe mode,
hardware-acceleration recovery and native window lifetime policies take precedence.

## Container and manifest

The root `mimetype` is the UTF-8 literal `application/x-nexa-theme` (an optional final
newline is accepted). The root `theme.json` uses `format: "NTP/1"`, a bounded stable
`id`, dotted `version`, localized `name`, `styleApi: 1`, `nss: "NSS/1"`, ordered
`styles`, required/optional capabilities, typed `options` and finite `appearance`
resource fields. Unknown executable/behavior declarations are rejected. Optional
unsupported capabilities take declared fallbacks; required unsupported capabilities
reject activation. Host OS default mapping is separate from system light/dark mode.

Limits: manifest 64 KiB/depth 16; 4096 archive entries; 128 MiB compressed/per entry,
512 MiB actual expansion; 64 NSS files and 1 MiB total NSS; 256 options; bounded
4096-character values. Reject traversal, absolute archive names, links, duplicate and
cross-platform ambiguous paths. Resources and folder enumeration have explicit type,
size and count budgets. Both package and folder sources use the same validation.

`asset()` resolves within admitted package identity. `file()` and `folder()` require
explicit user-selected grants bound to installation/source identity, resource kind
and canonical path. Theme ID alone cannot inherit a different package's grants.
Imports do not convey external-read grants. Revocation/retirement releases resource
leases, decoders, playlists and watchers; stale generations cannot reattach them.

## Styles and activation

Retain authored style identity and base-source metadata through NXML compile/load and
dynamic reload. Computed presentation is separate from original host state. Legacy
NSS remains accepted. NSS/1 adds typed tokens/options, bounded conditions and selectors,
responsive rules, resource expressions and finite keyframes. No script or network
imports. Every declaration is validated even when no node currently matches.

Runtime style cache keys include theme/option/environment/resource/font revision,
entity generation, interaction state and relevant parent/viewport dependencies.
Unchanged frames do not parse NSS or query settings. Paint changes invalidate paint;
layout/font changes invalidate measure/arrange and paint. RGB guessing is restricted
to the built-in compatibility layer; third-party computed colors are literal.

Candidate admission -> manifest/permission validation -> NSS compile -> required
resource preparation -> render-thread same-generation activation -> persistent
last-known-good commit. Failures retain the last valid theme. IO/native effects use
candidate journal and compensation rather than assuming cross-process atomicity.
Folder watches debounce and retire with their scope. Theme preview is temporary.
A->B->A is distinguished by monotonic activation generation, not theme ID.

Virtual layouts consume same-generation shared metrics, retain item/scroll anchors
and realization limits. Grid/order/transform must agree with hit testing, clipping,
keyboard order and accessibility without changing tree ownership. Font support uses
an independent measurement/shaping bridge and deterministic fallback. Material and
animation capabilities require actual backend consumption and visible evidence.

### Virtual metrics and frame ordering

The renderer exposes a finite `ThemePreparing` hook before the ordinary `FramePreparing`
projection pass. Desktop commits an already prepared same-generation candidate there; it
performs no recursive render. Existing host projections then consume the new virtual metrics
before measure, arrange and scene publication. Candidate errors remain owned by the Desktop
activation journal and completion result; the renderer does not swallow arbitrary callbacks.

`RegisterVirtualLayout` admits dedicated `ItemWidth`, `ItemHeight`, `ItemSpacing` and
`ItemColumns` facts. These do not overwrite a container's viewport height. Linear catalogs
admit one column; screenshot waterfalls admit 1–12 columns. Their metrics include generation,
program revision and a realization limit. Screenshot height is an optional minimum image
height; natural image aspect geometry remains the default. Host source templates project row
heights, widths, margins and spacer extents from the same metrics through retained Dynamic
reload. Registered virtual containers and protected realized rows/spacers give their host
geometry higher priority than generic theme width, height, bounds, margin, padding, flow,
grid, order, position and transform declarations. Every declaration is validated before this
policy removes those overrides; legal broad selectors remain safe when a catalog opens later.
Dedicated item metrics remain admitted only at registered containers. Protected rows clip
nested content; their paint, font and nested typography remain themeable. Fonts and layout
invalidate measure and arrange; paint-only changes preserve measurements. Unchanged frames
reuse the computed cache.

A style or grid-placement failure first uses the dedicated `XsrUiThemeStyleException`, never
a general host exception. Candidate admission validates current retained parent/child grid
placements without mutation. Grid column spans must fit the parent's admitted tracks;
explicit placements cannot run past the final track, and automatic placement wraps a fitting
span to the next row. The renderer rejects wider spans rather than clipping them silently.
Unchanged keyframe declarations retain their presentation clock across palette switches;
changed declarations start their new finite track. A later match caused by a newly created node or environment
change raises `ThemeStyleFailed` with its entity, program revision and activation generation.
Desktop synchronously restores cached native built-in appearance and clears the style program
at one newer shared generation, then queues durable recovery through the activation actor.
The renderer retries that frame exactly once only after the handler changed the program or
generation. It retires borrowed outgoing theme pixels without restarting motion, retains
source nodes, inputs and routing ownership, and forbids recursive rendering. An unhandled failure, a second style failure or any ordinary host exception still
propagates; recovery never swallows arbitrary application callbacks.

## Host, settings and startup

The fixed appearance entries are theme mode, theme pack and launcher icon. Remaining
options are declared by the current theme and projected through finite host NXML
controls. Per-theme values have schema/version and stable IDs. Old appearance/music
values and unknown fields remain; migration is idempotent. No theme migration code.
Global settings reset remains scoped to available fixed catalog settings, including
the protected host motion controls. It preserves retained legacy appearance/music
values; per-theme values use the independent typed theme-options reset route.

One DesktopThemeSession coordinates effective appearance with existing consumers.
Page icons, runtime window/tray icons and installation icons are distinct; installation
metadata is not rewritten. Platform-only materials degrade explicitly. Tray restore
must preserve custom chrome and motion without DWM frame regressions.

The first splash uses built-in or validated finite startup appearance. Third-party
ZIP/NSS/media scanning never blocks its creation. Preparation joins existing startup
readiness; bounded style/icon/color/motion data can update the splash. Safe mode and
an independent recovery entry bypass third-party style/media/watchers.

## Delivery gates

Each closed slice includes contract/behavior, architecture and relevant publication
checks. Complete NTP/1 includes a runnable Advanced template covering advertised
capabilities, owned dynamic UI preservation, resource authorization, hot-reload and
stress/retirement checks, actual NativeAOT/full-trim executables and shipped files.
Record real platform acceptance separately from portable fixtures; do not claim
Windows/macOS results from Linux rendering. No silent unsupported capability success.

Service candidate cleanup uses a synchronous monitor scope because .NET 10 full
trimming removed the nested `Monitor.Exit` handler from the async `finally`; the
service suite checks that another thread can query immediately after preparation.
