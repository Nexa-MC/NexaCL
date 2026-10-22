# Compact global settings pages

The global Settings destination opens its real NXML page. The active categories and their groups are projected from the sealed settings catalog query.

## Presentation

- A draggable top segmented selector and horizontal subpages replace the category rail. Setting rows remain 44 px high with 14 px group spacing. The content host's former extra padding is removed only while this page is active and restored on exit.
- Typography uses 14 px setting labels and 12 px secondary group labels, the existing shell accent, quiet white grouped surfaces and inset separators. There is no Hero or repeated page title.
- Category transitions use a zero-offset fade. Scroll uses the existing drag/inertia component and visible indicator, with no per-row entry animation. Each category remembers its current scroll position during the session.
- The developer preference uses a confirmed-state draggable On/Off radio group. Adding developer groups preserves the triggering control's focus and scroll position. Exclusive preferences use draggable radio-group segmented controls; XSR-773 adds native toggle/selection semantics.
- Only available entries with a concrete editable value contract appear in settings forms. Unsupported, undelivered and currently unavailable entries do not create rows or empty groups; their stable catalog IDs and persisted values remain intact. No new renderer or Avalonia dependency enters Services.
- The 2026-10-22 simplification removes inline help, static explanatory notices and read-only policy cards, including the protocol-registration and content-trust summaries. Empty theme-option and permission groups stay hidden. Required preview-build telemetry does not appear as a disabled setting; first-run disclosure and the underlying policy remain. Loading/error/empty results, actual diagnostic data, form labels, confirmations and working tools retain their behavior.

## Active settings

Only catalog entries with a verified consumer are enabled. The launch-policy migration
adds global/instance memory and Java automatic acquisition; memory and preferred Java
offer an explicit automatic/manual radio selector. See [XSR-761](XSR-761-settings-launch-policies.md).
Other stored legacy preferences are not enabled merely because their keys exist.

Edits dispatch through Foundation settings commands. Text drafts are not overwritten while focused and are persisted only by Apply; failure reports through the shared feedback surface. Service state revisions update the presented values. The UI does not evaluate inheritance, import files, or call SettingsService directly.

## Verification

Desktop regressions cover all current categories, narrow-window geometry, command-to-persistence behavior, draft focus, developer-section continuity, omission of ineffective rows/help and anchored choice menu geometry/selection. Visual review uses the actual Avalonia scene surface rendered to a bitmap in an external headless probe, not a separate HTML mock-up. Standard architecture, formatting, native and trim checks still apply.

Settings cards use an unpadded painted surface and a padded inner body. Platform facts,
preflight issues and update actions therefore share the full page width while content has
symmetric 16px/20px insets. Padding must not shrink the renderer's painted card rectangle.

XSR-773 standardizes inset forms and instance page layout. Boolean preferences use real
UI.Next draggable On/Off radio groups, export categories use check boxes, and choices have radio semantics.
Action buttons are retained for operations. See [XSR-773](XSR-773-settings-controls-and-forms.md).
