# Sidecar UI documents

## Shared NXML schema 3

New UI is authored in the product's `.nxml`, `.c.nxml`, `.d.nxml` and `.nss`
suite. Schema 3 supersedes the resource-card-only execution policy below; schemas
1 and 2 remain wire-compatible legacy adapters. There is no second control enum
for NXML and no feature-owned registration slot. The shared compiler, component
library, styles, state bindings, layout, input, accessibility and dynamic reload
implement both product UI and Sidecar UI.

`SidecarNxmlDocument` contains a stable node selector, operation, entry point and
immutable text files. `UiModule` transfers its encoded bytes and SHA-256 hash at
registration. Both peers negotiate feature `Nxml` (bit 16). Sources are limited
to 96 portable relative filenames and 512 KiB of actual UTF-8 source bytes;
session source budget is 4 MiB/32 modules. Files never become filesystem paths.
The shared compiler validates the entire initial projection and host state
references before READY. Mirror-state changes compile on a coalesced background
worker and publish a sealed immutable `XsrNxmlModuleSnapshot`. Rendering never
parses source, compiles documents or performs IPC.

Selectors address any named product node: `#VersionList`, or an ancestor path
such as `#SettingsPage/#Toolbar`. They match every corresponding live host node,
including retained and subsequently created pages/fragments. The generic
coordinator runs after logical frame projection, so new nodes are covered before
their first paint. It excludes module-owned nodes from target discovery. Future
pages and shared components need no Sidecar adapter or registration changes.

Operations are append/prepend/before/after, modify and replace. Modify overlays
the root's ordinary renderer components, retaining its logical children;
replace presents the new root/subtree. Latest live layers take precedence.
Replacement inherits the host node's visibility. Logical nodes, controller
handles, bindings, live values and controller-owned child permutations stay
intact. Retirement restores earlier live layers or current product content,
including changes made underneath a replacement. Stale entity generations and
inactive exposure identities cannot submit commands. Failed mounts roll back
without deleting host nodes or another provider's presentation.

The entry document may contain `{data plugin.state.name}` values from the
session's scalar state mirror; values remain literal data, never interpolated
XML. Host `{state ...}` bindings use the ordinary shared binder. Bundle-local
components extend an immutable copy of the current host library, and styles
overlay the current host NSS stylesheet. A dynamic file `Rows/001.d.nxml` loads
into local `<Dynamic Key="Rows"/>`; multiple files in that folder append in
ordinal filename order. `Rows.d.nxml` addresses the same slot directly. Reload
retains entity identities, focus, selection, caret, drafts and nested slot state.
Expanded content is bounded to 16,384 nodes per document, including fragments.

Commands are standard NXML `Command` bindings, with optional literal/data-bound
`Argument`. `ArgumentFrom="Draft"` reads the current value of the local input
named `Draft`, so form submission requires no page-specific controller. References
are checked for missing keys, cycles and depth before load. Inputs, toggles and
ranges expose their normal intent value through the shared renderer.
The Desktop composition edge routes session-declared commands through the
existing asynchronous numeric exchange, codecs, cancellation and timeouts.
No Sidecar object, transport, service reference or callback enters the renderer.
The global intent route consumes session-owned commands exactly once, allows
ordinary host commands through, and drops retired-source commands before host
handlers. Diagnostics observe intents before routing without collecting drafts.

## Standalone pages, onboarding and pressure retirement

`Page=7` and `OnboardingPage=8` require both Nxml and Pages (bit 64). Targets are
unique single-key `#PageKey` identities within the session. `ui.sidecar.page.open`
resolves only within the active invoking module's session. The onboarding continue
intent is `ui.sidecar.onboarding.continue`; it cancels pending exchanges, clears
retired page/draft state and resumes the product onboarding flow. A registered page
is cached locally; opening it does not fetch its source again.

Current runtime permits at most 128 active schema-3 documents globally. The legacy
64-module limit below describes the separate schema-1/2 adapter, not schema 3.
Registration splits large payloads into 60 KiB blocks (field 5 then continuation IDs
from 1024); source budgets and complete payload SHA-256 validation still apply.
Critical OS memory pressure may cooperatively DEACTIVATE the existing session;
recovery ACTIVATE reuses its registration. Manual stops are never automatically revived.

## Legacy text card schema 1

The first executable New UI ABI is a versioned text card, not PXML source or the compiler's
internal IR. `UiModule` continues to transfer hashed content at registration. With execution
admission, the whole batch is validated before READY and projected into sealed immutable XSR
State only during an active session. Without admission the existing cache remains inspection
only. UI.Next never reads the session, registry, transport or module runtime.

Payload: ordered unique binary TLV fields 1 schema U32 (=1), 2 host slot Str, 3 title Str,
4 body Str. Unknown fields are skipped. Payload <=4096 bytes; title 1–80 UTF-16 characters,
body 1–512 characters, no control characters. Slot is an exact host semantic ID. Flags and
codec are zero; resources, URLs, actions, bindings and scripts are outside this schema. The
host grants slots explicitly; no plugin entity names are exposed to the product tree.

Limits: 16 host slots, 32 modules per session, 64 active modules total, 8 per slot. The latest
live activation wins for each slot. Retirement restores its predecessor or hides the slot.
Snapshots retain immutable documents. State publications are serialized/coalesced outside
business locks. Activation owns its lease before publishing, including observer reentrancy;
all terminal paths retire it, and a delayed activation cannot resurrect a stopped session.

Desktop grants `ui.resources.extension-card.v1`. It renders a compact title and wrapping body
below the resource toolbar using existing typography and colors, with a bounded scrolling
viewport. The slot occupies no space without a live module. Accessibility and visual text
are literal together. The search, result list, focus, navigation and download actions retain
their host ownership. There is no render-time parsing, IPC, service lookup or plugin code.

## Interactive document schema 2 (XSR-806)

Schema 1 text cards stay compatible. Schema 2 uses the same ordered TLV fields 1 schema U32
(=2), 2 host slot Str, 3 title Str, 4 body Str, and field 5 Bytes containing the node sequence.
The entire payload is limited to 32 KiB. The node sequence begins with a u16 node count and
then u16-length-prefixed ordered TLV node records. Fields: 1 Id U32, 2 Parent U32, 3 Kind U32,
4 Label Str, 5 Command Str, 6 ValueState Str, 7 EnabledState Str, 8 VisibleState Str,
9 Resource Str, 10 Argument Str. Unknown ordered fields are skipped. IDs are contiguous 1–64,
parent zero denotes the slot root, otherwise the parent precedes the child and is a Stack/Row.
Maximum depth is eight. Kinds: Text=1, Button=2, TextInput=3, Toggle=4, Image=5, Stack=6,
Row=7. Label is at most 256 characters, button argument at most 512; controls are rejected.
The constructor copies nodes and snapshots expose read-only collections.

Button, TextInput and Toggle require a Command registered by this same session. Bindings
resolve once to the same session's numeric State contract and typed mirror cell. Buttons and
inputs accept string arguments; toggles require Boolean Command and ValueState codecs and
negotiated BinaryPayloads. Inputs submit through a host-owned submit button, with a 512
character draft. State can update text/input values, toggles, visibility and enabled status.
Unavailable bound values disable interaction. Ancestor visibility/enabled predicates are
rechecked at dispatch. State updates retain entities and uncommitted drafts unless the bound
input value itself changed. There is no parsing, reconstruction or IPC on unchanged frames.

Image nodes refer only to Resource semantic IDs listed in the module's RequiredResources.
Registration requires the hash-verified cache entry and a supported static PNG/WebP/JPEG
resource image, at most 1 MiB encoded and 1024×1024 pixels each. The sum of rendered node
pixel reservations is at most 8 MiB per module, and distinct encoded image content is also
limited to 8 MiB per module. Repeated references share one admitted image carrier across
registration documents. No URL, path or unregistered resource is read.
The immutable local image carrier crosses presentation State; only bytes crossed the peer IPC.
Native decoding stays in the backend with its existing decode-failure handling and cache.

A current module view has a fresh exposure GUID. Replacing or retiring it revokes captured
intents and cancels pending exchanges. Restoring a predecessor creates a new exposure GUID,
so stale intents from its earlier exposure do not regain authority. Session failure,
deactivation and disposal remove the view and unsubscribe live mirror bindings. Source-page
navigation cancels its requests and retires its control entities before the page is exposed
again. Command/query effects use the existing asynchronous numeric data plane, timeout,
cancellation and backpressure; no child delegate or session object enters UI.Next.

The existing resource extension slot is the legacy consumer for schema 2 documents.
It supports plugin-authored finite forms and nested layouts while keeping product navigation,
service execution, trust decisions, palette and viewport sizing under host ownership. Arbitrary
scripts and direct host service bindings remain outside the NXML language; global presentation
modification and replacement use schema 3 above.
The independent Plugin SDK/compiler/package UI IR freeze and real-engine long-duration
compatibility still require the external repository and physical evidence.

### Versioned onboarding receipts

An onboarding declaration may set `SidecarNxmlDocument.OnboardingRevision`
(default `"1"`). Its optional string TLV field is 64; fields 5–63 still carry
source chunks. Use 1–64 ASCII letters/digits, dots, hyphens or underscores.
Existing bundles that omit it remain revision 1.

Completion is stored locally by authenticated package identity, page target,
and revision. Session restarts, language/style changes and ordinary launcher
updates do not repeat completed pages. Increment the revision when a page adds
a new decision users must see; new page targets are independently pending.
For example, `new SidecarNxmlDocument("#CloudSetup", OnboardingPage, "main.nxml",
files) { OnboardingRevision = "2" }` introduces the second setup revision.
The SDK's enum is `SidecarNxmlOperation.OnboardingPage`.

Keep the continue/skip button bound to `ui.sidecar.onboarding.continue`.
Merely rendering a page does not complete it. Failed receipt writes keep the
page visible. The Host wraps every pending batch in its welcome and completion
screens, even when only one page is pending. These two screens are never filtered
by revision. An interrupted batch begins with welcome again, resumes unfinished
pages, and ends with completion. The batch is closed only by its final button,
including when all content pages were already completed before an interruption.
With no pending pages or interrupted batch, nothing is shown.

On a fresh installation these pages participate in the Host's first-run flow:
Host welcome → storage → privacy → pending Sidecar pages → Host summary. Do not
add another welcome/end pair in the Sidecar. Initial discovery must settle before
the content stage advances. First-run receipts stay in memory until settings are
saved, then persist under the selected data directory; cancelling first-run may
repeat its pages. This preserves the empty-directory validation for a new location.
Configured installations continue to use the standalone versioned batch described
above, including its welcome/end pages and durable per-page receipts.
