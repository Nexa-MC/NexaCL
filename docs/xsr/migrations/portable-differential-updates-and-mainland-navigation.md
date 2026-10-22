# Portable differential updates and mainland navigation

## Behavior

Previously the application always composed the system-helper control. A portable archive
therefore reported `CanInstall=false`, even when the release contained an authenticated
delta for its exact version. Portable archive layouts now compose an ordinary-user control
over the existing signed transaction. Public release/delta identities, signature pinning,
version floor, fresh slots, full fallback, offline recovery and rollback stay shared.
The initial extraction version is persisted separately from the running slot, preserving
idempotent completion and rollback after a process restart. The stable executable selects
the complete active slot and forwards arguments literally.
Native shell validation can initialize this installation-local state in its publish folder.
Packaging excludes the exact updater journal/lock names and reserved transaction/slot
directories, so validation state cannot be signed and distributed as application payload.

The portable adapter refuses elevated execution and system installation paths. It never
invokes the helper or the disabled legacy replacement/restart APIs. Its user-owned files
are not authority for a privileged process. System updates retain their protected adapter
and helper admission. Read-only images and unsafe system installations keep manual links.
Existing portable builds without this composition/bootstrap need one manual upgrade to a
published version containing the fix; this cannot retrofit a disabled updater into old bytes.

CN hides and disables the primary Install destination through a Desktop-owned state cell
bound in the shell NXML. The hidden destination occupies no layout space and rejects stale
selection/routing. Other countries retain all four destinations. The home download action
opens the official-launcher guide in CN; resource browsing, local import, direct modpack
installation with the required local vanilla base and existing instance editing remain.

## Evidence

- `PortableUpdateTests` exercises the real ordinary-user filesystem adapter, reduced delta
  transfer, authenticated full fallback, invalid-payload rejection, durable offline recovery,
  persisted baseline, idempotent completion, rollback, replay rejection, exclusive state and
  static link/layout rejection. Elevated runners explicitly test refusal instead of mutation.
- `PortableUpdateBootstrapTests` checks complete-slot selection, rollback, recursive-launch
  prevention, canonical slot names and literal argument forwarding without executing a fixture.
- `RegionalPresentationTests` checks CN/US/HK/MO/TW/unknown visibility, enabled state, rendered
  node exclusion, stale intents and continued visibility of the other destinations.
- The publisher smoke consumes generated bundles through the production portable adapter
  when unprivileged. The six-RID distribution workflow also runs portable regressions under
  NativeAOT. Existing protected native cases and their privilege requirements remain separate.

Physical power-loss, native signing/notarization and active same-account replacement
isolation for user-owned portable storage are not claimed by these fixtures.

Local validation: 802 Services cases, 361 Desktop cases, and 80 release-tooling cases
passed. Linux x64 NativeAOT passed the 14 update cases (protected native mutation cases
retain their dedicated-runner guard) and consumed the publisher-generated delta through
the ordinary-user adapter with zero full-package transfers.
The 72-project architecture suite passed. Linux x64 NativeAOT Desktop passed CN and US
shell validation, first-run validation and theme validation. Its actual startup-created
baseline journal was excluded by production payload staging while retaining the executable.
