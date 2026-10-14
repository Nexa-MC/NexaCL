# NTP/1 implementation and acceptance receipt

Receipt date: 2026-10-14. Executed environment: Linux x86_64, .NET SDK
10.0.401 and runtime 10.0.12. Desktop publication identity:
`2.0.0.ci.2538a1`. This records the NTP/1 working-tree implementation on
`refactor/xsr`; the version suffix identifies its parent commit, not a committed
snapshot of the new work.

## Delivered contracts

- The presentation-independent theme service exposes 19 typed routes for
  catalogue, preparation, activation, recovery, options, grants, resource leases
  and directory watches. ZIP packages and development directories share bounded
  admission, durable candidate journals and last-known-good recovery. Temporary
  previews, source revisions and activation generations remain distinct.
- Host NXML projects all nine option kinds: Boolean, Number, Text, Color, Range,
  Select, File, Files and Directory. Values are schema/version scoped; import,
  export, reset and legacy migration preserve the fixed host settings boundary.
  External file/folder grants bind source identity, resource kind and canonical
  path. Imported options cannot manufacture grants.
- NSS/1 applies validated presentation transactions. Authored NXML ownership,
  bindings, intents, roles and retained dynamic nodes remain authoritative.
  Conditions, responsive grid, transforms, visual order, finite keyframes and
  protected virtual metrics use the renderer's scene and input geometry. Late
  style failures restore built-in appearance at a newer shared generation.
- Native font measurement/shaping/drawing, bounded SVG/image resources, runtime
  icons and admitted media use concrete backend consumers. Resource generations,
  lease retirement, watch debounce and recovery prevent stale reattachment.
  Optional native materials report their effective platform fallback.
- BuiltIn, Minimal and Advanced ship as external author files. The finite startup
  cache stores verified last-known-good presentation only; first Splash creation
  avoids third-party ZIP/NSS/media scans, and safe mode bypasses that cache.

The normative contracts remain [NTP/1](ntp-v1.md), [NSS/1](nss-v1.md),
[package architecture](theme-package-architecture.md),
[resource authority](theme-resource-security.md),
[capabilities and fallbacks](theme-capabilities.md) and
[startup cache](ntp-startup-cache.md).

## Executed Linux checks

Logs below are retained under `/workspace/.pcln-cloud/` in the execution workspace.
The counts are observed suite totals, not a count of new tests alone.

| Check | Observed result | Evidence log |
| --- | --- | --- |
| Full solution build | 72 projects; 0 warnings, 0 errors | `ntp1-solution-build1.log` |
| Desktop composition | 327 passed, 0 failed | `ntp1-desktop-tests4.log` |
| Settings/policy services | 53 passed | `ntp1-services-tests2.log` |
| Renderer | 117 passed | `ntp1-renderer-tests5.log` |
| NXML compiler | 76 passed | `ntp1-nxml-tests6.log` |
| Avalonia backend | 41 passed | `ntp1-backend-tests6.log` |
| Architecture boundaries | Passed for all 72 projects | `ntp1-architecture-tests4.log` |
| Published NativeAOT theme service | 26 passed, 0 failed | `ntp1-theme-services-aot-tests3.log` |
| Published full-trim theme service | 26 passed, 0 failed | `ntp1-theme-services-trimmed-tests3.log` |
| Theme packaging | 5 passed | `ntp1-pack-tests.log` |

Both final compiled Linux NativeAOT and full-trim Desktop publications completed
`--validate-themes`, `--validate-shell` and `--validate-setup` with exit code 0.
The first exercises
published theme discovery, preparation and activation; the other two preserve
the existing shell and first-run contracts. Runtime evidence is
`ntp1-{aot,trimmed}-{themes,shell,setup}-final.log`; setup exits successfully without
stdout. Publication logs are `ntp1-desktop-aot-final-build.log` and
`ntp1-desktop-trimmed-final-build.log`. These final checks include the completed
author-template contrast adjustment.

Byte comparison against author sources passed for all three themes and all 16
theme files in each of `ntp1-publish/desktop-aot/Themes` and
`ntp1-publish/desktop-trimmed/Themes`. These files remain external content.
The publication logs retain the existing Jint/PAC `IL2098` warning; it was not
suppressed. The zero-warning solution build is a separate check.

## Actual native previews

Both actual Linux Avalonia window runs passed with normal native teardown:
`ntp1-native-preview-light5.log` and `ntp1-native-preview-dark5.log`. They produced
48 PNGs and 48 adjacent JSON receipts: nine stages at 1040 and 810 DIP in each
mode, plus six animation frames at 1040 DIP per mode. The nine stages are BuiltIn
launch/settings, Minimal launch, Advanced launch/options/grants, retained game
editor, retained virtual content list and retained screenshot crop modal.

The harness exercises real theme admission, option/grant routes, SVG/font resource
preparation, responsive grid and paint. Native font providers are present, and the
renderer/native appearance generations agree. Captures assert retained input entity,
draft, caret, focus, scroll and modal ownership across theme switches; the isolated
business fixtures record no crop/server/export writes. Advanced captures also
verify visible title glyph pixels, with 1067 observed in each launch capture.

Animation acceptance first activates the actual native window and waits for both
window/host activity and effective reduced motion to clear. Six successive frames
then change title opacity by more than 0.01. Restoring the user's reduced-motion
preference removes theme animations and restores opacity to 1. This verifies
native activity and host policy precedence.

Images use software-rendered `RenderTargetBitmap(window)`. Game/account data and
picker selections are isolated fixtures; theme services, native resource consumers
and runtime interaction routes are real. Media assertions cover admitted playlist
files and the existing consumer-generation acknowledgement, not audible playback
or independently stamped media state in every capture. These results do not
certify compositor screenshots or Windows/macOS native chrome.

The complete workspace gallery is
`/workspace/.pcln-cloud/ntp1-previews/index.html`; its adjacent
`ntp-previews.zip` contains 103 allowlisted files (8,472,965 bytes), including
all 48 PNG/JSON pairs, two single-play motion GIFs, capture manifests, evidence
summary and SHA-256 digests. Failed captures are excluded and the ZIP CRC check
passed. ZIP SHA-256:
`8c7c4183e6006c6c1c01b588fd57076b0cdd8cbe71cc2a13939f0aa365bce930`.
Each GIF uses six distinct actual frames; its 960 ms playback uses the nominal
160 ms sampling interval, because actual sample timestamps were not recorded.

## Platform and CI status

The XSR workflow adds `NTP/1 NativeAOT publication` jobs for `windows-latest`,
`macos-latest` and `ubuntu-latest`. They publish and execute the theme service
and Desktop, compare external author assets, install/check the media decoder,
and capture actual native light/dark interactions. Those new CI jobs have not
yet run for this implementation. Linux execution above does not certify
Windows/macOS fonts, media, native materials, window chrome or tray restoration.

The prior [XSR CI run at 2538a1e](https://github.com/Nexa-MC/PCL-N/actions/runs/38373669123)
had five existing failures: a macOS scheduler assertion expecting `Faulted` but
receiving `Completed`, and Windows Java 8/17/21/25 compatibility-fixture compilation with a
Unicode output path. Its build/architecture job passed. The new NTP publication
results must be assessed independently; the prior full workflow was not green.
