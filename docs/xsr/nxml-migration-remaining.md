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
- Resource filter overlays already use fragments. Other resource presentation is
  still imperative.
- Shared reload preserves entity identity, nested slots, drafts, scroll, focus,
  progress and transition objects; it validates the complete projection first.
- Bootstrap of detached pages is now owned by the NXML loader, not a temporary
  controller-created visual tree. Other controllers must migrate to that API too.

## Remaining slices

| Area | Authoritative entry points | Remaining work |
| --- | --- | --- |
| Settings and instance management | `SettingsPageController.cs` and partial files | Replace all remaining row/group/editor factories, diagnostic cards, content/detail/snapshot/server/export structures and their style mutations with templates. Preserve drafts and list virtualization. |
| Resources | `ResourcesPageController.cs` and partial files | Search toolbar, categories, virtual rows, detail/version/dependency and install confirmation layouts; content and state variants must be external. |
| Launch and installation | `LaunchPageController.InstallCatalog.cs`, `.Installation.cs`, `.FrameProjection.cs`, `.Styles.cs`, `BedrockInstallPageController.cs` | Catalog/loader rows, state panels, choice tracks and runtime presentation styling. |
| Version selection | `VersionSelectionController.cs`, `.Window.cs` | Row window/virtualization, directory choices/editor, popup body and selection styles. |
| Wardrobe | `WardrobePageController.cs`, `WardrobeLibraryPageController.cs` | Skin/cape cards, details, empty/loading/failure surfaces and interaction variants. |
| Accounts and first run | `AccountFormController.cs`, `FirstRunController.cs` | Form structures, state copy, action availability and complete OOBE composition. |
| Process and dock copy | `LaunchPageController.Process.cs`, `ProcessLogPageController.cs`, `DesktopTaskBubblePresenter.cs` | Layouts and visual constants are external; migrate remaining conditional labels and formatting, then audit all lifecycle paths. |
| Global overlays | `DesktopCommandPalette.cs`, `DesktopFeedbackPresenter.cs` | Palette, notification/dialog collections and actions, keyboard focus and dismiss behavior. |
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
