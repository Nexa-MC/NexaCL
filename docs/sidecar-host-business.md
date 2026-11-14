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
