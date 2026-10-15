# NSC startup and regional account policy

Country policy is independent of UI language and formatting culture. The deployment country
(`NEXA_COUNTRY`, ISO 3166-1 alpha-2), otherwise the OS region, selects policy. Only `CN`
enables mainland optimizations. Unknown regions use international rules. This is a deployment
setting, not a geographic attestation; IP geolocation is not introduced.

Regional admission and presentation use that same captured country policy. Outside `CN`
(including unknown, `HK`, `MO` and `TW`), mainland-only download-source preferences and mirror-provider switches,
resource mirror selection, mirror-resolution diagnostics and mainland-service acknowledgements
are omitted from the product UI, and their intent adapters reject unavailable choices.
Stored source preferences remain intact so a later CN session can restore them. The display
language and `general.region` formatting preference cannot enable these capabilities.

Game download admission also checks persisted source URLs: an old profile's mirror URL does
not become an official source merely because it is the original manifest value. Outside CN,
the source planner and final batch policy restore only exact, known product mirror mappings
to their official HTTPS origin (the LiteLoader/Forge/Fabric/NeoForge Maven namespaces,
content-addressed Mojang objects and assets, known version metadata, and the unlisted-version
archive). Known mirror URLs whose origin cannot be recovered are rejected before transfer.
The shared pure mapping lives in Common so Settings and Minecraft.Metadata enforce the same
decision without adding a service dependency. Manifests, expected hashes and sizes are not
rewritten; CN routing and unrelated global providers retain their existing behavior.

The existing mainland-only facilities are BMCLAPI/MCIM routing and MCIM translations,
the doh.pub DNS fallback, and automatic OptiFine mirror installation. OptiFine's automatic
catalogue tab is omitted outside CN; catalogue reads and prefetch stop before consulting
cached catalogue rows, provider adapters or HTTP. Local installation from a user-provided
official installer remains available. General loader/resource filtering, LittleSkin, third-party/offline
account providers and ordinary network diagnostics remain global. Manual network probes include
BMCLAPI/MCIM only in CN; other regions request and display the three official endpoints only.
International account additions
continue to use the verified-ownership admission below; their providers are not disabled by
country. Mainland purchase reminders are emitted only for admitted CN account operations.

International profile creation/import requires a Microsoft Minecraft Java entitlement
verified by the authentication service in the current Host session. An imported Microsoft
profile or a manually supplied token is not evidence. The roster mutation boundary enforces
the rule; mainland users receive a purchase reminder without being blocked. Ownership is
rechecked through Microsoft refresh when an existing profile is used as evidence.

NSC is now a standard native executable whose extension is renamed to `.nsc`.
There is no custom `NEXA NSC` magic, encryption, in-memory executable loading or temporary
executable extraction. Host validates the PE/ELF/Mach-O header and detached `.nsc.asc`
GPG signature against the embedded pinned release key, then starts the existing file using
the OS process loader. Unix deployments must preserve the executable permission bit.
Packages are discovered only at the top level of `AppContext.BaseDirectory`.

Host binds randomized current-user IPC before starting each child. Arguments are
`--nexa-sidecar --endpoint <endpoint>`; a random 32-byte bootstrap challenge is delivered
through redirected stdin. Sidecar echoes it as the first 32 bytes of its IPC stream before
frame traffic, then receives Host HELLO, replies WELCOME, sends REGISTER_BEGIN/ITEM*/END
and STATE_SNAPSHOT_BEGIN/ITEM*/END. Host sends READY and ACTIVATE. Bootstrap and registration
have a deadline; failures close the session and terminate the child without stopping other
packages. Credentials do not appear in arguments or logs. The package directory must be
owned by the launcher user/administrator; this does not claim protection from active
same-user executable replacement on Unix.

Kinds 1–6 remain unchanged; New UI is kind 5 (`UiModule`). Kinds 7–12 add UI Patch,
Event Catch, Event Listen, Intent Catch, Intent Wait and Function Patch. Extensions carry
semantic target (TLV 8), binary payload and SHA-256 hash. Host validates and retains them
transactionally with the session after the IPC bootstrap. The table is published only after
READY and activation. Disposal removes extension registrations with their owning session.
Registration does not permit arbitrary CLR/Harmony execution: generated Host patch sites
and renderer/event/intent execution adapters must consume these contracts explicitly.
Event/Intent execution follows [sidecar-signals.md](sidecar-signals.md), the bounded Function
ABI follows [function-patches.md](function-patches.md), and the UI caption State adapter follows
[sidecar-ui-patches.md](sidecar-ui-patches.md). New UI rendering, other visual properties,
further Function shapes and the independently owned Sidecar executable remain
separate work; declaration retention alone does not grant execution.
