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
Account additions in every country
use the verified-ownership admission below; their providers are not disabled by
country. Mainland purchase reminders are emitted only for admitted CN account operations.

Non-Microsoft profile creation/import in every country requires a Microsoft Minecraft Java entitlement
verified by the authentication service in the current Host session. An imported Microsoft
profile or a manually supplied token is not evidence. The roster mutation boundary enforces
the rule, including mainland China; there is no country exemption. Ownership is
rechecked through Microsoft refresh when an existing profile is used as evidence.

Mainland China does not receive standalone Minecraft base-game downloads. New standalone
game installation is rejected at service admission, before metadata or payload transfer;
retries and recovery cannot bypass that admission. Direct modpack installation in CN first
checks for the required local vanilla client, then verifies its client, base libraries,
asset index and asset objects against the pack's pinned vanilla metadata. A missing,
incomplete, corrupt or different-version base stops before loader/mod payload transfers and
returns a retryable, version-specific prompt to install that version with Minecraft Launcher
and import the complete installation into the selected game directory. Metadata discovery
remains available; no missing base-game payload or asset index is downloaded in CN.
An intact local base is reused without another installation confirmation. International
modpack installs continue to acquire required base-game files automatically. The same policy
applies to pack updates, retries and recovery without changing the shared country policy.
Pack validation, hashes,
transactional publication and task cache reuse remain unchanged. Launch file completion and
ordinary instance edits may reuse verified local game files, but cannot fetch a
missing/corrupt base-game file in CN. Version catalogue metadata, account authentication, Java, loaders, mods,
resource/shader packs and user-provided local imports remain separate capabilities.
The policy uses the same captured deployment country described above, including its stated
limitations; UI language/format settings do not change admission. International download
behavior remains available. The UI explains the restriction and does not offer unavailable
base-game installation actions.

Direct-pack regression coverage checks the version-specific prompt for absent, wrong-version,
incomplete and corrupt local bases, successful MRPack/Nexa installation with an intact CN
base, and automatic base acquisition outside CN. Existing CurseForge author-download
restrictions, pinned-archive retry and interrupted-install recovery fixtures cover both CN
and international policies. Standalone installation and ordinary launch-completion
restrictions remain covered separately.

The continuation repository is `Nexa-MC/NexaCL`, carrying the existing XSR branch and Git
history. Source/about links and publisher packaging use the new repository. Signed update
downloads prefer the independent `Nexa-MC/NexaCL-Update` distribution repository, then the
source repository and the two exact historical locations as 404-only fallbacks. Update
release links accept only those four pinned repositories and the exact
candidate version; unrelated repository URLs remain rejected. Existing signing trust and
artifact identity are unchanged. Repository creation, required CI credentials, verification
and the `2.0.0.alpha.8` publication precede archival of `Nexa-MC/PCL-N`; an unavailable GitHub
administration permission is not grounds to archive before the continuation is usable.

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

## Mainland Java acquisition handoff

The new Java download destination in CN is an official-launcher guide, linking only to
`https://apps.microsoft.com/detail/9PGW18NPBZV5` (Minecraft Launcher, not the Bedrock
product). It opens the existing HTTPS platform action after an explicit, visible button
activation and never dispatches a game-install task or catalogue request. The guide
explains the Windows Store requirement and continued local-import support on other OSes.
Existing instance editing retains its original editor; the service-level standalone Minecraft
download restriction and international installation flow remain in force. The guide does not
disable direct modpack installation through the existing file activation/import flow.

The Java guide and Bedrock handoff share `OfficialDownload.nss` and explicit padded
content inside separate painted card carriers. `--native-install-guides-preview` exports
their actual light/dark renderer output at 1280x800 and 810x500, including action bounds
and visible card insets; preview callbacks never open the browser or Store.
