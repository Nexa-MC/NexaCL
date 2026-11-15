# Sidecar Host business ABI

The Host exposes general launcher operations to publisher-verified native Sidecars. This boundary contains no plugin SDK types, plugin capability names, or guest identifiers. A Sidecar that executes untrusted guests must enforce its own admission and permissions before invoking Host operations.

## Connect and discover

Keep a single reader for the Sidecar connection. Negotiate `BinaryPayloads` and `HostRequests`, plus `LargeGeneratedDtos` for complete business DTO support, activate `SidecarHostRequestClient` with the negotiated features, and pass incoming command/query result frames to `TryAcceptReply`. Forward other frames to the existing peer state machine. Concurrent callers share the client; they must not each read the connection.

Construct `SidecarBusinessClient` with that transport client and an XSR dispatch observer. Call `DiscoverAsync` after activation. Discovery returns the wire version, DTO-and-operation-catalog fingerprint, and the numeric operations available to this session. During launcher composition, it reports unavailable; retry discovery with a bounded delay and cancellation. Do not retry a mutation to recover from connection failure.

The Desktop authorizes only the current active session of a publisher-verified package. A previous session identifier does not authorize a restarted process. The session must also remain active when dispatch starts. No package name receives a plugin-specific exception. Native Sidecars already execute with the current user's OS authority; this API is not an OS sandbox.

## Routes and data

`eng/sidecar/host-business.json` assigns explicit numeric IDs to 90 domain commands and queries. The reserved query 255 discovers capabilities; query 254 reads public state. Route names identify the underlying domains: accounts, library, game processes, install, content, settings, themes, and resources. A declared route is advertised only if its domain router actually supplies it.

`Nexa.Sidecar.Host.Data` contains independent client records. Generated codecs translate these records to the Host's domain DTOs using a bounded binary wire format. No CLR object crosses the process boundary. Values are bounded to 8 MiB. With `LargeGeneratedDtos`, the existing binary bytes field carries the first 65,535 bytes, field 6 declares total length, and fields 7 onward carry consecutive continuations. Without negotiation, large calls are rejected before execution and large query replies report feature unavailable. Legacy payload bytes and primitive limits are unchanged. Generated DTO `ToString()` returns only the type name, so logging a request cannot dump credential fields. Init-only fields without constructor parameters are required in the client so callers cannot accidentally substitute zero values for Host defaults. An incompatible fingerprint is rejected before business calls; published catalog schemas remain available when a new catalog is added.

Public state consists of library, account display records, game sessions, installation catalogs, login progress, character choices, and the settings revision. Account credentials and private profiles are excluded. Each field has its own domain revision. Missing domains have revision -1 and null content. The aggregate is not a cross-domain transaction: commands must still validate their expected revisions. Login progress includes the user-visible authorization code and URL, not the provider polling secret or access token.

State polling is a snapshot mechanism, not a replay log. A consumer must refresh after a completed mutation before relying on the resulting state. Watcher adapters belong in the consuming Sidecar and must handle disappearing identities and revoked sessions.

## View state (package 0.1.0-alpha.5)

Use `SidecarViewStateClient` when source files or model values exceed the generic
string codec's 32 KiB limit, or when a model refresh must retain the current page
and input drafts. Discover this catalog independently after activation. It shares
the session's presentation revision with `SidecarPresentationClient`; a successful
write through either client invalidates the other's previously read revision.

| ID | Kind | Semantic route | Input / result |
| --- | --- | --- | --- |
| 1023 | Query | Catalog discovery | Empty / version, fingerprints, admitted IDs |
| 1024 | Query | `presentation.viewstate.revision` | `HostViewEmpty` / `HostViewRevision` |
| 1025 | Command | `presentation.viewstate.replace` | `HostViewReplace` / domain outcome |
| 1026 | Command | `presentation.viewstate.models.update` | `HostViewModelsUpdate` / domain outcome |

For replacement, encode each document using `SidecarNxmlDocument.Encode` and pass
the resulting byte arrays in `Documents`. Each optional `HostViewModel` names a
zero-based document index and its complete model. Each `HostViewValue` supplies a
key and strict UTF-8 bytes. Omitted models start empty. The Host merges a document's
model over its session mirror values for compilation; it never writes those model
values into the mirror or another document.

For a data refresh, send a nonempty array of `HostViewModel` values to the model
update command. It replaces each named document's entire explicit model; omitted
documents keep theirs. All affected documents compile before publication. Exposure
handles remain stable, allowing the renderer to preserve navigation, control
identity and unsaved drafts. A later source replacement retires these handles.
Stale revisions, duplicate document indices, duplicate keys, invalid UTF-8 and
invalid NXML/model combinations reject the whole update. No partial model publishes.

The source limits of the presentation catalog still apply. Models additionally
allow at most 4 MiB of UTF-8 key/value text per session, 4,096 fields per document,
256 characters per key and 512 KiB per value. The 8 MiB generated-message limit
also includes encoding overhead; the individual maxima cannot all be combined in
one request. Source replacement clears prior models unless new models are supplied.
Deactivation and reactivation retain committed models. A model update does not
register new command contracts, grant guest permissions or provide access to
another Sidecar's documents.

## Dynamic presentation (package 0.1.0-alpha.4)

`SidecarPresentationClient` publishes NXML documents through the same Host request
transport. Call its `DiscoverAsync` after activation, then use its generated
`Queries` and `Commands` routers. This catalog has its own fingerprint and does
not change the base or effects catalog. Hosts without this catalog cannot accept
dynamic pages; a consumer must keep those features unavailable.

| ID | Kind | Semantic route | Input / result |
| --- | --- | --- | --- |
| 767 | Query | Catalog discovery | Empty / version, fingerprints, admitted IDs |
| 768 | Query | `presentation.nxml.revision` | `HostPresentationEmpty` / `HostPresentationRevision` |
| 769 | Command | `presentation.nxml.replace` | `HostPresentationReplace` / domain outcome |

Read the revision first. Supply it as `ExpectedRevision` with the **complete**
desired document set, including any initial registration documents to retain.
Each document includes its target, NXML operation, entry point, source files and
onboarding revision. Pass `"1"` for an ordinary document's onboarding revision.
This catalog uses the generic 32 KiB limit for each string field; use the view-state
catalog above for larger individual source files.
An empty document array removes the invoking session's complete presentation.
On success the revision increases by one; a stale revision rejects the request.
The request does not accept a package name or session ID: transport authentication
selects the owner, including when two admitted packages use the same page target.

Candidates use the same compiler and admission limits as initial NXML registration:
32 documents and 4 MiB of encoded document payloads per session, 128 active
documents globally, and the existing 512 KiB source budget per document. Page
operations also require negotiated `Pages` support and unique single-key targets
within the document set. Large requests require `LargeGeneratedDtos`. The entire
candidate compiles before replacement. Compilation failure, a stale revision,
budget rejection or cancellation before commit leaves the previous set intact.

Replacement invalidates previous exposure handles and cancels their pending UI
commands. Deactivation removes the presentation; reactivation restores the latest
committed set with fresh exposures and the same revision. Disconnect removes all
of the session's presentation. A lost acknowledgement does not prove a command
failed: read the current revision before deciding whether another update is needed.
Serialize writes within the Sidecar so its page and model publishers cannot race.

This is a general presentation capability. The Host does not parse guest manifests,
plugin permissions, registration handles or SDK models. A plugin Sidecar must
validate its guests and translate their models and actions before calling it.
The production PluginSidecar UI migration is still in progress; availability of
this catalog alone does not mean that the old Host plugin UI has been removed.

## General effects (package 0.1.0-alpha.3)

`SidecarEffectsClient` uses the same authenticated connection and has its own
`DiscoverAsync`, command router, and query router. Discover each client you use.
Its discovery query is 511; `eng/sidecar/host-effects.json` assigns IDs 512–522.
The base catalog at 255 and its fingerprints are unchanged. Discovery failure in
one client does not clear the other client's cache. Older Hosts report the effects
catalog unavailable; the consumer must disable dependent operations.

| Operation | Input and behavior | Result |
| --- | --- | --- |
| Account eligibility | Account index and expected roster revision; reads the profile inside Accounts | Whether launch is allowed and whether provider validation is required at launch |
| Account refresh | Same revision-checked identity; resolves through the configured account provider | Success or domain failure; no token or resolved identity |
| Java inspect | Absolute executable path; inspects that executable | Candidate or null |
| Java selection | Optional absolute instance directory and version-local flag; version-local requires an instance | Selected candidate or null |
| Java requirements | Absolute instance directory | Requirement range, recommended component and failure classification |
| Power | Empty request | Battery status, optional percentage, power-saving state; unavailable if required observations are missing |
| Preflight | Absolute instance/root directories and instance ID | Issues from a refreshed scope and the exact snapshot evaluated by the preflight service |
| Settings definitions | Empty request | Public definitions and the version-local Java sentinel; excludes proxy credentials and non-exportable settings |
| Stop game process | Session ID for a managed game process | Domain cancellation result |
| World snapshots | Instance directory and world name | Snapshot identities, creation times, file counts and sizes |
| Settings validation | Public setting key and proposed override | Validation result; does not mutate settings |

These are general Host capabilities. A native Sidecar executing guests must still
apply each guest's grants, confirm sensitive actions, and own its opaque handles.
The Host does not accept a guest identity or interpret plugin permissions.
Preflight sends plain issue fields, never capability interfaces or runtime objects.
A failed refresh can have provider-side effects; do not automatically retry it.
Settings mutations still go through the version-checked base domain commands.

The version-1 base and effects catalogs are immutable once released. New shapes or
operations require another independently discovered catalog, or an explicitly
negotiated version retaining the old schema. Do not modify a frozen fingerprint
and call that a compatible addition. These transport contracts do not add a guest
SDK operation or allocate a guest EXP_ID.

## Failure and lifetime

There are at most 32 pending calls per client. The request and reply must agree on command/query kind and codec. A cancelled or timed-out call retains its slot until the peer finishes or the connection is disposed. This prevents callers from bypassing backpressure by repeatedly cancelling requests. A stalled send cannot prevent local cancellation; a frame write itself is not interrupted halfway through by a caller's token.

Transport errors and domain errors are distinct. A successful transport reply may contain a rejected domain operation; its XSR error kind and semantic code are preserved. Host exception messages are not sent in that error envelope. Cancellation is best effort: if a mutation was accepted before cancellation, its effects may have committed. Query the domain's state before deciding what to do next.

Failed discovery clears the client's availability cache. Dispose the transport client when the connection ends to fail pending calls and release retained slots. The launcher does not wait for a Sidecar to discover or use these services in order to finish normal startup.

## Generate and verify

Build `Nexa.Services.Composition` with the pinned SDK, then run:

```sh
python eng/xsr/generate_service_wire.py \
  --assembly src/app/Nexa.Services.Composition/bin/Release/net10.0/Nexa.Services.dll \
  --roots eng/sidecar/host-business-roots.txt \
  --output src/app/Nexa.Services.Composition/Composition/Sidecars/BusinessWire.Generated.cs \
  --namespace Nexa.Sidecar.Host.Wire \
  --sidecar-contracts src/sidecar/Nexa.Sidecar.Host.Contracts
python eng/sidecar/generate_host_business.py
```

Generate the independent effects codec with the same script using
`host-effects-roots.txt`, the `Effects/BusinessWire.Generated.cs` output,
`--namespace Nexa.Sidecar.Host.Effects.Wire`,
`--sidecar-contracts src/sidecar/Nexa.Sidecar.Host.Contracts/Effects`, and
`--sidecar-data-namespace Nexa.Sidecar.Host.Effects.Data`. Its assembly is
`Nexa.Services.Composition.dll`. Generate its routes with
`python eng/sidecar/generate_host_business.py --catalog host-effects.json`.

Add `--check` to the codec command and `--verify` to the route command to verify without modifying files. Desktop tests exercise the independent codecs, authority revocation, unavailable state, UTF-8 paths, and preserved domain errors. Sidecar transport tests cover concurrency, cancellation, malformed replies, and backpressure. The package consumer verifies that the client works without launcher service assemblies, including NativeAOT.

## Receiving document-local form submissions

Negotiate `BinaryPayloads | Nxml | UiForms` and register the UI command with input
codec `SidecarWireCodecs.GeneratedDto` (6). Add `Pages` for standalone pages and
`LargeGeneratedDtos` if a form may exceed 65,535 bytes. The registered result codec
still governs the reply; form inputs do not change it. Older peers keep their
scalar command format. Do not enable `UiForms` until your command handlers decode
the form envelope.

When the user invokes that command, Desktop snapshots input drafts from the
invoking document. This includes its hidden inputs, as with a normal form, but
excludes Host controls, other pages and nested content owned by another exposure.
Each key is the input's NXML `Key`. A duplicate, empty, control-character-containing
or overlong key rejects the entire submission. Capture happens on the UI thread;
no renderer objects cross the connection.

```csharp
var request = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span);
if (request.Value.CodecId != SidecarWireCodecs.GeneratedDto)
    throw new InvalidDataException("Unexpected UI input codec.");
var form = SidecarUiForms.Decode(request.Value.ToArray());
// form.Argument is the clicked control's action argument.
// form.Fields contains ordinally sorted keys and strict UTF-8 byte values.
foreach (var field in form.Fields)
    HandleField(field.Key, System.Text.Encoding.UTF8.GetString(field.Utf8Value));
```

The envelope begins with version 1 and an exact schema fingerprint; it rejects
unknown schemas, trailing data, malformed UTF-8 and non-increasing keys. Limits
are 128 fields, 256 characters per key, 512 KiB per value, 32 KiB of UTF-8 action
argument and 4 MiB for the complete encoded envelope. Rejecting an oversized form
does not truncate or partially dispatch its values. Empty drafts are valid.
The source exposure and registered command must still be active. Replacing the
source or retiring the session cancels outstanding calls. Model-only updates
retain exposures and input drafts.

The binary codec is generated from `eng/sidecar/ui-forms-roots.txt` using
`generate_service_wire.py`, with the Protocol assembly and output namespace
`Nexa.Sidecar.Protocol.Forms.Wire`. The shared package consumer verifies this
codec and the canonical NXML component/style bundle without any Host service or
window backend dependency, in managed and NativeAOT execution.

## Migration status

This API is the first production boundary for moving plugin business adapters out of Desktop. Existing in-process plugin integration remains during migration. Do not interpret the generic route count as a claim that the plugin UI and all 358 SDK operations have already moved to the worker. The SDK Beta release remains blocked until that cutover and the worker packaging checks are complete.

## Page control (shared packages 0.1.0-alpha.9)

`SidecarPageControlClient` discovers catalog **1279** independently of presentation
and view state. Use it after publishing NXML ordinary pages. The wire schema is
`46D5C780DCD36136E0F3B0EE1E528E820A7FEBA2F80268C114764FDBA3AE59D4`.

| ID | Route | Request | Result |
| --- | --- | --- | --- |
| 1280 | `presentation.pages.state` | `HostPageQuery(ExpectedRevision)` | `HostPageState(Revision, Pages)` |
| 1281 | `presentation.pages.open` | `HostPageChange(ExpectedRevision, Document)` | Completion |
| 1282 | `presentation.pages.close` | `HostPageChange(ExpectedRevision, Document)` | Completion |

`Document` is the zero-based index in your own last committed source set, including
non-page documents in the indexing. `Pages` contains only ordinary pages in that
set, each with its document index, target and actual `Visible` value. Targets are
informational: requests cannot address another session by target. Onboarding pages
are excluded. An empty source set produces an empty result.

```csharp
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Host.PageControl.Data;

var pages = new SidecarPageControlClient(requests, observer);
var discovery = await pages.DiscoverAsync(token);
if (!discovery.IsSuccess) return; // This Host does not offer this exact catalog.
pages.Commands.TryResolve(SidecarPageControlRoutes.Open, out var open);
var opened = await pages.Commands.Dispatch(open,
    new HostPageChange(expectedPresentationRevision, documentIndex),
    cancellationToken: token).Completion;
```

Read the current revision through the presentation or view-state catalog and
serialize your own source/model writes with page requests. Every committed source
or model update advances that revision. A queued request is checked again on the
UI thread; an intervening update rejects it instead of applying it to a new page.
After a conflict, reread the revision and reconcile your intended source set before
retrying. Source replacement retires old identities; model-only updates preserve
navigation and input drafts. Opening an already visible page and closing a hidden
page are harmless. Closing a visible page restores its live return target; it
cannot close another session's page. Host onboarding prevents programmatic page
navigation until it completes.

Calls complete after a UI frame processes them. Do not block the receive loop that
must deliver their replies. A detached UI, invalid index, non-page document, stale
revision, unavailable mounted page, exhausted queue or missing return target yields
`xsr.rejected`. Cancelled requests and retired sessions/presentations yield
`xsr.cancelled`. A request waiting five seconds without execution yields
`xsr.timed_out`; it cannot execute on a later frame. At most eight requests per
session and 64 requests globally can wait. Cancellation frees this capacity even
when rendering is paused. Unauthorized or unavailable sessions have no routes.

The client and catalog are general Sidecar APIs. Guest page ownership, guest
permissions, action-token translation and guest visibility events remain the
responsibility of PluginSidecar. This catalog does not complete the legacy plugin
controller migration or change the frozen guest ABI.

## Confirmation (shared packages 0.1.0-alpha.10)

`SidecarConfirmationClient` discovers catalog **1535** and invokes query **1536**,
`interaction.confirmation.ask`. Its independent wire schema is
`05C1E5C77E48F23A3210C86227090656C5E3F3D2773AB2BD2981FB41132C7E36`.
The request is `HostConfirmationRequest(Title, Utf8Details)`; the response is
`HostConfirmationDecision(Accepted)`.

```csharp
using System.Text;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Host.Confirmation.Data;

var confirmations = new SidecarConfirmationClient(requests, observer);
if (!(await confirmations.DiscoverAsync(token)).IsSuccess) return;
confirmations.Queries.TryResolve(SidecarConfirmationRoutes.Ask, out var ask);
var result = await confirmations.Queries.QueryAsync<HostConfirmationRequest, HostConfirmationDecision>(
    ask, new("Replace saved configuration", Encoding.UTF8.GetBytes(details)), cancellationToken: token);
if (!result.IsSuccess || !result.Value.Accepted) return;
// Revalidate your own operation's identity and revision before executing it.
```

The Host supplies the buttons and displays the authenticated Sidecar package name
in the modal title. `Title` and details appear as literal body text: they cannot
select translation keys, introduce markup or replace Host controls. The title
must contain 1–256 characters without control characters. Details must be valid
UTF-8, nonblank and no larger than 256 KiB; line breaks and tabs are permitted,
other control characters are rejected. A UTF-8 byte array avoids the generic
string codec's 32 KiB limit. Large requests require the negotiated large-DTO
feature under the ordinary transport rules.

The request cannot displace any existing Host or Sidecar modal. Busy or unattached
UI, invalid text and stale authorization return errors. Explicit decline is a
successful response with `Accepted = false`. Cancellation, session retirement
and UI detachment dismiss the exact dialog; the two-minute deadline returns
`xsr.timed_out`. A stale button cannot approve a later request. A caller must keep
its receive loop running while waiting for the decision.

This API asks for a decision only. It neither executes a business operation nor
grants a reusable permission. Sidecar remains responsible for operation policy,
input binding, revalidation and execution. Never treat a failed query as consent,
and do not retry an uncertain effect merely because a confirmation reply was lost.

## Shell catalog

`SidecarShellClient` discovers contract `1791` before using the three independent
operations below. It is available in shared packages `0.1.0-alpha.11` and later.
The existing business, effects and presentation schemas retain their identities.

| ID | Operation | Input | Successful result |
| --- | --- | --- | --- |
| 1792 | `shell.open_https` | `HostHttpsRequest.Address` | Native browser handoff completed |
| 1793 | `shell.reveal_directory` | `HostDirectoryRequest.Path` | Native directory handoff completed |
| 1794 | `shell.displays` | `HostShellEmpty` | `HostDisplays.Items` |

```csharp
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Host.Shell.Data;

var shell = new SidecarShellClient(requests, observer);
if (!(await shell.DiscoverAsync(token)).IsSuccess) return;
shell.Commands.TryResolve(SidecarShellRoutes.OpenHttps, out var open);
var result = await shell.Commands.Dispatch(open,
    new HostHttpsRequest("https://example.com/help"), cancellationToken: token).Completion;
if (!result.IsSuccess) return;
```

The address must be absolute HTTPS, have no user information or control characters,
and contain at most 16,384 characters. Directory paths must be absolute, exist as
local directories, contain no NUL and fit 32,768 characters. Relative paths and
file URLs are rejected. Ordinary directory links remain usable. All strings also
follow the shared codec’s 32 KiB UTF-8 byte limit. These operations
hand work to the native shell; success does not prove that a remote website loaded
or that the file manager remained open.

Display results contain at most 32 entries. IDs are nonblank, unique ordinal
strings of at most 256 characters without control characters. Width and height
are positive pixel counts; scale is finite and positive. An empty array is valid.
The Sidecar must not treat these session-local IDs as durable monitor identities.

An unattached UI returns `xsr.unavailable`; invalid input or invalid platform output
returns `xsr.rejected`. A detached binding cancels pending requests. Replacement UI
bindings cannot complete work for the previous binding. The Host checks the
current authenticated session before executing and before accepting results.
Keep the receive loop active while awaiting calls. Do not retry an uncertain
browser or directory handoff automatically. Guest permissions, confirmation and
file-handle ownership are enforced by the Sidecar before invoking this catalog.

This catalog covers shell handoff and display enumeration only. File selection,
clipboard images and theme mutation require their own contracts and lifetimes.

## Theme configuration catalog

`SidecarThemeOptionsClient` discovers contract `2047` before sending configuration
mutations. It uses the same authenticated request transport as the other catalogs,
but has its own schema fingerprint and operation availability. Package consumers
need shared contracts `0.1.0-alpha.12` or later.

| ID | Route | Request | Successful completion |
| --- | --- | --- | --- |
| 2048 | `themes.options.save` | `ThemeOptionsSaveCommand` | Values validated and applied through the theme owner |
| 2049 | `themes.options.reset` | `ThemeOptionsResetCommand` | Declared defaults applied through the theme owner |
| 2050 | `themes.options.import` | `HostThemeOptionsImport` | Imported identity, schema and values validated and applied |

Read the general business catalog's theme catalogue and current snapshot first.
Use the catalogue's `SourceIdentity` and the current snapshot's `Revision` in the
mutation. This revision prevents an older UI or concurrently running Sidecar from
overwriting a more recent theme configuration. Refresh the snapshot after success;
do not predict the new revision or automatically retry a failed mutation.

```csharp
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Host.ThemeOptions.Data;

var options = new SidecarThemeOptionsClient(requests, observer);
var discovery = await options.DiscoverAsync(token);
if (!discovery.IsSuccess) return;
if (!options.Commands.TryResolve(SidecarThemeOptionsRoutes.Save, out var save)) return;
var values = new Dictionary<string, ThemeOptionValue>
{
    ["music.enabled"] = new(ThemeOptionKind.Boolean, "true")
};
var result = await options.Commands.Dispatch(save,
    new ThemeOptionsSaveCommand(sourceIdentity, values, currentRevision),
    cancellationToken: token).Completion;
```

The example assumes the selected theme declares a Boolean option named
`music.enabled`. Option IDs and kinds come from that theme's manifest, not a global
list. Save applies supplied overrides; reset restores all declared defaults. The
Host validates each value against the actual prepared manifest and uses its
existing appearance/resource preparation and persistence flow. An import carries
`ThemeId`, `SchemaVersion` and typed `Values`; the Sidecar reads and parses an
NTP-options file locally. Sending a JSON string as a command is not supported.
The Host independently compares the imported header to the prepared candidate,
and releases that candidate without activation if either field differs.

Requests allow at most 256 options, IDs of 1–128 characters, scalar strings of at
most 4096 characters, and at most 256 paths per `Files` value. `Files` requires an
empty scalar and a non-null list; other kinds require a null list. Null characters
are forbidden; only `Text` permits other control characters. These transport bounds
do not replace manifest-specific value, path or resource admission checks.

A stale revision, unknown option, wrong kind, schema mismatch, or invalid resource
is rejected by the theme owner. Safe mode returns `themes.safe_mode`. An absent UI
binding returns `xsr.unavailable`; cancellation or detachment cancels outstanding
work. Replies preserve the owner's error kind and code. A lost acknowledgement
does not establish whether the operation committed: reread current state before
asking the user to submit another mutation. Neither discovery nor successful
configuration grants a preview lease. Activation and session-owned preview cleanup
use the separate catalog below. The production plugin controller has not yet
moved to the worker.

The contract tests exercise the generated client against a real Desktop theme
session, including save/reset/import, revision conflicts, mismatch cleanup,
safe mode and binding retirement. Private Sidecar tests independently decode the
frozen binary layout and verify local document rejection before IPC.

## Theme activation catalog

`SidecarThemeActivationClient` is available in shared packages
`0.1.0-alpha.13`. Discover contract `2303` before using these commands:

| ID | Route | Request | Meaning |
| --- | --- | --- | --- |
| 2304 | `themes.activation.commit` | `HostThemeActivation` | Prepare and persist the selected theme |
| 2305 | `themes.activation.preview` | `HostThemePreview` | Temporarily display a theme under the caller's lease |
| 2306 | `themes.activation.end_preview` | `HostThemePreviewEnd` | Retire only the caller's named preview |

An activation contains the catalogue's `SourceIdentity` and `SourceRevision`,
and the current snapshot's `ExpectedRevision`. The Host checks them before
preparation and rechecks the actual source. Existing resource admission, downgrade
confirmation and safe-mode rules remain in force. A committed selection survives
Sidecar exit. A lost commit reply therefore requires a state refresh, not an
automatic activation retry or rollback.

For a preview, allocate a positive `LeaseId` before sending the command. IDs must
strictly increase for the whole Sidecar activation, including across UI rebindings
and client instances. Reserve IDs centrally if multiple guests or components share
one transport. Concurrent requests can reach the Host out of order; an older ID
that arrives after a newer one is rejected. This is intentional stale-request
rejection, not an invitation to resend automatically. No native UI generation is
exposed through the wire.

End the same ID on guest retirement and after a failed or uncertain preview
result, using an independent cancellation budget. Ending an unknown or already
retired ID succeeds without affecting another preview. It also advances that
caller's ID watermark: an end that arrives before its delayed preview prevents the
preview from appearing later. One Sidecar cannot end another Sidecar's lease.
A deactivation cancels all previews belonging to that activation; reactivation
creates a new lifetime and ID scope. Replacing ordinary NXML pages does not end
previews.

The Host reserves ownership before invoking Desktop. Request cancellation,
session retirement and UI detachment cancel pending native work and retire the
lease. If activation completes late, cleanup waits for its result and ends only
that native preview identity. A later manual selection remains untouched. If a
user action interrupts restoration, Desktop waits behind it and rechecks the
lease; failure of that user action cannot strand the old preview. If the committed
source can no longer be restored, Desktop falls back to the built-in
theme. Cleanup runs with its own five-second budget and reports failures. Shutdown
bounds draining; Desktop disposes its native resources when the window is gone.

The API admits at most 64 outstanding native operations and 128 active session
scopes. Cancelled calls continue consuming operation capacity until their native
callbacks and compensation finish, so cancellation cannot create unbounded
background work. Reaching the operation limit returns `xsr.backpressure`. An
absent UI rejects activation with `xsr.unavailable`, but end requests can still
record retirement. Source identities and revisions each allow at most 4096
characters, exclude null characters and must be non-empty.

Tests cover real Desktop activation and restoration, manual-selection protection,
source corruption fallback, foreign-session isolation, NXML replacement,
deactivate/reactivate, UI rebinds, late native completion, end-before-activation
and cancellation capacity. The Sidecar adapter is tested against an independently
encoded binary fixture, including lost acknowledgements and failed compensation.
This contract does not switch the production worker or change guest ABI 100.

## Notifications catalog

Shared packages `0.1.0-alpha.14` provide `SidecarNotificationsClient`. Discover
catalog `2559`, then call command `2560`, `interaction.notifications.publish`, with
`HostNotification(Level, Title, Body)`. Levels are `Info = 1`, `Warning = 2` and
`Error = 3`; other values are rejected. Title and body are literal strings, limited
to 4096 and 8192 UTF-16 code units respectively. Empty strings are allowed. Control
characters other than newline, carriage return and tab are rejected. The generated
wire also requires valid UTF-8 and bounded frames.

Desktop adds the authenticated Sidecar package name above the title and body.
Text is not interpreted as NXML, a localization key or a Host state binding. The
request cannot carry a callback or native action. Guest-specific attribution and
permissions belong to the Sidecar, so the Host never receives a plugin identity
or SDK notification object.

Success means the Host feedback service accepted the request. Rendering happens
later. The existing feedback owner controls expiry (Info: five seconds, Warning:
fifteen seconds, Error: manual dismissal), deduplication and its 200-entry limit.
Neither a transport acknowledgement nor screen visibility promises persistence.
There is no automatic retry: if a reply is lost after admission, retrying could
refresh or duplicate an already visible notification.

An absent binding returns `xsr.unavailable`. A well-formed request with invalid
text, or a failed local submission, returns `xsr.rejected`. Malformed binary fields,
including unknown enum values, are rejected by the generated decoder before
submission. Calls use the authenticated activation lifetime
and current UI binding; cancellation or retirement can return `xsr.cancelled`.
Once feedback has accepted a notification, subsequent cancellation does not undo
it. This preserves the same uncertain-outcome rule as other acknowledged effects.
The local binding callback must only submit to thread-safe feedback, never block
waiting for a UI thread or another IPC request.

Host tests exercise the generated binary client, source attribution, literal
rendering, all three levels, existing deduplication, validation, unavailable UI,
reentrant detachment and session retirement. Independent Sidecar tests decode a
frozen wire fixture and invoke the real guest notification route, including
permission denial, idempotency, pending acknowledgement, cancellation and lost
replies. Worker application composition remains a separate migration step.

## File picker catalog

Shared packages `0.1.0-alpha.15` provide `SidecarFilePickerClient`. Discover catalog
`2815` before calling query `2816`, `interaction.filepicker.pick`. The Host checks
the authenticated Sidecar activation and whether its native UI binding is present.
No plugin manifest, permission or guest handle crosses this boundary.

| Field | Meaning and limits |
| --- | --- |
| `HostFilePick.Kind` | `OpenFile = 0`, `Directory = 1`, or `SaveFile = 2`; selects one local item. |
| `Title` | Literal title, at most 256 UTF-16 code units, without control characters. Desktop prepends the verified Sidecar package name. |
| `Extensions` | Zero to 32 filters, each 1–32 ASCII letters, digits or dots; a filter cannot consist only of dots. An empty list means all files. `zip`, `.zip` and `tar.gz` are valid. Directory selection ignores file filters. |
| `HostFileSelection.Utf8Path` | Null if the user dismisses the dialog or the provider cannot supply a local path; otherwise strict UTF-8 bytes for an absolute path of at most 32768 UTF-16 code units. |

Paths use a bounded byte field because a valid Unicode path can exceed the general
codec's 32 KiB string limit. Do not interpret null as an empty path, decode malformed
UTF-8 with replacement characters, or assume a save destination already exists.
The Host does not normalize a chosen path into a guest grant; that is the caller's
responsibility after validating its own owner and cancellation state.

```csharp
var picker = new SidecarFilePickerClient(requests, observer);
var discovery = await picker.DiscoverAsync(cancellation);
if (!discovery.IsSuccess) return;
if (!picker.Queries.TryResolve(SidecarFilePickerRoutes.Pick, out var route)) return;
var result = await picker.Queries.QueryAsync<HostFilePick, HostFileSelection>(route,
    new(HostFilePickKind.OpenFile, "Select an archive", ["zip"]),
    cancellationToken: cancellation);
cancellation.ThrowIfCancellationRequested();
if (result.IsSuccess && result.Value.Utf8Path is { } bytes)
{
    string selectedPath = new System.Text.UTF8Encoding(false, true).GetString(bytes);
    // Validate the current owner before recording access to selectedPath.
}
```

Use `Nexa.Sidecar.Host`, `Nexa.Sidecar.Host.FilePicker.Data` and `Nexa.Xsr` for the
client, DTOs and router extensions. The connection owner must keep receiving replies
while this query awaits the native dialog.

The Host permits one outstanding Sidecar picker across all sessions. A concurrent
request receives `xsr.backpressure`; there is no queue. Missing UI returns
`xsr.unavailable`; invalid requests or paths are rejected. Native failures return
`xsr.unavailable`. A null successful result means no local selection, not a failure
of the transport. A client must not reopen the dialog automatically after an error
or an uncertain response.

The Host cancels a request after two minutes, on session deactivation, on UI-binding
retirement or when the peer cancels it. Cancellation need not close the OS dialog.
Its admission slot remains occupied until native completion, including across a
UI rebind, and the Host discards any late selection. Callers must recheck their own
cancellation before consuming the reply. The Sidecar business adapter maps null to
guest `CANCELLED` and creates an owner-scoped file grant only after this final check.

Validation covers generated binary discovery, all three selection kinds, path
encoding above 32 KiB, malformed inputs, prompt cancellation, a noncooperative
native callback, a late native exception, UI rebinding and session retirement.
Production worker composition is still pending; this catalog alone does not switch
plugin execution out of the launcher.

## Clipboard snapshots and staged writes

`SidecarClipboardClient` requires `LargeGeneratedDtos` and authenticated discovery
`3071`. It adds six operations, without changing the existing catalogs or guest
ABI 100:

| ID | Operation | Request → result |
| --- | --- | --- |
| 3072 | `interaction.clipboard.read` | `HostClipboardTransfer` → `HostClipboardSnapshot` |
| 3073 | `interaction.clipboard.read_chunk` | `HostClipboardReadChunk` → `HostClipboardChunk` |
| 3074 | `interaction.clipboard.begin_write` | `HostClipboardWrite` → acknowledgement |
| 3075 | `interaction.clipboard.append` | `HostClipboardAppend` → acknowledgement |
| 3076 | `interaction.clipboard.commit` | `HostClipboardTransfer` → acknowledgement |
| 3077 | `interaction.clipboard.release` | `HostClipboardTransfer` → acknowledgement |

Allocate a positive, increasing `TransferId` **before** sending a read or begin-write
request. Serialize those begin requests on a connection. Every admitted begin ID
is consumed, including a request rejected for capacity or unavailable UI. Release
with that same ID in a `finally` block, using a separate cleanup deadline. Releasing
an unknown ID also consumes it: a delayed begin cannot recreate an abandoned
transfer. These fences belong to the Sidecar activation and survive UI rebinding.
IDs cannot be shared across sessions or reused within an activation.

A read captures text and PNG bytes once. `TextUtf8` is strict UTF-8, bounded to
65,536 UTF-16 code units after decoding. `ImageLength = -1` and a null
`ImageSha256` mean no image; otherwise the length is 33 bytes through 16 MiB and
the digest is exactly 32 bytes. Read image chunks by offset, each at most 256 KiB;
validate the returned offset, length and final SHA-256 before consuming the image.
Chunks refer to the immutable snapshot, even if another application subsequently
changes the clipboard. The caller must release text-only snapshots too.

For a write, send text and the image descriptor in `begin_write`, then append
nonempty image chunks at exactly the next expected offset. Commit only after all
bytes have been acknowledged. The Host checks completeness, digest, PNG signature,
IHDR length, dimensions (1–4096) and pixel count (at most 4 × 1024 × 1024).
The native owner decodes the full PNG before modifying the OS clipboard. A null
text differs from `""`; null text with no image clears the clipboard.

The Host admits at most eight transfers overall, two per activation, with a 64 MiB
reservation budget and one outstanding native clipboard call. Each transfer expires
after two minutes without requiring another request. This is a transfer-storage
budget, not a bound on process RSS: codecs and the native image decoder also need
bounded temporary buffers. Cancellation, UI detachment and session retirement
invalidate transfers immediately; unfinished native calls retain their slot and
reservation until they actually return.

Missing native UI yields `xsr.unavailable`; exhausted capacity yields
`xsr.backpressure`; invalid descriptors, stale IDs, incomplete writes and digest
mismatches yield `xsr.rejected`. Native failures yield `xsr.unavailable` and
cancellation yields `xsr.cancelled`. A failed or missing commit acknowledgement does
**not** prove the clipboard is unchanged. Do not replay begin, append or commit
automatically, and do not restore an earlier clipboard snapshot: another
application may already have replaced the content. Release is safe to repeat.

Plugin confirmation, permissions and opaque blob ownership belong to PluginSidecar.
This Host API knows only the authenticated Sidecar session. The adapter checks guest
cancellation again after IPC, releases transfers with an independent one-second
budget and reports cleanup failures; Host expiry is the final cleanup bound.
Production worker composition remains a separate migration step.


## Runtime context and startup readiness

Discover `SidecarContextClient` using catalog query **3327**, then read
`SidecarContextRoutes.Snapshot` (**3328**, `HostContextQuery` →
`HostContextSnapshot`). Discovery checks the exact schema and catalog fingerprint.
The snapshot operation requires an active authenticated session and negotiated
`LargeGeneratedDtos`; discovery remains available without that feature and
advertises an empty operation set. Never infer readiness from successful discovery.

The reply contains:

| Field | Meaning |
| --- | --- |
| `Version` | The launcher's informational version, at most 256 UTF-16 code units. |
| `Utf8DataRoot` | Absolute launcher data root encoded as strict UTF-8, at most 131072 bytes. Decode with invalid-byte rejection. This is a native Sidecar fact, not guest filesystem authorization. |
| `Phase` | `Composing` (0), `Ready` (1), or terminal `Stopping` (2). Ready follows normal startup preparation; headless validation stays Composing. |
| `NativeWindowReady` | True only while the native window has opened, hidden startup preparation has completed, and the Host remains Ready. Tray hiding does not make the window unavailable. |

Use Ready for restoration that needs business routes. Also require
NativeWindowReady for work requiring an actual native owner. Poll with bounded,
cancellable backoff, and stop on activation retirement or Stopping. Complete the
Sidecar activation handshake before starting this wait: Host startup itself waits
for admitted Sidecars, so awaiting readiness inside activation would deadlock.
Keep the restoration task supervised and surface startup errors.

A successful snapshot neither reserves a window nor guarantees that a later
operation will succeed. Continue to handle cancellation and unavailable replies.
The Host never creates plugin storage directories or selects plugin recovery
policy. The production Worker still uses its previous restoration owner during
this incremental migration; adding the Context route does not start a second one.


## Modal documents (shared packages 0.1.0-alpha.18)

Negotiate `Nxml` and `Dialogs` (`1024`) before publishing a
`SidecarNxmlOperation.Dialog` (`9`). The source bundle still uses schema 3. Its
root must be a `Page` whose key matches the single-key target. Standalone targets
must be unique within a session. Modal documents are excluded from ordinary page
navigation, onboarding and the PageControl catalog.

Discover `SidecarDialogControlClient` with query **3583**. The independent wire
schema is `A71C4011DE44E975B9AC4739B69972D33A92781A74E3E20D9B02BB9112AC2409`;
the catalog fingerprint is
`1096DA93777680CBF63BA34AE34B273B8438A361C0581B55093234B34313CD31`.

| ID | Route | Request | Result |
| --- | --- | --- | --- |
| 3584 | `presentation.dialogs.state` | `HostDialogQuery(ExpectedRevision)` | `HostDialogState(Revision, Dialogs)` |
| 3585 | `presentation.dialogs.open` | `HostDialogChange(ExpectedRevision, Document)` | Completion |
| 3586 | `presentation.dialogs.close` | `HostDialogChange(ExpectedRevision, Document)` | Completion |

`Document` is the zero-based index in the caller's complete committed source set,
including non-modal documents. A state entry contains that index, the source
`Target` and whether it is currently `Open`. Targets are informational; they do not
let a caller select another session. A new modal document starts closed.

```csharp
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Host.DialogControl.Data;

var dialogs = new SidecarDialogControlClient(requests, observer);
if (!(await dialogs.DiscoverAsync(token)).IsSuccess) return;
dialogs.Commands.TryResolve(SidecarDialogControlRoutes.Open, out var open);
var result = await dialogs.Commands.Dispatch(open,
    new HostDialogChange(expectedPresentationRevision, documentIndex),
    cancellationToken: token).Completion;
```

The Host validates revision and ownership at admission and again on the UI thread.
It admits at most 64 queued modal requests globally and eight per session, with a
five-second deadline. At most 16 modal documents can be open globally and eight
per session. An ordinary-page index, stale revision, missing capability, retired
session, full queue or unavailable UI fails without opening a different document.
Opening an already open modal and closing an already closed modal are idempotent
within the current revision. An uncertain reply must be reconciled by querying
state; do not automatically replay an open command.

The Host supplies the outer frame and close button, displaying the authenticated
Sidecar package name as the source. A Sidecar modal cannot open over Host-owned
modal UI or onboarding. Host confirmations can appear above existing Sidecar
modals, and only the top modal layer accepts input. Form fields remain scoped to
the invoking exposure. Closing a modal cancels its in-flight UI submissions, including submissions from
components mounted inside it. Reopening starts a new submission lifetime while
preserving input drafts.

Model-only changes retain the modal and its input drafts. Replacing sources
retires old identities and closes their modals, even if a replacement uses the
same target. Session retirement and UI detach also close owned windows and cancel
queued requests. Native closure is visible in the state query; the Sidecar decides
whether that closes an application-level dialog or merely hides a view. No guest
SDK, plugin identity or plugin-specific lifecycle policy is part of this catalog.


## Retaining documents (shared packages 0.1.0-alpha.19)

Use reconciliation when one document changes but other documents in the same
Sidecar activation must remain visible. For example, publishing a second modal
must not close the first modal or discard its input. The Host retains only the
indices explicitly selected by the authenticated caller. It does not recognize
guest plugin IDs or infer application lifecycle policy.

Negotiate `Nxml` and `RetainedDocuments` (`2048`), then discover
`SidecarViewReconcileClient` with query **3839**. Its independent schema is
`D68929EB52E7BB891A4B1783601A41B6B9A120E09C940989CCAC7D333D169715`;
its catalog fingerprint is
`CF18AEE13178DE6491BC4112DF6AD5226D2C1D5C8782BFF633A4FB7FE36C6A31`.
Existing ViewState and DialogControl catalogs are unchanged.

Command **3840**, `presentation.viewstate.reconcile`, accepts
`HostViewReconcileRequest(ExpectedRevision, Documents)` and returns completion.
The request describes the **entire next source set**, in its desired order.
Each `HostViewCandidate` has these fields:

| Field | Meaning |
| --- | --- |
| `PreviousDocument` | A unique zero-based index in this session's current source set, or `-1` to add a new document. It is not an index in the candidate set. |
| `Source` | Empty bytes when retaining an index; otherwise a complete encoded `SidecarNxmlDocument`. A retained source cannot be replaced in place. |
| `Model` | Complete document-local model, expressed as `HostViewCandidateValue(Key, Utf8Text)` entries. Omitted keys disappear; this is not a patch. |

For example, keep previous document 2 as the new first document, drop all other
old documents, and append a newly compiled source:

```csharp
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Host.ViewReconcile.Data;
using Nexa.Sidecar.Protocol;
using System.Text;

var views = new SidecarViewReconcileClient(requests, observer);
if (!(await views.DiscoverAsync(token)).IsSuccess) return;
if (!views.Commands.TryResolve(SidecarViewReconcileRoutes.Reconcile, out var reconcile)) return;
var request = new HostViewReconcileRequest(expectedRevision,
[
    new HostViewCandidate(2, [],
        [new HostViewCandidateValue("caption", Encoding.UTF8.GetBytes("Updated"))]),
    new HostViewCandidate(-1, SidecarNxmlDocument.Encode(newSource), [])
]);
var result = await views.Commands.Dispatch(reconcile, request,
    cancellationToken: token).Completion;
// Advance the locally owned revision only after a successful completion.
```

Admission checks the active session, negotiated features and exact expected
revision. Duplicate or missing previous indices, a retired identity, conflicting
standalone targets, invalid UTF-8, duplicate model keys and compilation failures
reject the complete candidate set. An empty set retires every document. Limits
remain 32 sources and 4 MiB of encoded source per session, 128 sources globally,
4096 model fields per document, 512 KiB per value and 4 MiB of model keys/values
in total. Keys must be nonblank, at most 256 UTF-16 code units and contain no
control characters. Retained source bytes still count toward the source budget.

A successful commit increments the presentation revision once. Retained sources
keep their exposure identity, input drafts, navigation/modal state and pending
UI actions even if their indices change. Their models are recompiled before the
commit. Removed documents cancel pending actions and close owned windows. A
retained modal that the user already closed remains closed. The new source set
never implies an open request; use the separately discovered DialogControl API.

Queued page/modal controls captured against the old source set are cancelled,
including when all sources are retained. Issue subsequent controls using the new
revision and new indices. Existing `ViewState.Replace` deliberately keeps its
whole-set retirement semantics. Session retirement, invalid presentation and UI
detach remain retirement boundaries; a retired exposure cannot be resurrected.

Serialize presentation mutations in the Sidecar. Cancellation observed before commit
leaves the old set intact, but a lost reply can hide a completed commit. Do not
blindly replay the request or reopen modals based on a pre-update snapshot.
Read the current ViewState revision and reconcile the acknowledged source set;
if ownership cannot be established, seal action admission and recover explicitly
with fresh sources. A fresh whole-set replacement may close windows and must not
be presented as successful retention.

## ShortcutControl: document-scoped keyboard callbacks

Shared packages `0.1.0-alpha.20` add `SidecarShortcutControlClient`. Discovery is
query **4095**; `presentation.shortcuts.replace` is command **4096**. It requires
an authenticated active session with NXML, binary payloads and UiForms. Discovery
checks the independent wire/catalog fingerprints before enabling the command.
It does not change any earlier catalog or assign a guest SDK operation number.

```csharp
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Host.ShortcutControl.Data;

var shortcuts = new SidecarShortcutControlClient(requests, observer);
if (!(await shortcuts.DiscoverAsync(token)).IsSuccess) return;
if (!shortcuts.Commands.TryResolve(SidecarShortcutControlRoutes.Replace, out var replace)) return;
var result = await shortcuts.Commands.Dispatch(replace, new HostShortcutSet(revision,
[
    new HostShortcutBinding(documentIndex, "Ctrl+K", "example.sidecar.action", "open-panel")
]), cancellationToken: token).Completion;
```

`revision` is the current presentation revision. This command does not increment
it. `documentIndex` addresses only the caller's current ordinary page or mounted
source; modal and onboarding documents are rejected. The command must be a
GeneratedDto command registered by that same session. It need not be declared by
a button in the document. The callback is an existing `SidecarUiForms` envelope:
`Argument` contains the supplied opaque value and `Fields` is empty. Ordinary
markup dispatch keeps its separate requirement for a markup command declaration.

The request replaces the caller's complete set. An empty set clears it. A rejected
candidate leaves existing bindings intact. Limits are 64 bindings per session,
512 per UI attachment, 64 UTF-16 code units per gesture, 32768 per argument and
256 KiB of aggregate UTF-8 argument bytes. Blank gestures, control characters,
duplicate gestures for one document, foreign or missing documents, scalar/Host
commands and stale revisions are rejected. Matching trims surrounding whitespace
and uses ordinal case-insensitive platform gesture text; modifier aliases and
order are not normalized. Use the platform's spelling, for example `Ctrl+K`.

Host keyboard handling takes precedence. Callbacks require visible content under
the current ordinary page; onboarding, any modal layer and focused text input
suppress them. Conflicts resolve by Sidecar package name, session ID and binding
order. A running binding consumes repeats without starting another call. At most
16 callbacks run concurrently per UI attachment, each with the existing ten-second
action deadline. This is application-window input, not an OS-wide hotkey service.

Installed bindings use document identities. Model updates and retained document
reordering preserve them; identical binding replacements also preserve their
in-flight guard. Removing a binding cancels its pending callback. Replacing or
removing its source, retiring its session, or detaching the UI cancels callbacks
and retires the corresponding bindings. New sources require a new binding set.
Queued requests are bounded to 64 globally and 8 per session, expire after five
seconds, and revalidate their captured revision before applying on the UI thread.

A lost acknowledgement leaves the mutation outcome unknown. Serialize binding
updates with presentation updates and seal application action admission while
recovering. The generic client never replays a callback. Guest registration IDs,
permissions, scopes, action inputs and lifecycle checks remain in the Sidecar.

## Raster resources and scoped input

`SidecarRasterControlClient` discovers an independent binary catalog at **4351**.
Its five commands are `presentation.rasters.begin` (4352), `append` (4353), `seal`
(4354), `replace` (4355) and `release` (4356). Discovery verifies the schema and
catalog fingerprints before advertising session-authorized operations. Callers
must be active, admitted native Sidecars with NXML, binary payloads and UI forms.
The UI must be attached before an upload can start.

Allocate a positive resource ID within the current session. Begin with the exact
encoded length and SHA-256 digest, then append sequential chunks of 1–65,536 bytes.
Seal after all bytes arrive. Sealing checks the digest, bounded PNG metadata and
successful complete decoding. Decode concurrency is limited to two per attachment;
validation runs outside the UI frame. A failed seal retires the upload. Do not
append to or seal an already sealed image. Release unused IDs explicitly; releasing
an unknown ID is harmless, while releasing a bound image is rejected.

Limits include 16 MiB per PNG, 16 resources/64 MiB per session, and 64 resources/
128 MiB per attachment. Pending and unbound ready resources expire after 30 seconds;
accepted chunks refresh that deadline. Bound resources remain live until unbound
or their session/UI retires. Expiry does not require an active rendering frame.
After expiry or an uncertain upload result, use a fresh resource ID.

Replace publishes the **complete** raster set at `ExpectedRevision`. Each binding
contains an owned document index, exact local Dynamic-slot key, ready image ID,
logical width and height, own registered generated-DTO command and opaque argument.
Widths and heights must be finite and within 1–2048; rounding them up must produce
the PNG dimensions. One set contains at most eight bindings and 8,388,608 pixels;
the attachment permits at most 64 bindings and 33,554,432 pixels. A slot appears at
most once in a set. Callback arguments are limited to 32,768 UTF-16 code units each
and 256 KiB UTF-8 in aggregate. Onboarding slots and foreign/scalar commands are
rejected.

A complete candidate is validated before the current set changes. An empty set
removes all bindings. The UI queue is bounded to 64 operations globally and eight
per session, with a five-second deadline. A stale revision, missing slot, missing
resource or failed validation leaves the old set in place. Model updates retain
installed nodes and source reconciliation retains surviving document identities.
A removed source cancels callbacks and removes its raster nodes. Keep input
admission sealed after an uncertain replacement until explicitly synchronizing the
complete set again; do not replay input events to recover an acknowledgement.

The native owner forwards observations only from visible, enabled owned raster
nodes in the current page or topmost modal. The callback uses `SidecarUiForms` with
the supplied opaque argument and exactly `kind`, `phase`, `x`, `y`, `text` and `gap`
fields. Kind is `Pointer`, `Key`, `Text` or `Focus`; phase is bounded to 64
characters; coordinates are finite invariant-culture numbers. Text is bounded to
4,096 UTF-16 code units without splitting a surrogate pair. `gap` is `true` when
intermediate observations were coalesced or text was truncated. No surrounding
form drafts are included. Each surface has one in-flight callback and one latest
pending observation. Dialog closure, source removal and attachment shutdown
cancel pending calls. The Host does not interpret plugin-specific callback tokens.

## Configured HTTP exchanges

The independent `Http` catalog uses discovery 4607 and operations 4608–4612
(`Begin`, `Append`, `Send`, `Read`, `Release`). It requires an active, exact
authenticated session with `HostRequests` and `BinaryPayloads`; UI features and
`LargeGeneratedDtos` are not required. Shared packages `0.1.0-alpha.22` expose
the typed binary client. Guest ABI 100 is unchanged.

`Begin` snapshots the method, absolute HTTP(S) URI and headers. Body length -1
means no content; 0 means an explicit empty body. Supported methods are GET,
HEAD, POST, PUT, PATCH, DELETE and OPTIONS. Fragments, user-info and control
characters in URIs are rejected. Content-Length and Transfer-Encoding belong
to the transport; callers must not supply them. Header names are unique without
case, at most 128; each has 1–64 values. Aggregate header text is at most 24 KiB
in UTF-8. Request bodies are at most 16 MiB.

`Append` requires the exact next upload offset and 1–61440 bytes. `Send` is
accepted once, after upload completion, and returns the status code and bounded
response headers. It uses the Host's configured client with automatic redirects
and cookies disabled. Proxy/PAC, DNS/IP preference and provider admission stay
in their existing Host owner. HTTP error status codes are ordinary responses.

`Read` requires the next response offset and a count of 1–61440. An empty chunk
means EOF. Responses are streamed, without a whole-response size allocation;
plugin-specific download limits remain in the Sidecar. A failed Send or Read
retires the exchange. Never retry an uncertain effect or sequential read.
`Release` is idempotent for a missing ID within the caller's own session,
cancels pending operations and acknowledges only after cleanup drains. IDs must
not be reused in a session.

Each session may reserve 32 exchanges and 64 MiB of upload buffers; the Host
allows 64 exchanges and 128 MiB in total. Retiring resources still consume their
reservations until pending work finishes. Two minutes without a completed
operation expires an exchange, including a stalled operation; a timer checks
expiry and session retirement independently of UI frames. Host shutdown applies
a five-second drain bound. Errors contain neither addresses, headers nor bodies.
