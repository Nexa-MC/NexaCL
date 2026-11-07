# Themes

Services live under `src/modules/themes/`; built-in themes and examples are in `src/app/Nexa.Desktop/Themes/`. Theme services do not depend on Renderer, NSS compilation, or Avalonia. Desktop prepares and applies styles.

NTP/1 is a code-free ZIP or development directory. Root `mimetype` is `application/x-nexa-theme`. `theme.json` declares `format: NTP/1`, stable identity, dotted version, localized names, `styleApi: 1`, `nss: NSS/1`, ordered styles, capabilities, and typed options. Themes cannot replace NXML behavior, execute scripts, declare business commands, or change state bindings. Minimal and Advanced are executable format examples.

Manifest limits are 64 KiB and depth 16. Archives allow at most 4096 entries and 512 MiB actual expanded bytes. Styles allow 64 files and 1 MiB total. Reject traversal, absolute paths, links, duplicates, and cross-platform ambiguous names. File/directory resources require explicit grants and cannot escape through links.

A matching manifest ID updates the existing installation. Equal versions may be reinstalled; activating an older version requires explicit confirmation. Services own option preservation and version commits. Preparation, style commits, native effects, and persistence use candidate transactions with compensation. Failure retains a valid theme. Preview is temporary; monotonic generations distinguish A-to-B-to-A changes.

Unavailable required capabilities reject activation; optional capabilities use declared fallbacks. Reduced motion, safe mode, graphics recovery, and window lifetime policies override theme effects. Unsigned code-free themes and signed executable updates have distinct policies.

Independent themes own their source and NTP releases. This repository provides the theme runtime, built-in themes, format examples, and generic author tooling. To preview an external archive on Windows, build `tests/app/Nexa.Desktop.Tests` and run its assembly with `--native-theme-package-preview dark <output-directory> <package.ntp>`. The preview imports the archive through the settings UI and captures the launch and appearance pages using isolated settings. Use `light` for the light palette.
