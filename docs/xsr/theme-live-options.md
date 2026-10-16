# Theme option live application

Decision, 2026-10-16. Theme option editors apply complete values to the presented
source after a short quiet period, without requiring the Save button. Consecutive
changes coalesce before preparation; typing an incomplete number or color keeps
that editor's draft and the last admitted presentation. Other complete option
changes may still apply. Explicit Save remains available for retry and validation.
An active slider drag retains pointer ownership: automatic preparation waits while
the range is pressed and applies the final value immediately after release. It
cannot disable the editor halfway through a mouse, pen or touch gesture.
During an automatic option transaction the retained option editors remain enabled,
so typing, IME preedit and additional draft selections continue while preparation
is pending. A later application coalesces those newer drafts after the current
transaction completes. Theme lifecycle operations, external-resource pickers and
explicit Save/Reset stay serialized. Equal text values are not written back into
the text-input component because doing so would reset its caret and composition.

Desktop owns this scheduling. Every application still uses
`IDesktopThemeSettingsSession.SaveOptionsAsync`: schema/revision validation,
resource authority, style preparation, render/native generation, durable commit,
rollback and preview-selection ownership retain their existing NTP/1 boundary.
Installed sources retain their saved option history when a preview ends. A source
that was only imported for a temporary preview still retires its provisional
options and grants when its final candidate is released.
No renderer directly accesses the theme service, and no option change grants an
external resource. Picker admission precedes scheduling file/folder changes.

A successful application clears only submitted drafts that have not changed since
submission. Returning an editor to its previous saved value while a transaction
is pending still counts as a newer draft relative to the submitted value; a
newly published snapshot cannot overwrite it before task completion. Retained
editor identity, focus and caret remain owned by host NXML.
An invalid or failed application keeps drafts and the last admitted theme and
reports the existing error. It is retried by another edit or explicit Save, rather
than looping every frame. Leaving the appearance page cancels pending work and
retains the existing preview restoration contract; queued work cannot target a
replacement source or a later page lifetime.

Development-directory file watching remains the explicit NTP/1 watch operation;
its existing whole-source watcher covers `theme.json`, NSS and resources, including
changes saved by file replacement/rename. It debounces source revalidation by
300 ms. This decision concerns option values edited through the host settings page.

## Validation

Linux Release validation on 2026-10-16 passes all 350 Desktop composition tests,
29 theme-service tests and 77 NXML compiler tests. The live-option cases exercise
actual style recomputation and durable values in an installed theme preview,
coalescing, retired scopes, failed saves, slider release, focus/caret and IME
preservation. A gated save separately publishes its new snapshot before task
completion and verifies that returning to the previous value is preserved and
then saved using the next revision.

The native-window preview exports 28 captures per light/dark mode at 1040 and
810 DIPs, including switch, checkbox, slider and segmented option editors. Those
captures verify the real host templates and rendering; automatic application is
established by the behavioral tests above, rather than by a still image.
