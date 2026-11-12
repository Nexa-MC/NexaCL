# Sidecar Host business ABI

The Host exposes general launcher operations to publisher-verified native Sidecars. This boundary contains no plugin SDK types, plugin capability names, or guest identifiers. A Sidecar that executes untrusted guests must enforce its own admission and permissions before invoking Host operations.

## Connect and discover

Keep a single reader for the Sidecar connection. Negotiate `BinaryPayloads` and `HostRequests`, plus `LargeGeneratedDtos` for complete business DTO support, activate `SidecarHostRequestClient` with the negotiated features, and pass incoming command/query result frames to `TryAcceptReply`. Forward other frames to the existing peer state machine. Concurrent callers share the client; they must not each read the connection.

Construct `SidecarBusinessClient` with that transport client and an XSR dispatch observer. Call `DiscoverAsync` after activation. Discovery returns the wire version, DTO-and-operation-catalog fingerprint, and the numeric operations available to this session. During launcher composition, it reports unavailable; retry discovery with a bounded delay and cancellation. Do not retry a mutation to recover from connection failure.

The Desktop authorizes only the current active session of a publisher-verified package. A previous session identifier does not authorize a restarted process. The session must also remain active when dispatch starts. No package name receives a plugin-specific exception. Native Sidecars already execute with the current user's OS authority; this API is not an OS sandbox.

## Routes and data

`eng/sidecar/host-business.json` assigns explicit numeric IDs to 90 domain commands and queries. The reserved query 255 discovers capabilities; query 254 reads public state. Route names identify the underlying domains: accounts, library, game processes, install, content, settings, themes, and resources. A declared route is advertised only if its domain router actually supplies it.

`Nexa.Sidecar.Host.Data` contains independent client records. Generated codecs translate these records to the Host's domain DTOs using a bounded binary wire format. No CLR object crosses the process boundary. Values are bounded to 8 MiB. With `LargeGeneratedDtos`, the existing binary bytes field carries the first 65,535 bytes, field 6 declares total length, and fields 7 onward carry consecutive continuations. Without negotiation, large calls are rejected before execution and large query replies report feature unavailable. Legacy payload bytes and primitive limits are unchanged. Generated DTO `ToString()` returns only the type name, so logging a request cannot dump credential fields. Init-only fields without constructor parameters are required in the client so callers cannot accidentally substitute zero values for Host defaults. Regenerate both ends together before publishing a schema change; an incompatible fingerprint is rejected before business calls.

Public state consists of library, account display records, game sessions, installation catalogs, login progress, character choices, and the settings revision. Account credentials and private profiles are excluded. Each field has its own domain revision. Missing domains have revision -1 and null content. The aggregate is not a cross-domain transaction: commands must still validate their expected revisions. Login progress includes the user-visible authorization code and URL, not the provider polling secret or access token.

State polling is a snapshot mechanism, not a replay log. A consumer must refresh after a completed mutation before relying on the resulting state. Watcher adapters belong in the consuming Sidecar and must handle disappearing identities and revoked sessions.

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

Add `--check` to the codec command and `--verify` to the route command to verify without modifying files. Desktop tests exercise the independent codecs, authority revocation, unavailable state, UTF-8 paths, and preserved domain errors. Sidecar transport tests cover concurrency, cancellation, malformed replies, and backpressure. The package consumer verifies that the client works without launcher service assemblies, including NativeAOT.

## Migration status

This API is the first production boundary for moving plugin business adapters out of Desktop. Existing in-process plugin integration remains during migration. Do not interpret the generic route count as a claim that the plugin UI and all 358 SDK operations have already moved to the worker. The SDK Beta release remains blocked until that cutover and the worker packaging checks are complete.
