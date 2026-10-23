# Resource footer layout

Actual Avalonia native-window captures at 850 × 500, using controlled catalog fixtures
under Linux X11 software rendering. These images are not online-provider acceptance evidence.

- [Before, dark](before-dark.png): the 64-pixel footer reserves excess space below the list.
- [After, dark](after-dark.png) and [after, light](after-light.png): the 28-pixel footer returns
  36 pixels to the results viewport and aligns its status and controls in one row.
- [Appending, dark](append-dark.png): the compact ring and hint share the footer without
  changing the results viewport height or clearing existing cards.

Reproduce with `Nexa.Desktop.Tests --native-resource-preview dark <directory>` (or `light`).
The native route also verifies append, same-page retry, retained scroll and detail/back reuse.
Existing Desktop resource tests check compact footer geometry and ring/hint containment.

Validation (2026-10-23): 27 resource-related Desktop cases, both native preview routes,
the 72-project architecture check, and Linux x64 NativeAOT shell validation pass.
