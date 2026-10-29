# Update center and versioned onboarding

The Settings update page keeps product identity, last check, channel and result
visible while discovery runs. Launcher and Sidecar updates use separate retained
rows and operations; a failed provider never empties the page. Existing protected
launcher update transactions remain authoritative.

Sidecar updates use a publisher-signed, platform-specific catalog and detached
image signatures. Immutable verified versions live under the launcher data root,
not inside an installed executable/application bundle. Publishing a completed
version and its active pointer is atomic; failed/cancelled downloads never become
executable candidates. The supervisor verifies the selected native image again.
Activation occurs at next launcher start, with the bundled version as fallback.
Update catalogs are release assets, not arbitrary Sidecar-supplied download URLs.

Onboarding completion is identified by publisher/package, page target and an
explicit revision, not session GUID, launcher version or dynamic UI contents.
Optional NXML TLV field 64 declares the revision (default `1`); fields 5–63 remain
source chunks. Continuing or explicitly skipping records completion atomically;
merely drawing a page does not. Crashes resume unfinished pages. A new revision
or newly installed page is queued on the normal shell, without replaying the
existing launcher storage/privacy initialization. Pure style changes do not
force an already completed login guide to appear again.

Every non-empty onboarding batch is framed by Host-owned welcome and completion
pages. These wrappers are intentionally not completion-filtered. No pending pages
and no interrupted batch means no wrappers. Resuming after interruption starts with the welcome page again,
then only unfinished content, then completion. Persistent receipts are written on
explicit completion/skip, never for the welcome page itself. A durable pending-batch
marker is removed only by the final completion button, so interruption after the
last content page still resumes welcome and completion.


## Distribution and compatibility

The release workflow extracts the already verified, pinned bundled Sidecar into
six standalone `<package>-<rid>.nsc` assets. Each runtime also gets a
`Nexa-Sidecars-<rid>.json` catalog with schema, runtime, release identity and package
id/version/SHA-256/size/minimum host version. The existing release signing stage
signs both catalog and image, and the update-repository mirror verifies the entire
set. The launcher manifest keeps its original package contract unchanged.

Older releases without catalogs remain valid. The update center reports that no
independent Sidecar distribution has been published; it does not infer that every
installed extension is current. Catalog publication begins with the next release
built through this workflow. Only already bundled package identities are eligible.
Publisher signature, runtime, host compatibility and content checks are required;
failure leaves the prior active pointer untouched. Restart activates the selected
package; this feature does not hot-replace a running Sidecar.

The package lock declares its reviewed `minimumHostVersion` independently of the
release version, so compatible later Sidecar builds can update without requiring
that exact launcher release. A catalog older than the running launcher's bundled
release or a newer active package is not a downgrade offer. Replacing the host
with a newer bundled release retires older per-user overrides.

See [native previews and regression evidence](evidence/update-onboarding-2026-10-29/README.md).
