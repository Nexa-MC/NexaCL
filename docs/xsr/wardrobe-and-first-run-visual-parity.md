# Wardrobe and first-run visual parity

Reference: dev PageSkinAppearanceRight and FirstRunWizardWindow, plus the two user-supplied wardrobe screenshots (2026-10-10).

## Presentation contract

- Wardrobe retains a full-height current-appearance rail, a large character preview, account captions, a primary local-skin action and a bordered library action.
- Skin/cape inventories remain independent horizontal tracks. At ordinary window heights cards place preview beside identity and apply action; taller cards may use a vertical layout only when the preview has enough room.
- View cycling is a small icon inside the preview/card. Its localized accessible name carries the current view; it never reserves a full text row.
- Use registered backend icon names. Current appearance and both inventories allocate from parent viewport geometry rather than intrinsic card height. Leaving wardrobe restores shell content padding.
- First run retains the four stages: welcome, data directory, diagnostics and completion. Welcome pairs the product image with its setup action. Remaining pages use a stable-width form and fixed bottom navigation, with scrolling when needed.
- Storage changes are draft-only until completion; test-channel diagnostic requirements remain controlled by Setup Service. No account, telemetry or persistence contracts change.

## Validation

Desktop regressions cover localized accessible view captions, profile isolation, current-preview height, stable card dimensions, bounded controls, and first-run draft/save behavior plus diagnostic option visibility.

`Nexa.Desktop.Tests --native-wardrobe-setup-preview <output-directory>` mounts real Avalonia surfaces and captures both themes for wardrobe and all four setup stages. The wardrobe inventory is deterministic fixture data (six skin entries and one cape), not evidence of a successful live provider response. The harness never completes setup or changes the user's data location.

These changes use existing scene primitives and sealed services; no new reflection, native interop or AOT-only code path is introduced.
