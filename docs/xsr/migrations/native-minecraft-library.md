# Native Minecraft library and directory selection

Minecraft owns library registration, discovery, remembered selection, directory
names, instance deletion and rename reconciliation. Its state is replicated to
Foundation; production composition retains asynchronous command adapters. The
managed public library API and existing durable document format remain supported.

Persistence uses a typed library configuration port backed by canonical Settings.
The host decodes/encodes the existing local settings document; only the closed
configuration DTO and an opaque revision cross the binary boundary. A conditional
text exchange in Settings prevents an external edit from being overwritten between
read and durable save. No JSON settings document or synchronous persistence call
crosses the module boundary.

Mutations serialize through asynchronous admission. Discovery may overlap a later
directory change, but generation and cancellation checks prevent stale state or
selection from publishing. A provisional cached inventory never authorizes
deletion. The owner checks active process state and shares the same recovery gate
as installation and resource updates. Durable selection is committed before the
acknowledged state is published. Closing cancels and drains scans and mutations
before retiring settings or state callbacks.

Production library composition now uses the owner and replicas. The canonical
Settings adapter keeps the existing document format, rejects malformed/null
documents, and conditionally persists the exact value it read. Managed concrete
library APIs retain their synchronous compatibility methods and gain an additive
asynchronous port. Native commands await initialization; rename reconciliation
occurs before its acknowledged notification, with subsequent discovery updating
the visible selection.

Independent NativeAOT validation covers durable initial/renamed selection, display
names, normalized directory removal, final-directory protection, cancellation of a
held old discovery, real instance recycling and rejection during real game launch.
Close retires a held library discovery together with a held installation-policy
query. The schema contains 366 shapes, fingerprint
`E2785C00C6CE451BABCCD588E26051E293B891F0DBEFF1AD847B40F596D6A654`.
The existing install/import/management/resource/launch tests still pass; full UI
AOT, headless validation and real window close/exit pass. Eleven focused service
regressions include the managed library compatibility cases and a concurrent
conditional-settings-save test. The 97-project architecture and formatting gates
also pass.
