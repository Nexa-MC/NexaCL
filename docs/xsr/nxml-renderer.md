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
