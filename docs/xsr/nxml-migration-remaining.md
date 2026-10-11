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
  Fixed status/icon copy and counter/unit formats now come from external assets.
- Task/launch/process bubbles and Java choices now use external dynamic fragments.
  The dock registers only owned slot children and reads spacing/dimensions from NSS.
- Process log workspace and rows now load/reload fragments, preserving literal output,
  selection identity, search drafts and caret. Conditional copy and named formats are external.
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
- Instance capture/status/resource/system-event panels, durable launch/crash/operation
  histories and advanced captured launch facts now use external assets and owned row
  slots. Background completions and pagination no longer rebuild the settings page.
- Development-mode controls, bounded config/log editing, explicit offline checking
  and the recovery timeline now use external fragments/NSS. Async results and local
  paging retain controls/drafts; old scope/path/document completions are discarded.
- The immutable language catalogue now ships as an external JSON asset. Build/publish
  and architecture checks reject the former embedded resource; bounded parsing and
  complete entry validation remain enforced.
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

## Instance diagnostics / launch facts boundary

Externalize the complete instance diagnostic surface: capture toolbar, current
session/status, launch performance, guarded measured resource values, bounded
system-event rows, durable launch/crash/operation history and advanced captured
launch plan/document/timeline/paging. Fixed labels, status variants, metadata/unit
formats and styles are assets. Service data remains literal, bounded and scoped.

Move the existing automatic history read out of presentation into the async
controller update lifecycle. Manual launch-fact reads remain explicit, read-only
and coalesced. Both reads must cancel/retire on visibility, instance, page or
development-mode changes; late results cannot populate another scope. Paging
changes only owned row slots, with retained toolbar/focus/scroll and other captured
sections. No observed metric may be replaced by an estimate or guessed zero.

## External localization bundle boundary

Move the immutable language catalogue out of embedded resources into
Ui/Localization/messages.json beside the executable. Use the same bounded local
asset reader as markup/styles. Preserve language resolution, literal arguments,
named-format localization before substitution, singular forms and cached lookup.
The catalogue is read once per presentation lifetime; translations are data and
never executable markup. Publish/copy rules and architecture checks require the
external file and reject the old embedded catalogue. Missing or malformed assets
fail clearly; there is no compiled copy silently masking a broken package.

## Instance diagnostics and localization validation, 2026-10-11

- Desktop composition: 271 passed, 0 failed. Architecture: 69 projects passed.
  NXML compiler/runtime: 56 passed.
- Capture/history/operation/launch-fact partials are protected by both direct
  node/style construction and legacy-helper factory regressions.
- Automatic history reading is admitted by the async update lifecycle; rendering
  does not start a query. Explicit launch capture coalesces reads. Navigation,
  same-scope return and instance replacement cancel/discard retired tasks; unobserved
  faults are consumed. Optional or unsampled metrics retain unavailable semantics.
- History and launch paging preserve action/root identity and focus, and update only
  their owned rows. Literal paths, environment names, captured arguments, document
  lines and service metadata are not translated or reparsed as markup/bindings.
- New named formats pass Simplified Chinese, Traditional Chinese and English checks.
  The external language reader rejects empty, incomplete and oversized catalogues.
  Build outputs contain the language file and no embedded copy.
- Actual native previews captured both themes and a constrained 820px window:
  current capture, measured-resource fields, launch/crash/operation history,
  operation pagination, captured plan and document pagination. These instance/session
  values are explicit read-only fixtures, not a live Minecraft measurement.
- Native preview waits for the actual category transition to settle before scrolling.
  Visual inspection corrected the selected tab contrast in dark mode. Existing
  general/runtime/resource/task/dock previews continued to pass.
- Windows x64 NativeAOT and self-contained full-trim publications succeeded. Both
  actual executables ran --validate-shell and exited 0, positively verifying
  Dynamic instance diagnostics and External localization validation markers.
  The existing Jint IL2098 warning remains and is not suppressed.
- Targeted whitespace format succeeded (workspace-loading warning). Validation
  artifacts are under .tmp-cleanup-validation/instance-*.
- Overall migration remains active. The remaining inventory still includes custom
  instance pages, resources, launch/install, version selection, wardrobe/accounts/
  OOBE, global overlays, conditional copy and appearance.

## Instance tools boundary

Development-mode controls, the complete bounded configuration/log workspace, explicit
offline readiness and recovery timeline move together to external fragments and NSS.
The file workspace keeps 25-entry/25-line paging, lossless line replacement including
tabs and original line endings, preview-before-save and original-file backups. Async
completion and local paging update owned result/row slots, retaining toolbar/editor
entities and drafts; no presentation-time query or full-page rebuild is allowed.

Every pending read captures normalized instance plus area/path/document identity.
Navigation, hiding, development-mode changes and instance changes retire it. Late
results and intents cannot act after retirement, including a return to the same scope.
Offline checking remains explicit, read-only and coalesced; settings revision changes
invalidate a pending/cached report. Timeline rows retain the most recent 16 real
snapshot timestamps, baseline markers and actual file-count deltas. User paths, file
contents and reason codes remain literal data, never markup or raw UI keys.

## Instance tools verification, 2026-10-11

- Desktop composition: 273 passed, zero failed. New deterministic regressions cover
  canceled/late reads after returning to the same scope, bounded identities for long
  file names, 25-row paging, retained editor/focus/caret, tabs/CRLF preservation,
  coalesced previews, edited-preview rejection and explicit-save admission.
- Offline checks coalesce, reject results from another root with the same instance
  name and invalidate reports when settings change. Their pending/result projection
  retains the identity editor and all unsaved notes in the overview.
- Architecture: 69 projects passed. The four tool partials plus their projection and
  behavior-only helpers now join both direct- and hidden-factory rejection guards.
  NXML compiler/runtime: 56 passed. Complete external language coverage passes.
- Actual native previews cover development controls, offline result/failure detail,
  persisted timeline points/deltas, file lists, text editor/save preview and an
  820-pixel window in both themes. Workspace/snapshot/Java facts are documented
  read-only fixtures; these previews do not write or restore user files.
- Windows x64 NativeAOT and full-trim publication succeeded. Both actual executables
  ran --validate-shell with exit 0 and the positive Dynamic instance tools validation
  marker: editor identity/tabs, owned slots and localization before literal metadata
  substitution. The existing Jint IL2098 warning remains in both paths.
- Scoped whitespace format and diff checks pass. Remaining instance management,
  resources, launch/install, version/wardrobe/account/OOBE and global overlays/copy
  still require migration; this closes the related tools, not the overall objective.

## Task/dock/process text boundary

Finish the already-external task, bubble and process-log layouts by moving fixed
state terms, unit/counter formats and presentation-only crash/Java/confirmation copy
to external text assets. Localize format templates before substituting task names,
instance identities and provider errors; raw log output stays literal and copied
byte-for-byte as the existing stream-prefixed text. Language changes must reproject
retained controls without another query, lost search/selection or reset progress.
The read/cancel/dismiss/process termination and Java-selection routes are unchanged.

## Task/dock/process text verification, 2026-10-11

- Desktop composition: 275 passed, zero failed; architecture: 69 projects passed;
  NXML compiler/runtime: 56 passed. A new guard rejects compiled fixed operation copy
  in task, task-bubble, process-bubble/Java/crash and process-log presentation.
- State terms/icons and 29 format variants are external. Format localization occurs
  before substituting literal task names, instance metadata and provider errors.
  Raw stream-prefixed log output retains tabs, CRLF, XML/binding-like text and ANSI
  control characters without reparsing. Oversized format data is rejected.
- Deterministic regressions retain task action/focus/progress entities and log row
  selection, search draft/caret/focus across Simplified Chinese, Traditional Chinese
  and English changes. Language changes do not read the log service again.
- Actual native previews cover task details, dock/process bubbles and selected log
  rows in both themes. Sessions and task progress are declared read-only fixtures,
  not evidence of a real game process. Existing constrained diagnostic previews pass.
- NativeAOT and self-contained full-trim publications succeeded. Both published
  executables actually ran --validate-shell and exited 0 with the positive External
  operation copy validation marker. Jint IL2098 remains unsuppressed.
- Scoped formatting and diff checks pass. Legacy lifecycle/helper primitives and the
  remaining global overlays/other inventory still need the full-product audit; this
  closes the operation copy group and does not complete the overall migration.

## Global overlay boundary

Migrate the command palette, notification host/cards and all dialog variants as one
related group. Static overlay layers are external pages with owned Dynamic slots;
workspace/card/row updates enter those slots through .d.nxml. A shown modal layer
retains its slot parent; Stage.Show must not detach an owned fragment. Preserve
route admission, retired-source rejection, notification expiry, out-of-flow exit,
modal pointer blocking, Escape, focus restoration and full literal dialog bodies.
Language changes retain search drafts, caret, scroll and overlay motion objects.

NXML may declare finite renderer facts for OverlayMotion, LiveSetting,
DismissCommand, ShowsVerticalIndicator, StickToEnd and ScrollGesture. Named finite
values and semantic routes are validated before tree mutation; scroll facts require
scroll containers. Input/command facts may compose with element/stack primitives,
without a product-specific CLR control. Reload preserves motion/input/scroll state
and rejects changes to their lifetime contracts. NSS holds all fixed dimensions,
palettes, margins and text presentation. Runtime rail position is computed from the
external base margin and shell progress; rendering performs no service query.

## Global overlay verification, 2026-10-11

- Full Desktop composition: 276 passed, zero failed; NXML compiler/runtime: 58
  passed; architecture: 69 projects passed; actual Avalonia backend: 30 passed.
- Palette, notification and all dialog variants enter owned Dynamic slots from
  external assets. Finite motion, live-region, dismiss and scroll facts are compiler
  validated; numeric/unknown enums and incompatible reload contracts are rejected
  before mutation. The controllers have no direct or hidden presentation factories.
- Regressions retain search/caret/focus, nested slot identity, notification/dialog
  motion and message scroll across languages. Literal metadata is not reparsed.
  Hidden, foreign and retired action sources cannot resolve the current dialog.
- Public hit testing now passes through an empty transparent nonmodal layer, while
  interactive notification children and modal scrims still participate. The four
  existing click regressions and an explicit empty-layer check pass.
- Actual native previews cover notifications, a three-action dialog and filtered
  palette in both themes and an 820px window. Visual inspection corrected title
  visibility, button contrast and aligned palette padding. The harness waits for
  real dialog exit before opening the next modal; it does not bypass that lifetime.
  Message/session/settings data are read-only fixtures, not a Minecraft run.
- Windows x64 NativeAOT and full-trim publish succeeded. Both actual executables
  ran --validate-shell with exit 0 and the positive Dynamic global overlay validation
  marker. The existing Jint IL2098 warning remains unsuppressed.
- Scoped whitespace/diff checks pass. Artifacts: .tmp-cleanup-validation/global-overlay-*.
  Other product copy/controllers and the remaining inventory are still open; the
  complete migration has not been declared finished.

## Version selection boundary

Migrate the entire selection workspace together: header/search, virtual version
rows and spacers, directory chooser, rename/path editors, selection/transfer variants,
icons, labels and confirmation copy. Every projected change enters an owned Dynamic
slot; no controller may clone IR or mutate fixed boxes/styles. External NSS metrics
supply row strides, popup padding/gaps and viewport reserves. Literal instance names,
paths and provider errors never become markup or unbounded entity keys.

Owned-slot Reorder accepts only a complete permutation of that owner's live direct
children. Validate every identity before mutation and retain focus, input drafts,
scroll, nested slots and motion. Virtualization stays bounded for 1,000/10,000 entries
and pins focused rows; selected transfer identities remain logical across windows.

Declare finite StableContent/ModalLayer and local Anchor facts in NXML. Anchor
resolution is unique in the closest enclosing tree scope after load and rejects
self/descendant targets; reload cannot change the anchor lifetime. The directory
popup stays in its owned slot and is hidden/revealed without destroying editor drafts.
Its actual height/indicator projection may change with viewport and content while
retaining scroll/inertia. ShowsVerticalIndicator is a viewport fact, not a lifetime
contract; other scroll/motion/anchor contracts remain immutable during Reload.

Preserve immediate single-click return, native post-navigation double click,
keyboard focus navigation, modifier/range selection, drag effects and process/root
identity. Retire old, foreign, hidden or disabled action sources. Selection, rename,
forget and delete continue through the existing service commands and confirmations.

## Version selection verification, 2026-10-11

- Full Desktop composition: 277 passed, zero failed; NXML compiler/runtime: 60
  passed; architecture: 69 projects passed; actual Avalonia backend: 30 passed.
- The complete workspace, bounded virtual rows/spacers, directory choices/editors,
  state variants, icons and fixed copy now enter owned Dynamic slots from external
  assets. No direct/hidden presentation factories remain in the three controllers.
- Owned Reorder validates complete permutations before mutation. Regressions reject
  foreign, stale, duplicated, incomplete and nested identities while retaining drafts,
  focus, scroll and nested ownership. Local anchor validation rejects missing,
  ambiguous and self/descendant targets without publishing a partial fragment.
- Existing 1,000/10,000-row bounds, exact scroll extent, keyboard pinning, immediate
  selection return, double-click continuation, modifier/range/drag behavior and
  root-qualified directory/service routes pass. Language/viewport/revision updates
  retain search/editor caret/focus, row identities, scroll and anchor contracts.
- Actual native previews in both themes cover four version kinds, selected tick,
  action capsules, search and directory rename at an 820px window. These manifests
  are read-only fixture data; no installation/launch measurement is claimed.
- Windows x64 NativeAOT and full-trim publications succeeded. Both actual executables
  ran --validate-shell, exited 0 and produced the positive Dynamic version selection
  validation marker. The existing Jint IL2098 warning remains unsuppressed.
- Scoped whitespace and diff checks pass. Artifacts: .tmp-cleanup-validation/version-nxml-*.
  Six main areas in the inventory remain open; the whole migration is still active.

## Account/setup/appearance boundary

Migrate the complete account onboarding form and its import/character choice collections,
all four first-run steps and the custom title-logo presentation as one related group.
External pages own explicit Dynamic slots; forms, choices, step projections and logo
visibility reload from .d.nxml without replacing drafts, focus, scroll or transition
objects. NSS declares fixed palettes, spacing and all welcome/flow/consent variants;
viewport formulas read validated external metrics. Fixed titles, hints, confirmations,
summary formats and validation copy are external; paths/usernames/provider errors are
literal data localized only before substitution. Authentication/persistence remain
sealed service routes with their existing cancellation/generation semantics. Passwords
clear on submission and retirement; stale/hidden/foreign sources and late pickers do
not gain authority from a new view. Preserve required test-channel diagnostics and
necessary telemetry policy, centering, step transition, final-save admission and no
persistence before Finish. Custom PNG/theme loading remains bounded off-thread and
rejects stale generations; the renderer only receives immutable raster/palette data.

## Account/setup/appearance verification, 2026-10-11

- Full Desktop composition: 279 passed, zero failed. NXML compiler/runtime: 60
  passed; architecture: 69 projects passed. No imperative node/style factories or
  fixed presentation copy remain in the account, first-run or appearance controllers.
- Account forms own their dynamic import/character rows. Language and viewport
  updates retain entity identity, drafts, focus and caret. Hidden, retired and
  foreign sources are rejected using applied visibility/enablement state; passwords
  clear on retirement and submission. Discovery regressions use a controlled real
  candidate source so background scans exercise the same reconciliation contract.
- All four first-run steps project through one owned external workspace. Late
  pickers cannot be adopted after leaving/returning to the same step. Required
  test-channel diagnostics, unavailable private choice, save admission and no
  persistence before Finish are covered. Bound step motion objects remain retained.
- Actual Avalonia previews cover the account form and all four setup steps in both
  themes, including an 800px viewport and forced diagnostics. The preview manifest
  declares read-only fixture data; no account authorization or setup persistence occurs.
- Custom logo visibility uses an owned fragment. PNG/theme decoding remains bounded,
  asynchronous and generation-qualified; only immutable raster/palette data reaches
  the renderer. Literal paths and provider metadata are never reparsed as markup.
- Windows x64 NativeAOT and self-contained full-trim publications succeeded. Both
  actual executables ran --validate-shell, exited 0 and positively emitted Dynamic
  bootstrap validation passed. Existing Jint IL2098 remains unsuppressed.
- Scoped whitespace formatting and diff checks pass. Artifacts are under
  .tmp-cleanup-validation/bootstrap-*. Four main areas remain in the inventory;
  the whole-product migration remains active.

## Wardrobe/library boundary

Migrate the entire player rail, skin/cape tracks, local PNG dialog and public skin
library together. Pages expose owned Dynamic workspaces; collection rows/cards and
library grid rows are external fragments, including empty/loading/failure/selected
states. Fixed labels, formats, palettes and all responsive dimensions come from
NXML/NSS. Account/catalog/texture queries, bounded lazy image windows and typed
mutations keep their existing identity, generation, cancellation and admission rules.
Provider metadata and file paths stay literal; localization occurs before insertion.
Viewport/language/busy updates reload the same controls, retaining search/path drafts,
focus, view selection, scroll and raster caches. Only immutable raster construction
and behavioral scrolling remain compiled; no controller-owned presentation factories.

Declare finite horizontal-indicator and wheel-routing facts in NXML. Reload may
change a non-scroll stack orientation while retaining identity; a scrolling stack's
orientation remains a lifetime contract. Add an owned Move operation for library
reflow: both slots must belong to the same top-level owned workspace, validate before
mutation, reject foreign/stale identities, cycles and fragments containing anchors.
Moving preserves live children/drafts/motion; ownership updates with the move so
subsequent reload/remove/reorder stay valid. Resize cannot retire catalogue cards or
reset their view merely to regroup rows. Anchor relocation remains unsupported.

## Wardrobe/library verification, 2026-10-11

- Full Desktop composition: 281 passed, zero failed. Wardrobe-specific regressions:
  27 passed, including all selectable dark accents, readable modal/card/selection
  contrast, correctly painted inset surfaces, and retained path/search caret/focus.
- NXML compiler/runtime: 62 passed; architecture: 69 projects passed; actual
  Avalonia backend: 30 passed. Both controllers reject imperative presentation
  construction and fixed copy through the architecture regression.
- Entire workspaces, skin/cape cards, library/site rows, local PNG modal and finite
  state variants now use owned external fragments, named text formats and NSS.
  Horizontal scrollbar/wheel facts are declarative. Responsive orientation and
  grid regrouping preserve live cards, draft/focus, view selection and raster data.
- Owned Move validates source/destination lifetimes and the same enclosing owned
  workspace before mutation, rejects foreign scopes/cycles/anchored subtrees, and
  keeps nested ownership coherent for subsequent reload/reorder/remove.
- Actual native previews in both themes and 800px windows cover the main wardrobe,
  local PNG modal and public library. They use declared read-only fixture accounts,
  textures and catalog data; no real account mutation or game launch is claimed.
  Preview teardown exited normally after returning from the fixture library.
- Windows x64 NativeAOT and self-contained full-trim publications succeeded. Both
  actual executables ran --validate-shell, exited 0 and emitted the positive
  Dynamic wardrobe validation passed marker. Existing Jint IL2098 remains unsuppressed.
- Scoped whitespace and diff checks passed. Evidence is under
  .tmp-cleanup-validation/wardrobe-*; three main areas remain in the inventory.
  The overall migration remains active.
