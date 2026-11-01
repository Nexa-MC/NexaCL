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
