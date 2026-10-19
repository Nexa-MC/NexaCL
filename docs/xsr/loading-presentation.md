# Loading presentation

Native migration previews select the business card and its padded body by semantic keys (`LauncherMigration.Card` / `LauncherMigration.Body`). Loading content is a sibling of the retained ready workspace, so positional assumptions such as “the last child is the business card” are invalid. Native padding assertions still check actual painted card insets whenever that card is visible, and busy stages capture the centered loading presentation.
Migration selection, completion and failure messages belong to the ready business card; the busy view supplies its own working hint. Hiding the busy view must not hide the results of the completed operation.
The migration confirmation dialog keeps the reviewable form visible behind its modal. Pending user confirmation disables actions but does not represent loading; only a running migration/read or cleanup operation masks the business content.

The host projects progress as finite renderer facts: a progress primitive may opt into ring presentation and indeterminate motion. Existing bar primitives retain their behavior and API. NXML `ProgressRing` uses additive literal `Ring` and `Indeterminate` flags; determinate values remain bounded fractions. The scene carries only immutable ring facts, never a clock or service.

Avalonia owns the rounded arc and its phase. Rings share the existing presentation clock, invalidate paint only, and stop when removed, clipped out, minimized, or under reduced motion. Reduced motion shows a static arc. Native Splash uses the same arc and clock. Theme accent and progress accessibility semantics are preserved.

Initial or replacement loading hides the corresponding content region and centers a ring above a localized hint. Errors are separate states with recovery actions. Interactive authentication challenges retain required codes and actions; passive preparation hides the form without discarding drafts. Task/download progress preserves real stage, percentage and cancellation. Resource pagination alone retains existing items and scroll position, showing its loading indicator at the bottom.

This is a presentation migration. Service caches, operation ownership, cancellation and Sidecar contracts are unchanged.

Navigation remains available while a content region loads. Independent settings controls and live theme option saves remain interactive; background save rings do not remove input focus or IME composition. Idle/busy/cancelling variants that reload in place share exactly the same NXML shape. Native dialog analysis carries an explicit loading fact rather than changing a text-format contract.

Dynamic templates support a single boolean inverse (`{data !loading}`) on `IsVisible` only. It resolves before compilation, rejects non-boolean data, and does not introduce executable expressions or alter the plugin UI ABI. The native startup window reserves room for the ring, bounded hint viewport and cancellation/retry controls.

Blocking cards project a numeric loading weight: they use available space while waiting, with a minimum sufficient for the ring and hint, then return to intrinsic height when ready. This prevents long settings forms from collapsing or overlapping adjacent cards. Independent background-save and recovery-policy controls keep their ordinary flow.

Verification (2026-10-19): Release build has no warnings or errors; 356 Desktop, 776 Services, 124 renderer, 79 NXML and 43 Avalonia backend cases pass, alongside the 72-project architecture check. The real native startup smoke covers reduced motion on the first render, live motion-policy changes, long topmost loading, failure/retry, and handoff to the same prepared window. Stopping a ring queues its paint invalidation after composition; repeated stops remain idle.

Linux x64 NativeAOT and self-contained trimmed publications both pass `--validate-shell` and `--validate-setup`. Their explicit NXML validation fixtures include the new loading facts. The upstream Jint IL2098 publish warning remains unchanged. Native resource and settings previews use actual X11 pixels and controlled data, with separate empty initial loading and append states; the offline gallery includes the real Splash ring and finite GIFs.
