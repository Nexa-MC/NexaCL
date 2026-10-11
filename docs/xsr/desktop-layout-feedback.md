# Desktop layout and interaction feedback

## Contract

All Desktop pages share renderer/backend interaction feedback. A pointer press is visible immediately, before command completion. Disabled controls cannot retain a pressed appearance. Large rows/cards may contract by at most 2 logical pixels overall; reduced motion keeps a static color response with no contraction. Explicit hover colors remain authoritative, with a low-contrast theme accent fallback for otherwise unstyled clickable controls. Inputs retain native text-selection geometry.

Page navigation uses a short coordinated fade with content visible from its first frame. Account state changes do not stagger individual controls. Dragging, segmented selection, scroll inertia and interruptible springs retain their existing state/intent contracts. No animation delays command dispatch or service work. Loading/progress, failures and completion remain truthful service-backed state, not simulated success.

## Page coverage

| Surface | Layout / feedback policy |
| --- | --- |
| Launch and account forms | Shared outer gutter, stable account identity, coordinated account fade |
| Version selection / directory picker | Aligned toolbar and list, existing immediate select and post-navigation double click preserved |
| Install chooser / Java install / version edit / Bedrock / Java choice | Compact single outer gutter, selectors stay directly draggable; async catalog status stays in each loader page |
| Resources / favorites / details | Compact aligned toolbar and result list; existing media and dependency state retained |
| Global and instance settings, content details, snapshots, platform diagnostics, servers/export | Single page gutter, tighter section spacing, shared controls and existing operation status |
| Task center / process log | Compact header and list, fixed actions, no delayed row entrance |
| Wardrobe / skin library | Keep full-height preview rail and horizontal collections; shared feedback |
| First run | Keep centered welcome, fixed subsequent forms, explicit next/back state |
| Dialogs / notifications / bubbles | Keep bounded, interruptible overlay motion and keyboard focus |

## Validation boundary

Renderer/backend interaction regressions exercise pressed, disabled and reduced-motion states. Desktop regressions check route and layout behavior; native screenshots cover representative light/dark settings and wardrobe/setup layouts. Fixture screenshots are not live-provider verification. No service, IPC or persisted-state contract changes.

## Installed content selection

Mods, resource packs and shader packs use full-row selection instead of a permanent checkbox column. Plain activation selects one item; Ctrl-click toggles one item; Shift-click selects the filtered range from the anchor; Ctrl+Shift adds that range. Selection uses root-qualified file identities, survives virtualization and filtering, and is pruned when file identities disappear. Refresh/scope retirement clears selection and its anchor. Details remain a separate action. The toolbar reports selected/updatable counts and disables selected-update when no selected file has a known update. Selected rows use the projected theme accent and a thin in-row marker; no row re-creation or motion is needed for selection feedback.

Validation includes real renderer pointer press/release modifier sequences, clear selection, retained row identities, and batch commands constrained to the selected live files. Native fixture previews capture the same controller in both themes.

## Verification (2026-10-11)

- Release Desktop build: zero warnings/errors.
- Desktop composition: 264 passed; renderer: 105 passed; Avalonia backend: 30 passed.
- Architecture: 70 projects passed. Scoped `dotnet format` completed; workspace-load warning retained in its log.
- Actual native fixture captures: light/dark settings, compact launch/install, wardrobe and four setup steps; selected installed-content rows in both themes.
- Explicit transparent foregrounds now remain transparent instead of becoming white checkmarks in dark mode.
- NativeAOT publishing and remote CI were not rerun for this UI-only change; these checks do not claim either result.

## Installed content online association and row actions

Mods, resource packs, shaders and world rows use the same expanding icon action control. Details and recoverable deletion are available through existing routes; enable/disable requires the local enabled fact and command; update requires a non-stale authoritative version. Child action clicks do not change row selection. Screenshots retain their card/preview layout.

Online identity is independent of project enrichment and update eligibility. A verified hash association survives detail or version lookup failure (including the batch deadline), after local identity/link revalidation. Unavailable enrichment never fabricates update availability. The UI counts verified installed identities and displays the returned failure reason instead of conflating all failures with unlisted files. Empty stale identity responses preserve stale state so the existing single renewal can execute. Regional CurseForge key requirements and mirror policy remain enforced.
