# Install validation and modpack updates

Installation and content updates keep file length, digest, archive bounds, path,
local-file identity and running-instance checks. Metadata inspection must not treat
a plain Java library declared inside a loader-owned nested JAR as an unidentified
top-level mod. Dependency checks remain authoritative for known mod identities and
required version ranges; installed, disabled and bundled identities must be handled
in their actual loader context.
Incomplete dependency declarations are reported in the task rather than invalidating
an otherwise verified archive. Updates inspect changed mods against retained mods
and reject newly broken reverse dependencies, without requiring every unrelated
legacy mod to have a complete modern metadata table.

Modpack updates belong to Minecraft.Install, reusing the existing archive reader,
download verification and game/loader preparation. Desktop sends typed preview and
apply requests; it does not rewrite game files. A selected newer local archive can
update an existing isolated instance. Fresh installations record a managed-file
baseline. Updates replace or remove unchanged managed files, preserve user-added
files and locally edited configuration, and keep a durable transaction for rollback
or resumption of an interrupted publication.
Saves, screenshots and launcher identity/settings remain user-owned. Existing packs
without a baseline can supply their original archive to establish file ownership.
Conflicting managed binaries and changes since preview are reported before commit.

The update is a single task and must exclude running games, preserve the original
instance on preparation/verification failure, and recover an interrupted publication.
Source changes and malformed archives are never admitted by skipping validation.
Verification includes real nested-library archives, dependency versions and complete
update transactions with user files, cancellation, conflicts and rollback.

The preview/apply routes are `minecraft.modpack.update.preview` and
`minecraft.modpack.update.apply`. A preview binds the source archive, installation
fingerprint, managed-file baseline, update options and current target hashes; apply
recomputes that revision before preparation and publication. New installations keep
the version 1 baseline in `Nexa/ModpackManaged.json`, using generated JSON metadata
for NativeAOT. A version 3 installation publication plan permits pack content changes
only within the selected instance, plus the existing shared dependency scopes.
Versions 1 and 2 retain their original publication scopes. The task becomes
discoverable only after its complete publication plan exists, so recovery resumes
the verified file transaction instead of reinstalling the previous game version.

Use **Version settings → Modpack → Update modpack** to select a newer local
MRPack, CurseForge or Nexa archive, preview the changes and confirm the update.
Legacy installations without a baseline also require the original pack archive.
Modified configurations remain local; modified mod binaries require explicit
replacement consent and are retained in the transaction backup. A canceled
preparation is reported as canceled and leaves the original instance untouched.

Online pack information belongs to Resources. It identifies the installed archive
with the existing Modrinth SHA512 and CurseForge fingerprint/SHA1 adapters, reads
project/version metadata through the shared persistent resource cache, and offers
newer downloadable pack versions by publication time. Pack versions may change
Minecraft or loader versions; the existing installation preview exposes that change.
Incomplete or stale online results remain visible but cannot authorize an update.

Fresh managed manifests retain the original archive's SHA256, SHA512, SHA1, size
and CurseForge fingerprint. A shared bounded streaming fingerprint reader serves
both content identification and pack installation. Older installations can recover
that identity from the original-archive path in their authenticated installation
journal, or from a user-selected original archive. Completed installation staging
archives remain prunable; if the original was deleted the user must supply it.
No project is associated by display name alone.

Resources.Contracts may reference Minecraft.Install.Contracts for the online
preview's existing local transaction contract. The reverse dependency is forbidden.
Resources downloads a selected, freshly resolved provider version into its owned
archive cache, validates its published bytes, and delegates preview/application to
Minecraft.Install. Application revalidates the exact provider version, archive hash
and installed archive identity; local file conflicts, preservation and recovery keep
the existing semantics. Preview downloads do not modify the installed instance.

The online panel in **Version settings → Modpack** loads source/project information,
the installed provider version and newer downloadable versions. Choose **Download
and preview update**, review the existing file-change/conflict summary, then use
**Apply update**. Source and version information survives restarts through the shared
metadata cache; verified archive bytes are reused rather than downloaded again.
Offline information remains readable, while online preview/application require
fresh provider confirmation. A missing legacy source archive can be supplied with
the existing original-modpack picker. Installed sources are not guessed from titles.

Validation on Linux x64 (2026-10-18):

- Full solution Release build: zero warnings and errors.
- Full Services harness: 771 passed; full Desktop harness: 355 passed.
- Architecture harness: 72 projects passed.
- Final focused Services regressions: 6 passed in NativeAOT and the full managed
  harness, including 24 online update scenarios, 10 local update scenarios,
  malformed/partial resource identities and interrupted publication resume/undo.
- Official Modrinth project/version endpoints were probed with Fabulously Optimized:
  477 versions, primary MRPack SHA512 present, response within the 8 MiB catalog cap.
- Desktop shell/setup validation passed in managed, NativeAOT and trimmed builds.
  Native publication retains the pre-existing third-party Jint IL2098 warning.
