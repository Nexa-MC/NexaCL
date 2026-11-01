# Minecraft settings ports

The native Minecraft owner must not create another writable settings service or
persist settings behind the host's back. Foundation remains the canonical owner.
Launch preparation consumes an immutable, typed settings read snapshot. Each item
retains its semantic key, declared scalar type and schema-encoded current value,
including defaults. The existing public launch constructor continues to accept
SettingsService; an additive ISettingsValueReader adapter permits snapshot reads.

Effective policy queries and mutations use asynchronous numeric reverse ports.
Rename/recovery planning and commit checks stay with the canonical settings owner;
Minecraft keeps ownership of its file transactions and awaits those settings steps
at the existing durable boundaries. Preserve expected revisions, reverse/recovery
operations, temporary overrides and launch profiles. No synchronous settings RPC,
CLR service object or JSON message enters the native data plane.

Plans crossing this boundary need typed value records. JSON representations used
for existing on-disk journals remain disk compatibility formats, not transport
messages. Keep their documented encoding/validation algorithms in their current
owner, and preserve public managed APIs through adapters or additive overloads.

Acceptance includes typed/default/type-mismatch reads, settings changes between
launches, canonical persistence failures, stale revisions, rename/recovery rollback,
temporary-profile behavior, cancellation and physical shutdown.

## Java registrations

Java registrations remain persisted by the canonical host settings adapter. Native
inventory, manual acquisition and management use an additive asynchronous registry
port. Existing synchronous registration APIs stay available to managed callers;
their local adapter is immediate and does not implement synchronous IPC. Native
operations await a bounded typed registry snapshot and conditional commit. The
locator receives a local immutable registration view captured for its operation;
inventory and destructive management recheck the host revision before completion.
Manual acquisition retains its consumed preview, fingerprint, license confirmation,
revision admission and actual executable verification in the Minecraft owner.

## Preparation and pause

Effective policy and immutable scalar reads expose asynchronous interfaces while
the existing managed methods remain immediate local adapters. Installer and launch
consumers await those reads. An install waiting for its initial policy has no
durable work to retain: pause cancels that preparation query before waiting for a
journal-ready worker. Later journal creation/publication keeps its existing stop
ordering. Cancellation retirement covers both sources, including modpack preparation.

## Rename identity

Native rename planning returns source/destination and a canonical content identity
for the instance overrides and profiles. The host keeps their actual values: apply
and rollback move the currently matching source/destination content under the
existing revision check. The identity includes absent values and preserves unknown
settings/profile members, so no serialized JSON or lossy projection crosses the
native port. Canonical hashing sorts object members and preserves array order.

Existing rename journals containing full original settings remain readable. A
native identity-only journal uses a new schema version so older readers reject it
instead of treating absent inline values as an instruction to discard settings.
Current managed recovery understands both representations. Durable progress and
the file/settings rollback order remain unchanged.

Rename implementation validation: nine related service tests pass. The interrupted
journal test replays and reverses each of eight prefixes using both legacy inline
settings and an asynchronous identity-only port. New assertions verify schema 1/2
selection, unknown settings preservation, stale-content rejection, object ordering,
exact numeric identity and array ordering. Public managed APIs remain available;
internal rename/recovery paths now await the canonical transaction interface.
The independent NativeAOT caller also passes actual game-directory rename with
one host settings transaction, using 128 DTO shapes
(`3559B4C3D2B431814EB59362EC403EE88122EDDA7A27C2413310B9B4838268A9`).
It reads the native edit fingerprint, renames the installed client/manifest,
checks the old directory is gone and verifies the canonical transaction call.
Production native-service composition compiles without warnings/errors, and the
architecture gate passes for all 97 projects.

## Recovery baseline

Recovery snapshot capture and planning exchange typed setting rows (key, original
override and effective override) and typed before/after mutation plans. Existing
local JSON baseline/journal representations remain disk formats with explicit
conversion; they are not native messages. The canonical host validates the same
settings scope, selected keys, temporary-profile restrictions, current revision and
conditional persistence. File/settings compensation keeps its existing order and
awaits settings commit or rollback before recording the transaction outcome.

Recovery service validation: all 59 related recovery/rename tests pass after the
asynchronous adaptation. The successful-exit baseline and file/settings coordinator
tests additionally run through a delayed settings interface as well as the local
adapter; both pass. They cover stale revision compensation, successful commit,
startup replay and automatic baseline capture after a confirmed successful session.
Existing tests continue to preserve private arguments and inheritance, and reject
wrong scopes and incomplete baselines.
The independent NativeAOT caller passes the 142-shape protocol
(`093F28C23C90CD533CA816276FEFFB4B072EDE4778B4C6E18AA5E72EBC71BAA7`),
including recovery comparison without an invented baseline and clean startup
replay. Existing real installation, rename, Java and physical-shutdown scenarios
pass. Complete native successful-session capture remains part of full launch
ownership, rather than being inferred from the empty-baseline protocol case.
Production native-service composition compiles without warnings or errors after
the recovery adaptation.
The architecture gate passes for all 97 projects.
