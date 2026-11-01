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
