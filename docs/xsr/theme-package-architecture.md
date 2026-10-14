# Theme package architecture

Decision date: 2026-10-14. This document refines [NTP/1](ntp-v1.md).

## Ownership and dependency graph

`Nexa.Services.Themes.Contracts` contains portable immutable queries, commands,
snapshots and the `IThemePackageService` port. It references XSR abstractions only.
`Nexa.Services.Themes` owns archive/directory admission, installed sources,
transactions, persisted options and resource grants. It references its contracts;
it does not reference Desktop, Renderer, the NSS compiler or Avalonia.

Desktop owns `DesktopThemeSession`. It prepares a candidate through the service,
compiles its ordered NSS sources, acquires required resource leases, and commits a
style program and prepared host appearance under one activation generation.
Renderer owns computed presentation and geometry. Avalonia consumes the immutable
scene and prepared image/font resources. Neither renderer nor compiler reads theme
files or resolves concrete services.

The service catalogue is persistent source identity, not a renderer object cache.
Authored NXML identity and base templates are independent of theme revision.
Activating a theme must not rebuild pages or call Dynamic Reload to alter commands.
The style DTO deliberately has no recipe, command, binding, entity creation,
visibility, enabled, password, selection, ownership, anchor or dismiss properties.

## Container

A theme directory and the root of a `.ntp` ZIP have the same contents:

```text
mimetype                  application/x-nexa-theme
theme.json                strict NTP/1 manifest
styles/main.nss           explicitly ordered by manifest.styles
assets/…                  admitted image/icon/font/audio/video files
```

The reproducible packager is `python eng/themes/pack.py THEME --output OUTPUT.ntp`.
It places the literal uncompressed mimetype first, uses fixed archive timestamps,
sorts entries and rejects links and nonportable file names. Runtime admission is
performed independently by `ThemePackageService`; running the packager does not
grant access or activate a theme. Themes do not inherit application update keys.

Publication smoke commands, with an isolated `NEXA_DATA_DIR`, are
`Nexa.Desktop --validate-themes` for managed/NativeAOT/full-trim orchestration and
`Nexa.Desktop.Tests --native-theme-preview light|dark OUTPUT` for actual native
SVG/font decoding, drawing and retained interaction captures. The headless smoke
copies shipped templates to a unique temporary development source, uses the real
runtime/session/controller routes, and restores its original selected source.

The manifest is strict and rejects unknown/duplicate fields. Its root fields are
`format`, `id`, `version`, localized `name`, `styleApi`, `nss`, ordered `styles`,
`requiredCapabilities`, `optionalCapabilities`, `optionsVersion`, `options`, and
`appearance`. Required format values are `NTP/1`, `1`, and `NSS/1` respectively.
Stable IDs accept ASCII letters, numbers, dot, underscore and hyphen; theme
versions use the manifest's dotted grammar independently of product versions.

An optional capability is `{ "name": "window-acrylic", "fallback": "solid" }`.
The host admits only its actual capability names. A missing required capability
rejects the candidate; an optional capability is reported with its fallback.
Parser acceptance of a property alone does not prove native platform support.

## Parameters

An option has a stable `id`, finite `kind`, correctly typed `default`, localized
`label`, optional localized `description`, `group`, `visibleWhen`, and type-specific
`min`, `max`, `step`, `choices` or `resourceKind`. Choices have `value` and localized
`label`. Boolean defaults are JSON booleans; Number/Range defaults are numbers;
Files defaults are string arrays; other defaults are strings. File, Files and
Directory options declare Image, Icon, Font, Audio or Video resource kind.

Values are stored by source identity and option schema version. A package merely
sharing a theme ID does not share its predecessor's source grants. Invalid saved
values do not replace defaults silently during a successful parameter save.
Unknown legacy settings are retained; migration has an idempotent marker. Import
and export carry options, not external-read authority. Reset does not erase the
original legacy configuration, and uninstall does not grant a replacement source
access to resources selected for the removed source.

Host-authored NXML templates select finite editors. Themes cannot supply NXML,
editor factories, binding expressions or executable migration functions.

## Activation and recovery

The activation sequence is candidate admission, NSS compilation, required
resource preparation, style/appearance commit, and durable last-known-good
selection. Candidate and active generations are monotonically increasing;
theme ID equality does not make an old asynchronous result current. Stale
candidate completions retire their leases and cannot attach media or icons.
Prepared native appearance resources serialize borrow admission, release and
owner retirement under the same lifetime lock. Owner retirement does not destroy
an outgoing generation while scene/control borrows still exist; final release
destroys it once. Vector resources prepare one normalized drawing picture, and
tile presentation repeats that picture with a backend brush instead of allocating
shapes per tile. The exact SVG bounds are listed in
[theme-resource-security.md](theme-resource-security.md).

Preview is temporary and does not rewrite the committed selection. Revert restores
the committed snapshot. An invalid file watcher candidate leaves the previous
valid program active. Watchers belong to the development source lifetime, use
debouncing and stop when the source/session retires. An interrupted persistent
transaction is recovered on startup before applying an uncommitted selection.

The first splash uses the built-in or bounded verified startup snapshot. Opening
the splash does not wait for ZIP extraction, directory enumeration, fonts, video
or third-party NSS compilation. Main-window preparation joins startup readiness.
Safe mode bypasses third-party programs, media and watchers. A host recovery entry
remains available independently of the current theme's authored styles.

The bounded splash mark accepts none/fade/draw/breathe motion; draw is 600 ms and
breathe varies mark opacity from .88 to 1 over 1.8 seconds. Window topmost behavior,
startup stage transitions and lifetime remain host-owned. Resize policy is finite:
auto/immediate/instant applies directly, deferred waits for a 120 ms quiet period,
and animated shares a 180 ms renderer viewport between layout/paint/hit/caret.
Reduced motion applies resize immediately.

## Publication and acceptance

`Themes/BuiltIn`, `Themes/Minimal` and `Themes/Advanced` are external content.
Publication checks compare every shipped author asset byte for byte with source.
Service tests execute independently of UI. Desktop `--validate-themes` exercises
the coordination boundary against the shipped sources in managed, NativeAOT and
full-trim outputs. Renderer/compiler/backend cases verify matching, state
preservation and actual scene consumption. CI also exercises native host windows
on their own operating system; a Linux capture is not Windows/macOS evidence.

See [NSS/1](nss-v1.md) and [resource security](theme-resource-security.md) for the
style and authorization contracts. Executed acceptance receipts identify their
exact build, platform and capabilities; this architecture decision alone is not
an execution receipt.
