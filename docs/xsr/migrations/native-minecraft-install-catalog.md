# Native Minecraft installation catalog ownership

The Minecraft owner composes InstallCatalogService and MinecraftInstallDraftService
with its existing shared cache, canonical remote work scheduler, HTTP transport and
regional configuration. Catalog requests, prefetch, draft changes, eligibility and
edit planning execute there. The production UI keeps typed command/query adapters
and replicas of the catalog and draft cells. Managed developer APIs remain intact.

Caller-supplied catalog fixtures use an asynchronous reverse source port returning
bounded typed versions; no source object crosses runtimes. Production uses the
owner's HTTP catalog source. Source selection is immutable after catalog admission.
Catalog initialization is awaited before production commands or state-dependent
queries. Close cancels producers and drains the shared cache before callbacks retire.

Generated codecs gain bounded maps with string or defined enum keys and supported
value shapes. Existing string/string encoding and fingerprints remain unchanged.
Readers reject duplicate keys, undefined enum keys, missing reference values and
excessive counts. Draft enum selections and eligibility result maps use these
codecs without JSON messages or runtime reflection.

Installation, modpack update/inspection and local import production routes will use
the same owner-side installer and canonical task controls. The existing managed
installation interfaces are preserved through local adapters; file transactions,
verification, resume/pause and physical shutdown remain in the owner.

Desktop catalog adapters retain asynchronous eligibility/edit-plan query tasks and
wake frame projection on completion; stale input keys never reuse earlier results.
Draft commands are ordered, and dependent UI actions must await the corresponding
acknowledgement before reading state. No pending remote query may be treated as a
synchronously available eligibility result or block the render thread. Interactive
validation must include delayed queries and rapid selection changes.
Draft replacement is an atomic typed command containing the user's selected
version, primary loader, build rows and addons. The owner validates all inputs and
publishes once. UI selection workflows await this command and then project the
confirmed state; they do not replay local compatibility rules or expose a partial
sequence of choices to later queries.
Primary-loader selection is one owner-side draft intent. Selecting vanilla clears
builds; selected addon rows are retained only when compatible with the selected
primary loader. Desktop only projects the acknowledged choice and does not rebuild
this rule by querying a local synchronous service.
While a refreshed eligibility query is pending, rendering retains the last
confirmed page visibility. It does not hide the active catalog or navigate away
before the new result arrives. Commit still awaits the current eligibility result.
Visibility projection never mutates domain addon choices.

Validation: the independent NativeAOT caller passes 190 DTO shapes
(`2E31063E7D37DDC46DD91DDD90935312BE2070C2DDE9E16F9C3C0E6AEB25DEE4`).
It loads delayed caller-supplied game/loader catalogs, replicates enum-keyed draft
choices and typed eligibility maps, rejects duplicate/undefined map keys, and
continues to pass actual installation, launch, remediation, post-exit baseline and
physical-retirement cases. The previous string-map network schema remains exactly
unchanged. The desktop regression suite passes all 388 cases.

The full production NativeAOT UI passes module/CLI checks, 61-node headless shell
validation including real asynchronous catalog selection and compatible-addon
reveal with retained entities/segment controls, and actual X11 window startup and
normal OS closure. Validation waits for asynchronous frame convergence within its
existing deadline; it retains all selection, layout and identity assertions.
Final checks after asynchronous visibility fixes: all 25 installation-focused
desktop cases pass, the 97-project architecture gate passes, and regeneration of
the network's 29-shape protocol produces its original fingerprint unchanged.
