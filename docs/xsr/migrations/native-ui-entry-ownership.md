# Complete UI entry ownership

The production `nexa-ui` image now owns the existing Desktop entry, XSR UI tree,
controllers, renderer, Avalonia backend, native window and application cleanup.
Managed development invokes the same implementation. The bootstrap loads a separate
`nexa-host` admission library, which validates the UI identity and forwards the
caller-owned UTF-8 arguments and base directory on the original OS main thread.
The v2 entry additionally receives the bootstrap's canonical product version. UI
validates and copies it before product initialization; managed development keeps
the existing assembly-version source. The UI image and its linked dependencies
use fixed assembly versions and deterministic source/link settings, so changing a
release label or unrelated Git metadata does not rewrite an unchanged UI image.
No managed references, tasks, exceptions or UI objects cross either C ABI.

The Host has no UI or service project dependencies. Publication rejects a dependency
on any other Nexa runtime project, Avalonia, SkiaSharp or HarfBuzzSharp. It also
continues checking that the complete UI runtime uses the eight external stable
engines. Installers and portable archives require both nonempty Host and UI images;
signed complete-file manifests and differential updates include the new image.

A native debugger identified a Host stack-buffer guard failure after successful UI
execution. Admission uses an explicitly pinned managed identity buffer, matching
the existing window client. The corrected bootstrap/Host/UI chain exits normally.
This is a cold-load path, so all required exports and the exact identity are checked
before application entry and images remain loaded until process exit.

Linux x64 NativeAOT publication produced a 2,408,424-byte admission Host and a
79,249,624-byte UI image. These are individual image sizes, not a claim that the
complete installer became smaller. The real bootstrap passed foreign-working-directory,
argument and exit-code forwarding, missing Host, missing UI and wrong UI export
smokes. Headless shell and first-run projections passed. A separate OS-level smoke
started the normal application in safe mode, observed its visible product window
under Xvfb, requested closure through `WM_DELETE_WINDOW`, and checked normal process
exit. The Windows/Linux publication jobs now run this visible-window smoke using
their matching native UI and OS close messages.
An actual clean recompile with a different release label and Git revision retained
the UI SHA-256 `a630fb0c1d0018237e4e3f8fd334f7b59d31b4eabb7419decb3c64b022d50f96`.
The resulting application still reported the bootstrap's `2.0.0.alpha.18` product
version, proving the release identity remains independent of the stable UI image.
Release tooling tests cover the enlarged required payload and reject re-embedding
UI/service dependencies into Host. Cross-platform main-thread and installer behavior
remain covered by the six-RID publication workflow.
The Release solution build completed with no warnings or errors, architecture
validation covered 90 projects, and all 106 release-tooling tests passed.

Business composition still runs inside the UI image in this migration. Accounts,
network/downloads, Minecraft and update coordination must each move into their own
native owner with async ports and ordered state replication. This migration is the
complete UI ownership move, not completion of those business boundaries.
