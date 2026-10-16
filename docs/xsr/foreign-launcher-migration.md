# Foreign launcher instance migration

## Mainstream launcher coverage

The migration workspace supports the native Minecraft family (the official
launcher, Nexa/PCL II/PCL CE, HMCL, XMCL and other launchers retaining native
version manifests), the Prism/MultiMC/PolyMC family, Modrinth App, CurseForge,
ATLauncher, GDLauncher Carbon and legacy GDLauncher, FTB App and Technic.
SKlauncher, TLauncher and other launchers can use the native-directory adapter
when they retain standard Java version manifests; this does not promise to
convert proprietary client patchers. Family enum values already published
retain their numeric identities; new families are appended. Automatic
recognition examines supported families in
the selected directory rather than silently choosing only the first family.
Known container paths are resolved to the filesystem's stored directory casing
before deduplication, without following links. Thus `instances` and `Instances`
are one source on a case-insensitive volume, while two real directories on a
case-sensitive volume remain independent sources.
Adapters use actual installed manifests and exact Minecraft/loader versions;
unknown components and unfinished instances remain visible with a reason.

Native profile metadata is an input to discovery when it chooses a custom game
directory or declares isolation. Multiple official profiles pointing at the
same version but different game directories remain independent instances.
PCL and HMCL isolation metadata takes precedence over directory heuristics.
All metadata affecting those choices participates in the source revision.
When PCL's conditional global isolation requires a launcher-specific display
classification that is not recorded in the instance, preview asks the user to
set explicit instance isolation in PCL before retrying. It never chooses a
different saves directory based on an assumed classification.

An additive discovery query lists existing sources at a bounded set of standard
per-user launcher locations. It never scans drives recursively, imports data,
executes a launcher or reads account credentials. Each candidate exposes its
launcher family and exact directory; choosing one updates the ordinary manual
source form and requires the existing preview and confirmation before import.
Portable installations and custom locations remain available through the
directory picker. Discovery shares Setup's owned route lifetime, cancellation
and read-only filesystem checks; leaving the page discards late results.

GDLauncher Carbon reads `instance.json` and its `instance` game subdirectory;
legacy GDLauncher reads `config.json` and the instance directory. FTB App reads
installed `instance.json` metadata and its installed pack/version information.
Technic reads installed pack metadata and the corresponding game directory.
These formats are converted through the existing installer port, followed by
the common game-data allowlist. No foreign executable, launch command, account
database or Java installation is imported. A private or unknown launcher format
does not become supported merely because its brand appears in a location name.

Technic's `bin/version` records the pack build, not the Minecraft version.
The adapter reads `bin/version.json`, a standard installer archive's
`version.json`, or a vanilla client's embedded version metadata instead.
Legacy JAR injection and unknown launch entry points remain visible as
unsupported; they are never silently imported as vanilla Minecraft.

Upstream schema references used by these adapters:

- [GDLauncher Carbon instance manifest](https://github.com/gorilla-devs/GDLauncher-Carbon/tree/35533d7cd963be8955af4fe30f681a84ad527049)
  and [legacy GDLauncher](https://github.com/gorilla-devs/GDLauncher/tree/7e5346be21bb67b2b3ebac7103ef3e76e02a558c).
- [FTB App](https://github.com/FTBTeam/FTB-App/tree/3f2f2d5acd5926c965b83c5efcdeff12a98e0b19).
- [Technic Launcher](https://github.com/TechnicPack/LauncherV3/tree/345e3e0c0a48e6ec01c0ece84bd02d6418baff51).
- [PCL II game-directory selection](https://github.com/Hex-Dragon/PCL2/blob/main/Plain%20Craft%20Launcher%202/Modules/Minecraft/ModMinecraft.vb)
  and [HMCL stable game repository](https://github.com/HMCL-dev/HMCL/blob/release-3.6.15/HMCL/src/main/java/org/jackhuang/hmcl/game/HMCLGameRepository.java).

## Boundary and workflow

`Nexa.Services.Setup` owns read-only discovery, a bounded content preview, staging,
publication and a verified undo receipt. The renderer sends intents through the
Desktop controller; it does not resolve a launcher adapter or filesystem service.
This migration is separate from `LegacyMigrationService`, which converts only
Nexa settings and offline account metadata.

The user selects a source launcher/directory and a **new** destination directory.
A preview lists individual instances, Minecraft and loader versions, selected
data bytes, unsupported instances and warnings. Each imported instance receives
an independent Minecraft root below that destination, so isolation, shared saves
and identical instance names cannot merge existing user data. Desktop registers
the returned roots through existing library commands only after the whole migration commits.

Native Minecraft `versions/<id>/<id>.json` layouts (Nexa/PCL, HMCL, XMCL and a
plain game directory) preserve the required inheritance documents, client jars,
libraries and assets. Standard game data is copied without launcher state.
Prism/MultiMC component manifests and CurseForge, Modrinth and ATLauncher
profiles are translated into an explicit Minecraft version and supported loader
build. Their game files are overlaid only after the existing Minecraft installer
has created a real native launch manifest. Setup receives that installation
operation as an injected asynchronous port using primitive contract data; it
does not add a dependency on Minecraft.Install. Unknown/custom components remain
visible as unsupported and are never represented as launchable copies.

Discovery reads Prism `instance.cfg` plus `mmc-pack.json`, CurseForge
`minecraftinstance.json`, ATLauncher/XMCL `instance.json`, HMCL game-directory
configuration, and Modrinth's legacy `profile.json` or current `app.db` instance
records. Modrinth SQLite reads use a private bounded copy of the database and its
WAL, so even SQLite shared-memory bookkeeping cannot modify the source. Only
fixed instance/component and custom game-directory columns are selected;
account tables are not queried. Database and WAL bytes remain inputs to the
source revision. Unsupported database schemas or missing platform SQLite APIs
produce a visible unsupported instance, preserving the original data.
Private temporary directories canonicalize only macOS's verified system aliases
`/var` to `/private/var` and `/tmp` to `/private/tmp`; arbitrary links in selected
sources, destinations and temporary directory ancestry remain rejected.

The installer port also prepares its successful staging output for relocation.
The existing install authority binds task receipts to their original absolute
root, so those receipts must not be moved to a different game directory. The
installer's internal preparation operation accepts only a root immediately
inside an owned `.nexa-migration-stage-<guid>` directory. It authenticates each
completed task plan/status, takes the exclusive execution lock, and removes only
the three verified thin terminal files (`plan.json`, `status.json`, and
`execution.lock`). Pending, incomplete, unauthenticated or unexpected records
abort the migration; they are never discarded or rewritten. The migration's
whole-directory content receipt then records the relocated game files.

## Ownership and integrity

Source directories remain read-only and are never renamed, deleted or edited.
The preview revision covers source identity, selected manifest bytes and every
planned data file's SHA-256. Apply regenerates the plan and rejects a changed
revision, checks copied file hashes and regenerates the revision before commit.
Links/junctions, recursive source/target relationships, duplicate destinations,
invalid names and excessive file/manifest counts are rejected. Copies use new
files in an owned sibling staging directory. Cancellation and failure remove
only that owned stage. A same-volume directory move publishes the complete new
destination; an existing directory is never replaced.

Launcher credentials, account/token databases, Java executables, pre/post launch
commands, wrapper scripts and launcher configuration are not imported. The
standard game-data allowlist includes saves, mods, resource/shader packs,
configuration, screenshots and game option/server files. Native manifests and
game/loader jars are deliberate game inputs, not imported launcher programs.

A durable receipt records every committed file and directory. Undo first checks
the receipt identity, hashes and complete directory/file membership; any added,
removed or modified content refuses undo before deleting anything. It then
renames the unchanged import to an owned removal directory before deletion.
The source is unaffected. Accounts must be signed in again in NexaCL.

Receipt ownership is established by an injected
`ILauncherMigrationReceiptAuthority` port, backed in composition by the existing
machine-local recovery authority. Before publication it authorizes the exact
receipt bytes at their **final destination path**, outside the copied directory.
Reading, recovering and undoing a migration require that authority to verify the
same path and bytes. A receipt's own GUID or content hash is not proof that NexaCL
created it. After undo temporarily renames the directory, verification remains
bound to the original final receipt path. Preview needs no authority, while
apply, recovery and undo fail closed if the port is absent. A failed or canceled
publication can leave an unused external authority record; its exact path,
bytes and fresh random receipt identity cannot authorize a different import.

## Modrinth database discovery

Current Modrinth App instances use `app.db`: older releases expose `profiles`,
and current releases join `instances.applied_content_set_id` to
`instance_content_sets.id` (also matching the owning instance). The instance
directory remains `<config_dir>/profiles/<path>`; only `settings.custom_dir` is
read to locate a non-default configuration directory. Discovery selects only
instance identity, display name, install stage, game and loader versions. It
does not select account, token, hooks, launch override or cache columns.

The adapter loads the OS SQLite library on demand without an additional managed
dependency. SQLite can update a WAL shared-memory file even for a read-only
connection, so discovery never opens the source database with SQLite. It copies
the database and existing WAL into a private owned temporary directory, checks
the source hashes again, and opens that snapshot read-only in a transaction.
The snapshot is removed after discovery and is never copied into the import.
The source database/WAL remain preview-revision inputs; source changes refuse
apply. Limits are 256 MiB for each input file, 256 instances, 4 KiB per selected
text value, a 250 ms busy wait and a five-second SQLite execution budget, with
cancellation. Missing SQLite, an unknown schema, a corrupt or changing database,
unsupported loader data or an incomplete instance yields an explicit source or
instance diagnostic without disabling the other launcher formats.

On Windows, snapshot copying and revision hashing open only adapter-marked
`app.db` and `app.db-wal` metadata with read/write/delete sharing so an idle
Modrinth database connection can remain open. The bounded reader checks source
length and hashes again; changing metadata still invalidates the preview.
Ordinary manifests and copied game files retain their stricter read sharing.
The real WAL fixture keeps its SQLite writer open while checking preview/apply
and verifies source database, WAL and SHM bytes and timestamps remain unchanged.

The schema and path contract is from Modrinth's `packages/app-lib/migrations/`
`20240711194701_init.sql`, `20260611120000_instances-content-foundation.sql`,
`20260619120000_finish-instance-profile-migration.sql` and
`packages/app-lib/src/state/dirs.rs` in `modrinth/code`.

## Validation

Contract tests cover native inheritance and game-data preservation, converted
profile installation through a fake installation port, content revision changes,
conflicts, source preservation, cancellation and undo refusal after user edits.
The mainstream fixtures also exercise GDLauncher Carbon/legacy, FTB custom roots
and loader overrides, Technic installed runtime manifests, mixed-family source
directories, official profiles sharing a version but keeping separate worlds,
and both generations of PCL/HMCL isolation settings. Each newly converted family
has a complete preview, stale-source refusal, apply, receipt recovery and undo
case. Desktop covers source detection/selection, every source-kind choice and
late discovery results after navigation or cancellation.
Architecture and AOT/trim checks remain part of the repository closure gate.
This imports local instances; it does not import passwords or promise that old
mods are compatible with a different Java/Minecraft runtime.

## Desktop workflow and recovery

Migration route registration returns an asynchronous lifetime. Each query and
command enters that lifetime before touching Setup and links its caller token
to the lifetime stop token. Shutdown rejects new entries, cancels active work,
and waits for every entered handler and its owned staging cleanup to finish
before Setup's semaphore is disposed. A completed disk commit remains a success;
shutdown does not retroactively cancel its returned result. Page navigation
cancels only its own caller token and leaves the runtime lifetime available.

The storage settings page keeps the previous JSON settings/account migration
assistant and adds a separate “从其他启动器迁移” workspace. The source defaults
to automatic detection; users can cycle the explicit launcher family, choose a
source directory and choose a destination parent. Desktop creates a unique new
child-directory name and displays its complete path before preview or apply.
The preview exposes every instance's version, loader, file count, source bytes,
warnings and unsupported reason. Only importable checked instances enter the
explicit confirmation. Conversion may download Minecraft/loader dependencies
through the existing installer and task center; the byte estimate labels those
additional files separately. No foreign launch hooks or account credentials are
executed or copied.

After Apply succeeds, Desktop reads the durable receipt through its sealed query
route and registers each returned Minecraft root with the existing library
commands. It preserves each instance name/selection and selects the first
imported root. A registration failure retains the receipt and enables a retry
which registers the committed roots without applying or copying again. Existing
registrations remain unique because the library normalizes root paths.

Leaving the page or pressing Cancel cancels the operation token, retires the
pending generation and dismisses its confirmation. A late preview, picker or
completion cannot publish into the new page scope. Already committed disk data
is preserved; “恢复迁移记录” reads a selected migration destination across page
navigation or application restarts, then offers registration retry and undo.
Undo requires explicit confirmation, delegates the full ownership/integrity
check to Setup and forgets corresponding library roots only after that succeeds.
A refusal preserves the receipt and registered entries so the user can continue
using the changed copy.

Undo is refused while its roots are the only registered library directories;
the user must register another game directory first. Desktop checks the current
library snapshot and composition repeats that check at dispatch. Once disk
removal commits, directory-forget commands use a non-cancellable cleanup token
even if the page is left. A registration-write failure retains a pending cleanup
receipt in the controller and exposes “完成撤销登记” after returning. That retry
only forgets remaining registrations and never runs Setup Undo a second time.

The library directory label follows its existing 80-code-unit contract. Long
foreign names are shortened at a Unicode text-element boundary for that label
only; the full name remains in preview, imported instance metadata and receipt.

Native visual evidence is exported in a separate process with
`--native-launcher-migration-preview light|dark <output-directory>`. It uses the
shipped settings controller, NXML/NSS, feedback dialog, actual software Avalonia
window and real library commands. Typed foreign-instance routes, picker returns
and temporary source data are explicitly controlled fixtures. Captures cover the
initial form, preview and unsupported rows, selection confirmation, completed
registration and recovered receipt at requested widths of 1040 and 810 DIPs.
Each PNG has adjacent JSON containing actual accepted client size/DPI, complete
scene geometry, the migration card's four padding values and measured horizontal
insets between painted card surfaces and their visible contents. The main card
requires 16 DIPs and instance cards require 12 DIPs. Native work-area
limits are recorded rather than presenting a requested size as an observed one.
This evidence does not claim authentication or live third-party account data.

The discovery extension adds detected-source and selected-source captures at
both widths, bringing migration captures to 16 PNG/JSON pairs per light/dark
mode. Detection previews use controlled local source candidates and verify
that selection alone does not copy or register an instance.
