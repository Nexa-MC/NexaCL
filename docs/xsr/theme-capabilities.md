# NTP/1 capability matrix

Registry decision: 2026-10-14. Required capability admission uses exact names.
These names describe concrete interfaces, not CSS browser compatibility.
Actual acceptance receipts must state their platform and executable; adding a
name to this matrix does not create a passing native result.

| Exact capability | Implementation boundary | Template/use and fallback |
| --- | --- | --- |
| `styles.basic` | Typed style-only snapshot, base-source separation | Minimal/Advanced; BuiltIn clears overrides |
| `tokens`, `options` | Finite NSS tokens and manifest values | Accent, title size, radius; invalid values reject |
| `selectors` | Authored kind/key/class/role and target interaction state | Title, capsule buttons, retained dynamic children |
| `conditions`, `viewport` | Bounded boolean/platform/mode/viewport program | Dark card, Linux focus border, narrow grid |
| `grid` | Renderer fractional/auto/pixel grid geometry | Launch home two columns, narrow single column |
| `position` | Flow/Absolute/Overlay geometry with original ownership | Existing launch progress overlay |
| `order` | Visual order shared with navigation | Existing version action buttons |
| `transform`, `transforms` | Same scene transform for drawing and inverse hit testing | Title transform; singular transforms rejected |
| `virtual-metrics` | Same-generation host virtual extent and realization facts | Host-marked virtual owners only; unmarked layout protected |
| `keyframes`, `animations` | Finite renderer-driven scalar animation | Title breathe; reduced motion takes precedence |
| `images`, `svg` | Admitted bytes, bounded native bitmap/vector resources | Advanced background/icon; explicit resource fallback |
| `fonts` | Admitted TTF/OTF, shared shaping/measure/draw identity | Licensed DejaVu title font, system glyph fallback |
| `opacity`, `gradients`, `shadows` | Immutable scene paint consumed by backend | Title/card paint; literal third-party colors |
| `translucency` | Portable opacity under host policy | Does not imply native blur support |
| `runtime-icons` | Prepared window/tray/brand resources | Runtime only; installer metadata retained |
| `startup-appearance` | Bounded verified startup palette/brand facts | First splash creation does not scan third-party NSS/media |
| `media` | Authorized prepared audio/video through existing consumers | Packaged samples disabled by default |
| `window-acrylic` | Windows/macOS native transparency capability | Optional, declared `solid` fallback elsewhere |
| `window-mica` | Supported Windows 11 native material capability | Optional only; never assumed on Linux/macOS |

The host may expose singular `image`, `font`, `gradient`, and `shadow` aliases
when their concrete provider is present. Templates use canonical plural provider
names. System default theme mapping is distinct from system light/dark mode;
third-party themes cannot register themselves as the operating system default.

Every manifest declaration is validated even if no node currently matches or a
condition is false. Unsupported required capabilities reject before activation;
unsupported optional capabilities remain visible with their fallback. Native
material negotiation still reports the effective fallback on the real window.
Codecs and installed fallback fonts vary independently of capability parsing.
The `media` capability exposes the admitted-resource and consumer contract; it
does not invent an installed decoder or audio output. The native publication job
installs/checks FFmpeg on each platform and performs actual decode preflight.
An unavailable playback/output dependency remains an explicit consumer state.

Coverage is split between service admission/authority tests, NSS compiler tests,
renderer geometry/state tests, actual backend resource consumption, Desktop
`--validate-themes`, and published NativeAOT/full-trim executions. CI's NTP
publication checks compare external assets with source and execute the shipped
program. Windows chrome/tray probes remain an additional native contract gate.
