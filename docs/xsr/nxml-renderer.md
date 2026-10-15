# Nexa.Xsr.Renderer — external NXML and NSS

## Boundary decision

The user explicitly requests replacement of the UI.Next source/assembly identity with
Nexa.Xsr.Renderer while preserving the existing product design. This supersedes the
build-time PXML catalog requirement in XSR-207/208/209. It does not change Sidecar wire
IDs or the separately versioned plugin UI IR. Host markup/IR is not a plugin ABI.

- Renderer: layout, input, animation, drawing primitives; state in, intents out.
- NXML compiler: bounded XML parsing, external component expansion, typed primitive IR.
- NXML runtime: apply already validated IR to a tree. No service resolution or reflection.
- Desktop: selects external pages, supplies state and dynamic data/behavior.

### Additive finite control facts, 2026-10-13

`SegmentThumb` declares a local key in the same loaded IR document; a stack or
stack-layout Dynamic track resolves it to one declared transition-bearing thumb.
Missing, duplicate, self, or foreign-document targets fail before attachment.
`SegmentWidth` declares a positive finite expanded width on a command control.
These facts create renderer track/reveal components without changing their live
selection, drag, scroll or reveal progress during reload; changing the declarations
requires replacement. Tracks may reference a declared sibling thumb in their own
workspace, keeping dynamic options direct children for keyboard and drag routing.

`Checked` is a literal/data-projected boolean for command controls whose role is
CheckBox or Switch. Loading creates the existing renderer toggle; reload updates
its value while retaining input and toggle identity. `TransitionMovesSelf` optionally
declares the existing transition behavior and requires `TransitionKey`; it is an
immutable reload contract. No script, service lookup or new plugin wire shape is added.
`SelectionRequired` declares the existing group policy on a stack or stack-layout
Dynamic group, including an inventory that permits no selected row. Group identity
and policy remain fixed across reload.
Text input placeholder copy may reload in place; maximum length, password and tab
contracts stay fixed and reload never changes the draft, caret or selection.
The renderer honors the semantic `Localize=false` marker for input placeholders,
as it does for labels and ordinary text. A localized template substitutes literal
data after translating its format; drawing must not translate the resulting data.
Externally styled shell title text retains its declared typography across palette
changes; the programmatic renderer shell keeps its canonical defaults.
Feedback distinguishes already localized, data-bearing titles/messages from raw
vocabulary keys. Dialogs and notifications preserve that literal marker through
refresh and notification-detail expansion; default raw-copy callers keep their
existing localization behavior. Dock viewport arrangement/exit motion, parsed
Minecraft formatting spans, user background/raster data, literal host title data
and renderer-owned selection/scroll/input/motion updates remain narrow compiled
bridges; they may not create ordinary product layouts or style variants.
Input hit testing skips destroyed entities left in the last scene. Escape routing
also requires the dismiss target to be alive and visible in the current tree, so
owned removal or a newly staged modal cannot revive a retired or obscured action.

## File contract

Pages are `.nxml`. Reusable components are `.c.nxml`, with a single
`<nxml-component>` document root and one visual root underneath. Visual descendants
are `<Node>` or references by component name. A component reference expands its
external definition; its attributes override root defaults and its children append
to the component root. Cycles, unknown components, duplicate entity keys and expansion
budget violations fail before tree mutation. `<nxml-page>` wraps a page's visual root.
Node carries renderer primitives (Recipe/Role), not executable code or CLR types.

Styles live in `.nss` files. Selectors use component type, `.class`, and `#Key`; later
rules of equal specificity win. Explicit attributes override stylesheet layout values.
Visual declarations are typed/allowlisted. No script, network imports or reflection.
Styles and components are loaded once per document bundle, not during rendering.

Assets ship under `Ui/` next to the executable with CopyToOutputDirectory and
CopyToPublishDirectory. No embedded markup/component fallback. Component and stylesheet
paths resolve only within the selected bundle; no absolute paths or parent traversal.
User state and install data remain outside the presentation bundle.

## Migration and compatibility

Retain existing entity keys, state binding semantics, intents, geometry, gestures and
animations. Rename project references, tooling, tests and CI together. Dynamic service
collections still arrive through C# projection; their presentation templates belong to
NXML, and their visual constants/rules to NSS. Platform rendering remains compiled code.
Legacy PXML catalog generation is removed, rather than compiling the new XML back into
C# control definitions. Existing host-internal IR names may change; plugin protocol does not.

## Verification

Exercise parser budgets, component cycles/expansion, override precedence, invalid styles,
external-file publish presence, missing-file errors, state binding and scene parity.
Run Desktop/renderer/architecture regressions and NativeAOT/trim shell validation.
Do not claim visual or cross-platform acceptance solely from compilation.

## Authoring example

`Ui/Components/ActionRow.c.nxml`:

```xml
<nxml-component>
  <Node Recipe="CommandInput" Role="Button" Kind="ActionRow" Focusable="true" Clickable="true">
    <Text Content="Open" />
  </Node>
</nxml-component>
```

`Ui/ExamplePage.nxml`:

```xml
<nxml-page>
  <Page Key="ExamplePage">
    <ActionRow Key="OpenAction" Command="ui.example.open" Class="primary-action" />
  </Page>
</nxml-page>
```

`Ui/Styles/ExamplePage.nss`:

```css
.primary-action { Height: 40; Background: #0b5bcb; Foreground: #ffffff; CornerRadius: 20; Surface: Solid; }
#ExamplePage { Padding: 20,16,20,16; }
```

NSS uses the same typed property names as Node. Colors are #RRGGBB or #RRGGBBAA.
Primitive semantic metadata, commands and state bindings are not stylesheet properties.
Priority: component defaults, stylesheet specificity (type/class/key), then explicitly
provided attributes. Files are cached for a running process; restart reloads edited assets.
No runtime compilation or generated CLR controls are needed for a new component.

## Migration scope

All 24 existing embedded Desktop templates and the inline installation catalog row now
ship externally: 15 page documents, 10 product components and 16 primitive components.
Their 993 fixed page-layout declarations and primitive layout defaults are NSS rules.
The launch page's static visual styling and shared palettes also use NSS. Stateful C#
controllers still project dynamic collections, selected/disabled/error state, custom
render primitives and some legacy per-row visual values; those are not a claim that all
imperative presentation has been eliminated. Further extraction should use this same
pipeline without replacing service boundaries or rebuilding the visual design.

## Validation recorded for this migration

- Release solution build: succeeded, no build warnings/errors.
- NXML compiler/loader: 43 passing tests.
- Renderer kernel: 105 passing tests; Avalonia backend: 30 passing tests.
- Desktop composition: 267 passing tests, no failures.
- Architecture: 69 projects passed after removing the build-only control generator.
- Targeted format verification: exit 0 (workspace-loader warning remains visible).
- Windows x64 NativeAOT: publish succeeded; `--validate-shell` and `--validate-setup`
  both actually executed with exit 0. Published UI contains 41 NXML and 28 NSS files.
- Windows x64 full trim/link: publish succeeded and `--validate-shell` exited 0.
- Existing Jint IL2098 warning remains visible in both publish modes; no new renderer
  or NXML trimming warnings were observed. This is not macOS/Linux physical validation.

## Dynamic content contract (2026-10-11)

Pages own named `<Dynamic Key="..." />` insertion slots. A `.d.nxml` document starts
with `<nxml-dynamic>` and contains one visual root. A dynamic fragment can itself
contain slots for nested collections. Loading the same fragment repeatedly creates
independent instances and appends them in insertion order; later instances appear below
earlier ones in the default vertical slot. Keys are local to an instance, never global.

The loader validates and compiles the complete fragment before attaching it. A failed
load leaves the slot and its existing children unchanged. Replace, remove and clear
operate on loader-owned instances and destroy their bindings with the tree. Stale slot
or instance handles must not target a replacement entity. Controllers supply data and
select fragment names; they must not create presentation trees or generate XML strings.
Static labels, shapes, spacing and visual variants belong in external NXML/NSS assets.

`{data name}` is a literal substitution in dynamic documents, not XML interpolation,
script or a service lookup. It cannot select primitive recipes, roles or binding paths.
Data is checked against the same typed property schema and size budgets as static NXML.
State bindings remain `{state path}`. Dynamic documents require complete data before
compilation and cannot silently reuse parameters from a previous load.

Migration is complete only after controllers no longer create presentation structure;
adding the slot API alone is not completion. Rendering primitives and platform bridges
remain compiled code. Regression coverage must include append order, repeated keys,
failed-load atomicity, stale handles, nested slots, data escaping and idle network frames.


### Resource and task presentation follow-up (2026-10-11)

Resource searches retain all fetched project facts for the current search, without a Desktop project-count, page-count or aggregate text display cap. Rows and media remain viewport-bounded. A duplicate-only page pauses automatic admission; the user can explicitly request the next page. Provider response validation and request limits remain in Services. Search provider and dual-provider category filters are explicit query fields and participate in cache identity. World is an additive resource kind. Dropdown presentation uses external dynamic fragments in a named page slot, and game versions reuse the install catalog service state/command.

One content-update transaction owns one task, including all file downloads, validation and final replacement. Child downloads report to that task without registering additional cards. Active task cards precede terminal cards without altering task state or publication order.

The Java diagnostics, manual-download workspace and policy-explanation cards are removed from Settings, including their dedicated presentation state and handlers. Java selection, inventory management and launch-time detection/download services remain available.


### Dynamic rollout validation and remaining scope

The network/proxy and network-diagnostics controllers now load external dynamic fragments;
the architecture regression forbids imperative presentation factories in these migrated
controllers. Settings sections can use `Dynamic Layout="HorizontalPager"`; ordinary slots
remain vertical. The loader rejects a dynamic fragment outside a slot and rejects ordinary
page IR inserted directly into a slot. Repeated fragment loading and clearing avoid scanning
all owned instances for every appended row.

Validation of the follow-up: 264 Desktop composition tests, 47 NXML tests and architecture
checks across 69 projects passed. Windows x64 NativeAOT and full-trim publication both
succeeded, and each published executable actually ran `--validate-shell` with exit 0.
The pre-existing Jint IL2098 warning remains. Native Avalonia previews exercised light/dark
settings and network pages, resource filters and directory renaming; resource preview data
was a fixture, not a live upstream availability check. Targeted resource-service regressions
also cover single-task batch success/failure, source/category cache separation and actual
provider query mapping.

This does not complete the entire static/dynamic extraction: other Settings sections,
resource detail/list projections, installation rows, task cards and several remaining
controllers still create presentation nodes. They must be migrated in subsequent bounded
slices before extending the no-factory architecture rule to the entire Desktop project.
The Dynamic API and two migrated network controllers are complete; whole-Desktop
externalization is still in progress.


### Retained dynamic reload (2026-10-11)

A controller may reload an owned `.d.nxml` instance with new typed data. Reload validates
its entire existing shape and bindings before changing any live node. It preserves entity
identity, focused input drafts, nested Dynamic contents, scroll/pager positions and live
progress/transition objects. Only changed declarative properties invalidate rendering;
reloading identical data does not schedule a frame. Shape, recipe, bindings or transition
contract changes require explicit replace, and a rejected reload leaves prior content intact.
This keeps dynamic data projection in external templates without forcing controllers to
mutate text/style components or recreate a card for every progress sample.

The next complete consumer is Task Center: cards and steps enter Dynamic slots, all visual
values come from NSS, and header/card data changes use retained reload. Static semantic
labels and terminal-state wording must ultimately move into fragment variants; the current
task projection still formats summary/state captions and is recorded as remaining work. Task routing and ordering
remain controller behavior; service task contracts are unchanged.


Retained-reload slice validation: 50 NXML tests, 264 Desktop tests and architecture checks
for 69 projects passed. Native Avalonia task-center previews in light and dark themes
exercised active/finished ordering and expanded steps. Both Windows x64 NativeAOT and
full-trim executables actually ran the added task smoke (card load, nested steps and
retained progress reload) and exited 0. The existing Jint IL2098 warning remains.
See `nxml-migration-remaining.md` for the whole-objective inventory and completion gates.


### External overlay layers

`Dynamic Layout="Overlay"` is a named slot whose children overlap, using their declared
alignment and size. It has no stack layout. Desktop-owned nonmodal layers load a `.nxml`
slot through the normal loader, show that slot on the stage, then append `.d.nxml` bubbles
without reparenting them away from their owner. An empty layer must not intercept page
input. Each layer owns disposal and stage dismissal. Dock registration tracks explicit
entities rather than scanning the entire page tree each frame, and prunes destroyed entries.
Dock flow offsets and animation remain behavior; base dimensions, insets, gaps and paint
come from external assets. Task/launch/process bubbles share this path.

NXML `Selected` declares command-control selection; `Localize="false"` preserves literal
user/provider text and accessible names. Both values participate in retained reload, so log
rows require no imperative text, semantic, selection or style construction. Neither changes
service data or introduces a plugin ABI.

### Settings editor text contracts

Settings catalogue rows, selector options, argument rows and hook-command rows are
owned dynamic fragments. Input drafts are assigned only by controller interaction,
never from template Content. `PreserveTabs` is a text-input contract (default false),
while `MaxLines` (0 means unrestricted) and `TrimOverflow` declare text presentation.
Reload may update text clipping but cannot change input sanitization contracts.
These host-internal properties do not change service or plugin contracts.

## Literal dynamic text formats

A dynamic template may mix static text with named `{data name}` placeholders only
in Content, Label and Placeholder. Exact data bindings keep their existing semantics.
Keys, commands, styles and primitive metadata cannot use mixed formats. Expansion
runs once on the parsed format: supplied data is never parsed as XML, markup or a
binding. Each format allows at most 256 placeholders. Output consumes the existing 1 MiB expanded-data budget, including repeated
parameters and static text. Missing parameters and malformed braces fail before
loading or reloading a tree.

CompileLocalized is additive to Compile. It translates the static format before
substitution; translated formats must preserve exactly the same named parameter
set. Product formatted text declares Localize=false to avoid translating user data
after substitution. The Desktop adapter does not cache language-specific compiled
fragments across localizers. This is text formatting, not an expression language.


### Captured platform presentation

Platform status/group vocabulary is a keyed literal text-only NXML asset; observation rows,
right-side device/resource lists, preflight issues/actions and hardware advice are external
`.d.nxml` projections with NSS surfaces and padded inner bodies. Unavailable list observations
retain their status rather than becoming a verified empty-device/default-resource list.

The frame update requests one asynchronous preflight report for a captured machine snapshot.
Repair and advice presentation reuse that report, including when no advice card is available;
neither construction nor rendering invokes the service. Typed facts and instance scope remain
service data. Confirmation and Java-picker callbacks capture the snapshot, scope and projection
generation; returning to the same cached snapshot does not revive retired UI actions.

This slice passed 267 Desktop tests, 56 NXML tests, architecture checks for 69 projects and
actual light/dark native previews. Both Windows x64 NativeAOT and full-trim executables ran
the new platform template smoke and Java-panel retirement assertions, then exited 0. Existing
Jint IL2098 remains. The whole externalization inventory is still open in
`nxml-migration-remaining.md`.

### Captured runtime / advanced tools

Runtime diagnostic groups and fields, Trace controls/status/rows, advanced
automation/routes/facts/flags, probe status and redacted settings rows are external
assets. Each collection has an owned Dynamic slot. Refresh/reload retains controls;
Trace paging uses its captured bounded metadata, with no request/state payloads.
Advanced probe completion and settings paging do not reconstruct the settings
page. Diagnostic unit/metadata formats are external literal named text; fixed
clock/motion/availability terms and route labels come from RuntimeTerms.nxml.

This closes these three controller partials only. Shared dialog feedback copy,
instance diagnostics and other custom settings/resource/account/launch surfaces
remain in the migration inventory.

## Captured instance tools

Instance config/log editing, offline readiness and timeline fragments own their
result/row slots. Local reads, paging and save-preview completion must not replace
the toolbar or editor. Capture instance/area/path and original document identity
before asynchronous work, retire it on navigation, and require the preview to match
the current lossless draft before dispatching save. Terms and mixed localized
metadata formats are external assets; file names and source contents stay literal.
See nxml-migration-remaining.md for the related parity and publication evidence.

## Declarative overlay and scroll facts

OverlayMotion (None/Notification/DialogScrim/Dialog), LiveSetting
(Off/Polite/Assertive) and DismissCommand use finite renderer primitives and validated
semantic IDs. Element/stack nodes may compose command input without changing their
layout recipe. ShowsVerticalIndicator, StickToEnd and ScrollGesture require Scroll.
Command capsules may declare Scroll only when their authored HoverExpand is true;
only ShowsHorizontalIndicator and UseVerticalWheelForHorizontalScroll apply there.
The complete caption remains on the same command/accessibility owner. Horizontal
offset and its thumb are renderer presentation state, retained across compatible
reloads; theme packs cannot declare Scroll or change its input contract.
These are host presentation facts, not services or a plugin execution interface.
Reload may update the live-region urgency but preserves input, motion and scroll
contracts/state; incompatible projections fail atomically before mutation.

## Owned virtual collections and local anchors

Reorder validates a complete permutation of live, direct fragments owned by the
same Dynamic slot before changing order. It creates no nodes and preserves fragment
identity/state. StableContent and ModalLayer are finite Boolean renderer annotations.
Anchor is a literal local key, validated and uniquely resolved in the closest tree
scope after load; self/descendant anchors are invalid. All are immutable reload
contracts. ShowsVerticalIndicator can reproject in place, retaining scroll objects
and offset/inertia while invalidating layout so gutter and extent are recomputed.
