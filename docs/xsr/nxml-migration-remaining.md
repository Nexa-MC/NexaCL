# Remaining NXML migration

Objective: migrate all product presentation to Nexa.Xsr.Renderer, external `.nxml`
pages, `.c.nxml` components and `.nss` styles. Dynamic collections must enter named
`Dynamic` slots through `.d.nxml` fragments. Keep the existing product design and
service boundaries. Finishing infrastructure alone does not finish this objective.

## Current evidence, 2026-10-11

- Network/proxy and network diagnostics load dynamic fragments. Their controller
  architecture checks reject imperative presentation factories.
- Task Center cards and nested steps now load/reload owned fragments; their layout
  and visual constants are in NSS. Controller-owned node/style construction is gone.
  Remaining conditional copy and status formatting are still projected by C#.
- Task/launch/process bubbles and Java choices now use external dynamic fragments.
  The dock registers only owned slot children and reads spacing/dimensions from NSS.
- Process log workspace and rows now load/reload fragments, preserving literal output,
  selection identity, search drafts and caret. Conditional status copy remains C#.
- Settings catalogue rows, navigation, save status, enum/boolean selectors,
  plain inputs, argument rows and lossless command editors now load owned fragments.
  Their layout and visual constants live in NSS; labels/hints and option mappings
  still include C# copy and must be externalized.
- Installed Java inventory, runtime/missing rows, selection states and management
  actions now load/reload fragments. Paths remain literal data; bounded derived keys
  keep long executable paths within NXML's identity contract.
- About content and diagnostic export controls now use external fragments and NSS;
  export/cancellation availability reloads without replacing button entities.
- General protocol observations, storage location/cleanup controls, resolved directory
  rows, settings import/export/reset and disk-log actions now use external fragments.
  Disk-log busy state reloads in place instead of rebuilding the settings page.
- Backup, migration and AI workspaces use external fragments. Backup statistics,
  captions and diagnostic history formats now live in NXML rather than C# sentences.
- Developer renderer diagnostics and state inspector use external fragments, fixed
  field/condition templates, NSS and named text formats. Renderer refresh preserves
  field entities; inspector paging replaces only its owned row slot.
- Platform observation groups, status/badges, right-side device/resource lists and
  preflight/remediation panels now load owned fragments and NSS. Hardware advice shares
  one asynchronous preflight evaluation per captured snapshot; presentation never queries.
  Fixed status/group/format copy is external, while service facts remain literal data.
- Literal named text formats support localization before substitution, reject non-text
  placement and enforce both output and placeholder budgets. Data is never reparsed.
- Resource filter overlays already use fragments. Other resource presentation is
  still imperative.
- Shared reload preserves entity identity, nested slots, drafts, scroll, focus,
  progress and transition objects; it validates the complete projection first.
- Bootstrap of detached pages is now owned by the NXML loader, not a temporary
  controller-created visual tree. Other controllers must migrate to that API too.

## Remaining slices

| Area | Authoritative entry points | Remaining work |
| --- | --- | --- |
| Settings and instance management | `SettingsPageController.cs` and partial files | Replace remaining custom row/group factories, diagnostic cards, content/detail/snapshot/server/export structures and their style mutations with templates. Preserve drafts and list virtualization. |
| Resources | `ResourcesPageController.cs` and partial files | Search toolbar, categories, virtual rows, detail/version/dependency and install confirmation layouts; content and state variants must be external. |
| Launch and installation | `LaunchPageController.InstallCatalog.cs`, `.Installation.cs`, `.FrameProjection.cs`, `.Styles.cs`, `BedrockInstallPageController.cs` | Catalog/loader rows, state panels, choice tracks and runtime presentation styling. |
| Version selection | `VersionSelectionController.cs`, `.Window.cs` | Row window/virtualization, directory choices/editor, popup body and selection styles. |
| Wardrobe | `WardrobePageController.cs`, `WardrobeLibraryPageController.cs` | Skin/cape cards, details, empty/loading/failure surfaces and interaction variants. |
| Accounts and first run | `AccountFormController.cs`, `FirstRunController.cs` | Form structures, state copy, action availability and complete OOBE composition. |
| Process and dock copy | `LaunchPageController.Process.cs`, `ProcessLogPageController.cs`, `DesktopTaskBubblePresenter.cs` | Layouts and visual constants are external; migrate remaining conditional labels and formatting, then audit all lifecycle paths. |
| Global overlays | `DesktopCommandPalette.cs`, `DesktopFeedbackPresenter.cs` | Palette, notification/dialog collections and actions, keyboard focus and dismiss behavior. |
| Localization assets | `UiLocalizationCatalog.cs` and `Nexa.Desktop.csproj` | Audit embedded translation data and move the remaining static language payload into the external asset bundle. |
| Appearance | `CustomAppearanceSession.cs` | Separate runtime image/theme behavior from remaining fixed presentation construction. |
| Task copy | `TaskCenterController.cs` | Externalize conditional labels, icons and text formatting as fragment variants/data formatting, not C# literals. |

Direct node/style/stack construction still remains across the other Desktop slices. This is a locator, not a completion metric: helpers can hide imperative layouts,
and compiled renderer primitives/platform bridges are intentionally still code.

## Completion gates

1. Inspect every Desktop controller and presenter, including calls to helper factories,
   style/box mutations, interpolated XML and direct IR construction. Only behavioral
   projection, intent routing and platform/render primitives remain compiled.
2. Every fixed layout/style/content variant is declared in external assets. All dynamic
   collections have explicit slot ownership and append/replace/reload/remove semantics.
3. Extend no-factory regressions to each completed slice, then the entire Desktop surface;
   do not weaken the checks or hide factories behind a generic compatibility wrapper.
4. Preserve input drafts/caret/selection, pointer capture, keyboard focus, scroll position,
   virtualization, motion/reduced motion and async stale-result rejection.
5. Exercise actual rendered pages in both themes and constrained viewports. Record fixture
   data versus real service data; test the interaction, not only template parse success.
6. Run scoped behavior tests, full Desktop composition, NXML/runtime, architecture and
   publication checks; execute NativeAOT and full-trim smoke against shipped external files.
7. Remove obsolete UI.Next/PXML projects/assets/references, track any intentionally retained
   historical docs separately, format changes, commit in reviewable slices and push.

The overall migration remains active until all gates are evidenced for all slices.

## Dock and process slice validation, 2026-10-11

- Desktop composition: 264 passed; NXML: 52 passed; architecture: 69 projects passed.
- Overlay ownership and empty-layer pointer pass-through have executable regressions.
- Log selection retains row identity and raw text; workspace reload retains the search
  draft, focus and caret. Existing session cancellation/copy/redaction tests still pass.
- Java choice visibility and launch progress continue to pass their existing regression.
- Actual Avalonia previews were captured in both themes after transition settling:
  task dock, process dock and selected process-log rows. The process entries and logs
  are fixtures, not a real Minecraft session; service launch validation is not claimed.
- Windows x64 NativeAOT and full-trim publish each succeeded. Both executables ran
  `--validate-shell`, including dynamic network/task/dock checks, and exited 0.
  The existing Jint IL2098 annotation warning remains in both publication paths.
- Targeted `dotnet format whitespace` completed successfully. The full migration is
  still incomplete: use the remaining slices and completion gates above.

## Settings catalogue slice validation, 2026-10-11

- Desktop composition: 264 passed; NXML: 53 passed; architecture: 69 passed.
- NXML explicitly declares PreserveTabs, MaxLines and TrimOverflow. Reload keeps
  drafts and rejects changes to input sanitization before mutating the fragment.
- Catalogue, editor, hook and navigation partials are covered by the no-factory
  architecture regression. Legacy helpers remain only for unmigrated custom pages.
- Actual Avalonia previews in both themes show catalogue controls and argument rows;
  the native harness adds a draft argument without saving settings. Fixture data is
  documented in the preview manifest; no live Minecraft session is claimed.
- Windows x64 NativeAOT and full-trim publish succeeded. Both executables ran
  --validate-shell and exited 0, including selector slots, adding argument rows,
  retained drafts and literal command tabs. Existing Jint IL2098 remains.
- Targeted whitespace format and diff checks passed. The full migration remains open.

## Java inventory boundary

- Keep discovery, registry writes and scoped Java selection in the existing services.
- Inventory header, runtime/missing rows, ownership labels and actions must be external
  dynamic fragments; enable/disable uses the existing segmented-track interaction.
- Registry generation/revision checks, picker cancellation and managed-runtime identity
  checks must be preserved. Reload must keep row identities and focus when only the
  selected runtime or busy state changes.

## About and diagnostic export boundary

About copy, hierarchy, links and optional diagnostic section move to external
fragments without changing license text or link targets. Diagnostic export action
states (idle/exporting/cancelling) reload in place; cancellation and privacy/export
service boundaries remain unchanged. Runtime executable paths are literal data,
not NXML keys; Java management controls use bounded SHA-256-derived identities.

## Java inventory and About slice validation, 2026-10-11

- Full Desktop composition: 264 passed. Architecture: 69 projects passed, with
  no-presentation-factory checks added for Java, About and DiagnosticExport partials.
- Java tests cover asynchronous scan cancellation, stale picker completion, instance
  isolation, captured managed-runtime identity and retired deletion confirmations.
  Selection reload preserves entity identity; selected rows retain keyboard focus
  across unchanged projections. A pending write may intentionally disable the control.
- Diagnostic export tests cover coalescing, navigation, cancellation, retry and disposal.
- Actual Avalonia previews captured both themes for the About page and Java inventory.
  The Java entries are explicitly declared fixtures and never modify a real registry.
  The scrolling harness selects the actual pager body, since transitions can present
  both outgoing and incoming bodies at once.
- Windows x64 NativeAOT and full-trim publications succeeded; both actual executables
  ran --validate-shell and exited 0, including external Java body ownership and About
  copyright/license/acknowledgement nodes. The existing Jint IL2098 warning remains.
- Controller feedback/dialog wording and runtime label formatting still require the
  global copy/overlay migration. These slices do not establish whole-product completion.

## General/storage/transfer boundary

General protocol observation, storage location/cleanup controls, resolved directories
and settings import/export/reset presentation move to external fragments. Service
queries/commands, preview-before-write, cancellation, revision/scoped result rejection
and queued-close behavior stay unchanged. Filesystem paths are literal data. The
protocol observation uses separately localized state copy and literal timestamp/detail.
This does not migrate backup/AI workspaces or global confirmation-dialog copy yet.

Disk-log actions also move to an owned fragment. Busy state reloads the same action
entities instead of rebuilding the settings page; export data policy is unchanged.

## General/storage/transfer slice verification, 2026-10-11

- Desktop composition: 264 passed. Existing storage regressions verify preview and
  confirmation before migration/cleanup, late confirmation retirement, active-root
  revision changes, unavailable routes and preservation of task recovery records.
- Disk-log export now additionally verifies that a busy projection retains the same
  button entity. Existing cancellation, shutdown and error-redaction checks pass.
- NXML: 53 passed. Architecture: 69 projects passed; GeneralRuntime, Storage,
  StorageLocations, Transfer and Logs are now included in the no-factory gate.
- Actual Avalonia previews in both themes cover location/cleanup, right-aligned
  multi-directory rows and settings transfer. Directory snapshots are fixture data;
  no preview performs migration, cleanup or import.
- Whitespace format and scoped diff checks passed.
- Windows x64 NativeAOT and full-trim published successfully. Both actual binaries
  ran --validate-shell and exited 0, including the new directory slot, cleanup and
  settings transfer controls, and literal protocol details. Existing Jint IL2098 remains.
- Backup/AI workspaces, platform/diagnostic cards, instance management and the other
  product surfaces listed above remain open; this is not whole-product completion.

## Backup and diagnostic workspace boundary

Backup, legacy migration, durable diagnostic history and AI request editor structures
move to owned dynamic fragments. Styles and field limits are external. Controllers
retain service admission, confirmation, cancellation, identities and ephemeral input
drafts; API keys are never template parameters. Formatted service summaries and
confirmation copy remain a separate text-projection migration item.

## Backup and diagnostic workspace verification, 2026-10-11

- ContentWorkspace and DiagnosticAi now load external fragments into owned slots;
  both partials are covered by the architecture no-presentation-factory guard.
- Desktop composition: 264 passed, including AI opt-in, policy revision retirement,
  confirmation-before-send, raw preview localization, draft preservation and secret
  clearing. The test additionally verifies dynamic slot ownership and password/length
  metadata from the template. Java focus regression waits for the asynchronous write
  to restore interactivity before checking focus rather than racing the busy state.
- NXML: 53 passed. Architecture: 69 projects passed. Scoped whitespace/diff checks pass.
- Actual Avalonia previews in both themes include backup summary/controls, diagnostic
  workspace, AI fields and actions. Backup/history data are explicitly read-only
  fixtures; the preview executes no backup, migration or AI request.
- Windows x64 NativeAOT and full-trim publishes succeed. Both actual binaries run
  --validate-shell with exit 0 and verify backup and AI slots plus migration actions.
  The existing Jint IL2098 warning remains.
- Remaining: formatted summary/caption copy and confirmation dialogs still need the
  text-projection migration; platform/diagnostic panels and other product surfaces
  above still need migration. This does not close the whole-product objective.

## Developer diagnostic boundary

Renderer diagnostics and state inspector move to external fragments. Refresh reloads
renderer fields without replacing entities; state pagination replaces only its owned
row slot and retains the toolbar/body. Metadata capture remains bounded and read-only:
never read or format arbitrary state values. Format sentences live in NXML and localize
before literal data substitution. Developer admission and stale intent rejection remain.

## Developer diagnostics and named text verification, 2026-10-11

- Desktop composition: 265 passed. Added checks retain renderer field entities across
  refresh, preserve inspector controls while paging/refreshing, translate format
  sentences in en/zh-Hant/zh-Hans and never format arbitrary state payloads.
- NXML: 56 passed. Added mixed-format localization, unchanged parameter sets, literal
  XML/state-like data, non-text grammar rejection, 256-placeholder and 1 MiB output
  budgets, and reload identity/failed-projection preservation regressions.
- Architecture: 69 projects passed. Diagnostics.cs joins the no-factory gate. Its old
  DiagnosticText helper remains explicitly in Controls.cs for unmigrated runtime,
  instance, hardware, integrity and trace panels; these callers are still open.
- Actual Avalonia previews in both themes cover the complete renderer diagnostic card
  and rendered state rows after activation of Next. Nested surfaces preserve the
  original padding and radius. Preview metadata comes from the temporary fixture host.
- Windows x64 NativeAOT and full trim both publish and run --validate-shell with exit 0.
  The shipped inspector template and localized named format preserve XML/state-like
  metadata as literal text. Existing Jint IL2098 remains.
- Scope remains open: platform capabilities/preflight, advanced launch/runtime/instance
  diagnostics, remaining catalogue copy, global dialogs and the other product surfaces
  listed above. The reusable formatter removes the prior blocker for external copy;
  it does not imply all remaining copy has migrated.

## Platform and preflight presentation boundary

Platform groups, badges, right-side device/resource lists, diagnostics/remediation and
hardware advice move to external fragments/NSS. External PlatformTerms.nxml defines
presentation terms and preferred group order; controllers may read this text-only IR
as copy data, never as an imperative UI factory. Hardware advice text formats are
external text fragments. Every actual UI change still enters an owned Dynamic slot.

One asynchronous preflight request evaluates each captured machine snapshot and is
shared by diagnostics and advice. Rendering performs no service query. Source/scope
changes and leaving the page retire pending evaluation. Remediation dispatch is tied
to the current enabled source control and captured instance scope; delayed confirmation
or Java pick cannot act on a newly selected instance. Each platform projection has a
generation so returning to the same cached snapshot never revives a retired confirmation.
Unknown facts remain unknown.

## Platform presentation and Java removal verification, 2026-10-11

- Desktop composition: 267 passed. The captured-snapshot test proves that repeated frames
  issue neither new machine reads nor repeated preflight requests and do not publish a new
  machine revision. The asynchronous report appears in both the repair and advice sections.
- New regressions distinguish unavailable lists from verified empty lists, retain literal
  device/resource names, verify right-side list and centered badge layout at 760/1100 widths,
  and reject confirmation/Java-pick results retired by navigation or instance selection.
- Architecture checks passed for 69 projects, including the no-presentation-factory gate for
  Platform and HardwareAdvice. NXML compiler/loader checks: 56 passed. Targeted formatting
  completed; the workspace loader reports its existing generic warning.
- Actual Avalonia previews in both themes include the Java page without the removed
  diagnostic/manual-download/policy cards, plus platform preflight, advice, device lists and
  resource-pack lists. Observation values and available Java rows are explicitly declared
  fixtures; these images are layout evidence, not real hardware acceptance.
- Preview navigation now selects entities owned by its current controller. Two simultaneously
  retained Settings pages may have identical navigation keys during transitions; a scene-wide
  first match was insufficient for this fixture.
- Windows x64 NativeAOT and self-contained full-trim publication succeeded. Both published
  executables actually ran --validate-shell, checked Java panel retirement and the new
  platform group/issues slots, literal centered badge and named device format, then exited 0.
  Existing Jint IL2098 remains; no warning was suppressed.
- These are closed presentation slices. Remaining runtime/instance diagnostics, management,
  resources, install/version rows, wardrobe, accounts/OOBE, global overlays and fixed copy
  in other controllers/localization assets still require migration. The whole objective is active.

## Runtime diagnostics / trace / advanced workspace boundary

Runtime metric groups, field labels, unavailable/motion/clock variants, trace
toolbar/rows/paging formats, advanced navigation/facts/flags/probe and redacted
settings workspace move to external dynamic fragments and NSS. Captures remain
explicit and read-only. No service query is allowed during presentation. Trace
filters and paging operate on the bounded captured snapshot; navigation cancels
its in-flight read and rejects retired controls. Settings export preserves the
existing bounded, validated public-value/redaction contract. Asynchronous probe
completion and paging must update only owned fragments without rebuilding other
settings controls, resetting drafts, focus or scroll. Capability availability
and developer mode remain behavioral gates; no unobserved metric is invented.

## Runtime / Trace / advanced workspace validation, 2026-10-11

- Desktop composition: 268 passed, 0 failed. Architecture: 69 projects passed,
  including no-factory checks for these three partials and old helper-factory bans.
  NXML compiler/runtime: 56 passed.
- Runtime metric refresh retains field/action entities and remains explicit/read-only.
  Captures do not render, drain pending state or write settings.
- Trace tests hold the asynchronous capture pending, reject repeated capture, retain
  toolbar entities, page the bounded captured metadata and reject cancelled late
  results after leaving the page. Arbitrary state payloads are never formatted.
- Advanced probe completion/capture/settings paging retain other diagnostic captures,
  the selected inspector page and keyboard focus. Existing route dispatch, developer
  gating, retired action and redaction/copy checks continue to pass.
- Actual native previews captured both themes: automation navigation, probe, redacted
  settings, renderer/layout/scheduler/performance groups and Trace pages. Runtime
  counters/probe are actual native read-only observations; Trace is explicitly fixture
  metadata and the settings belong to a temporary data root. No Minecraft session is
  represented by these previews.
- Windows x64 NativeAOT and self-contained full-trim publications succeeded. Both
  actual executables ran --validate-shell and exited 0 with the new positive
  Dynamic runtime validation marker (owned slots, retained probe, literal metadata).
  The existing Jint IL2098 warning remains; it has not been suppressed.
- Targeted whitespace format succeeded (workspace-loading warning); diff checks pass.
  Validation logs/previews are under .tmp-cleanup-validation/runtime-*.
- The overall migration is still incomplete. Instance/launch diagnostics, content
  management and the other surfaces in the remaining inventory are the next work.
