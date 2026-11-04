# Launcher feature parity implementation

English | [简体中文](launcher-feature-parity.zh-CN.md)

The baseline is Alpha 21 (`3c1862e4e`). This work implements the user's explicit
feature list independently; external launchers are read-only behavior/format
references and are not implementation dependencies. Internet lobbies and tunnels,
remote homepage subscriptions and remote server-pack subscriptions are outside
this explicit list.

| Capability | Implementation and acceptance |
| --- | --- |
| LAN discovery | Bounded UDP multicast listen, deduplicated endpoints, server UI import, cancellation on exit |
| HMCL/MCBBS/MultiMC imports | Detect manifests and optional wrapper directories, normalize versions/loaders/files, existing staged installer, malformed/conflicting archive rejection |
| Export formats | MRPACK/ZIP plus CurseForge, MCBBS (HMCL-compatible) and MultiMC, round-trip import tests |
| Launcher and Java bundles | Explicit portable closure/runtime selection, exclude credentials, reproducible paths and usable start entry |
| Optional files | Stable per-file IDs in preview, independent selection in install/update, persisted selections |
| Instance scripts | Windows/POSIX quoting, selected root and instance, current launcher identity refresh, no credentials in scripts |
| Content previews | Archive inventory, metadata, bounded text/image preview, schematic metadata and embedded preview |
| NBT tree | Read-only typed tree, compressed/raw Java NBT, bounded depth/arrays/nodes, paged navigation |
| Offline customization | Optional UUID at creation, local skin selection, in-game local texture/authlib lifecycle |
| Nide8 compatibility | Dedicated server ID/account flow and compatible launch preparation; no credentials in UI state |

Each capability must include its Desktop entry, managed and native registration,
binary contract coverage, focused failure/cancellation tests and architecture
checks before it is marked complete. No new alpha release is implied by this
implementation request. Check actual validation output instead of inferring
success from a button, a DTO or an upstream class name.

## Entry points and boundaries

- Instance settings / Servers scans LAN automatically, offers immediate rescan and join/save actions, and cancels listening on navigation.
- Dropped ZIP/MRPACK archives normalize supported manifests and one wrapper directory. Optional install/update files have stable IDs and independent choices. Online installation binds the reviewed archive SHA-256. MCBBS `force` is update policy, not an optional-file flag.
- Instance settings / Modpacks and export is available in instance development mode. MRPACK, directory ZIP, CurseForge, MCBBS/HMCL-compatible, and MultiMC exports share file filtering. See the completion checklist for extended component/settings support; basic format recognition is not full compatibility.
- Portable exports select an explicit launcher distribution and Java home. Include only admitted release/runtime files, preserve legal files and execute permissions, and reject escaping/cyclic links. First use still requires setup and account login. Platform/architecture follow the selected files.
- Instance scripts preserve target root/instance through single-instance and setup handoffs. Launcher-mediated scripts refresh identity and do not store account tokens. Independent-script acceptance is tracked separately.
- Content/NBT inspection is read-only and bounded. Directory/ZIP inventories, UTF-8 text, PNG, raw/gzip/zlib NBT, numeric tree paths, and paged arrays share the existing query. Schematics expose metadata/palettes and embedded thumbnails without executing game/shader code. Region decoding and complete schematic summaries are tracked by the completion checklist.
- Offline profiles support custom UUID, local PNG skin/cape, and Slim. Protected profiles own texture bytes and copy them at ingress/egress. Random loopback texture endpoints use signed properties and the verified authlib-injector. Endpoints live with Minecraft services; closing the launcher stops later requests, while the game may retain downloaded textures.
- Nide8 uses a 32-hex server ID and the official `auth.mc-user.com:233/<id>/authserver` endpoint. Refresh retains character identity. The official agent is downloaded from fixed HTTPS metadata and checked against its published digest.
- Global Java settings and instance game settings expose automatic/manual/instance policies. Portable relative and major policies and broader version-local discovery are tracked in [completion](parity-completion.md). The launched instance supplies the actual directory after moves or renames.

## Verification evidence

The original implementation baseline passed 876 service cases, 393 Desktop cases, 101-project architecture/layout checks, independent Accounts/Minecraft/Host NativeAOT callers, protocol generators, and actual Avalonia windows at 1040/810 widths. The version-local Java increment added 59 focused Java/offline checks and native probing/isolation coverage. These are historical results for those revisions, not automatic evidence for subsequent changes.

Current completion requires executed checks recorded in [parity-completion.md](parity-completion.md). Windows firewall interactions, actual game appearance, and Java distribution differences require platform/device acceptance in addition to contract tests.
