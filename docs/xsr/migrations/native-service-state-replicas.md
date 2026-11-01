# Native service state replicas

Accounts and subsequent business modules own their service stores and credentials
inside their native images. The UI store retains canonical typed declarations for
renderer/controller compatibility, but these entries become immutable replicas at
builder time. Ordinary publication, coalescing, deltas, availability writes and
initial-snapshot cloning cannot overwrite a replica. Derived state remains local
and cannot itself be declared a replica.

A generated numeric service adapter decodes bounded binary values into local
objects and applies complete typed snapshots with the producer's revision and
availability. Revision zero is the unpublished state; positive revisions must be
strictly increasing. Delayed or duplicate frames cannot roll state backwards.
Collection snapshots preserve key identity, reject duplicates and apply their
canonical ordering. Reader-facing notifications use the source revision, including
for expected-roster-revision account commands; the store-global diagnostic stamp
remains local. No CLR objects, tasks, delegates, streams, exceptions, or JSON data
plane cross the native boundary.

A composition creates one module session for one UI store. A retry creates a new
composition and store, rather than silently restarting the producer revision
sequence under existing readers. Session shutdown rejects new work, cancels and
drains admitted operations and reverse callbacks, and only then retires callback
contexts. Native images stay loaded for process lifetime. Account HTTP requests
retain the current network policy through asynchronous reverse ports until the
network service owns those ports directly. Credentials remain confined to login,
storage and privileged launch-identity calls; replicated account state is the
existing credential-free account view, onboarding and skin contracts.

The first producer is `nexa-accounts`. It creates `AccountService`, encrypted file
storage, shared Microsoft/LittleSkin/Yggdrasil providers, onboarding, skin resolution,
wardrobe and catalog services within its own runtime. Production UI references only
`Nexa.Native.Accounts.Client`; the publisher rejects the concrete account assembly in
the UI closure. Managed development retains its original API and implementation.
Account errors and onboarding configuration now have their canonical contract owner,
with type forwarding from the former assembly. Privileged game launch reads use an
additive asynchronous account interface; existing synchronous managed implementations
remain source compatible through its default method.

A profile-store descriptor passes an absolute encrypted-store location at startup;
UI cannot call Load/Save on that descriptor. Explicit in-memory descriptors exist for
controlled validation and are never used for the live profile file. Base storage and
provider composition initialize asynchronously. A fixed route table and generated DTO
fingerprints are admitted before commands. Complete snapshot values and their revision
come from the same capture. The enclosing account lifetime owns both base storage and
provider workers, including previously replaced login and skin requests, and drains
reverse HTTP work before retiring callback registrations.

Publication uses a bounded worker with one latest snapshot per fixed state slot.
Source revisions prevent an older concurrent capture from replacing a newer pending
snapshot. Service locks never perform encoding or transport callbacks. Commands wait
for a publication barrier so successful completion observes their state or a newer
revision. Transport backpressure retries on the publication worker. The 68 MiB frame
budget preserves the existing 64-account, one-MiB-per-skin ceiling rather than silently
reducing it at the new boundary. HTTP bodies retain their separate 16 MiB bound.

`WhenIdle` retains its existing latest-request meaning. A separate internal drain
tracks replaced workers for native shutdown. Async call clients likewise distinguish
caller cancellation from physical peer completion; service retirement waits for the
latter before releasing completion callbacks. The existing skin supersession test now
asserts both behaviors, and the independent AOT fixture delays HTTP cancellation
retirement to check that shutdown produces no calls to a retired endpoint.

The implementation passed the 93-project architecture gate, 184 runtime tests,
852 service tests, 388 Desktop composition tests and 106 release-tooling tests.
Independent Linux x64 AOT runtimes exercised private profile/launch reads, catalog
queries, 64 concurrent selection commands with publication barriers, device login,
HTTP policy preservation, supersession, delayed cancellation and physical retirement.
The existing complete native window suite also passed after the additive async-client
retirement change. Generated DTO code was compared between product-version and fixed
1.0.0 contract assemblies: both retain fingerprint
`98D2E6943DE70F57C1C16E72A5DAA79333F491930DDDE5EB824AF7332B98904F`.

The complete Linux x64 payload passed admission/missing-module smokes, headless shell
projection and visible-window startup/OS closure. Its account image is 13,272,920
bytes and its UI image is 77,465,528 bytes; these are image sizes, not installer-size
savings. A clean account recompile with different release/Git metadata retained
SHA-256 `bb24c727db1b41dc0230ef2b08d93dc416432cab2e1d610272bd8cdc61fe9de8`.
The UI's published dependency closure contains no concrete account implementation.
The corresponding clean UI rebuild also retained SHA-256
`e03447f2f108a04a0ff6aaa366120cc2d6240d3c2f566dbf05d275171597a1e3`.
