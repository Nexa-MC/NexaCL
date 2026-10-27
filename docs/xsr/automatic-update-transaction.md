# Automatic update transaction (XSR-754)

The same-account attacker boundary in update-privilege-boundary.md remains mandatory.
Automatic updates use a preinstalled native helper, not the legacy path-based apply or
restart APIs. The helper never loads user plugins or starts the launcher as administrator.

## Installation and activation

The machine installer owns a stable bootstrap and helper. The application payload is
installed in immutable version slots. Windows uses Program Files/NexaCL; Linux uses
/usr/lib/nexacl; macOS uses /Library/Application Support/NexaCL, with a public application
entry in /Applications. macOS must not use the group-writable /Applications directory as
privileged mutation authority. Installer types stay setup.exe/MSI, DEB/RPM/AppImage and DMG
containing PKG, with portable archives. Unsafe system installations use manual updates.
Portable archives have a separate, strictly unprivileged update composition described below.

The helper independently obtains the release manifest and detached signature from the
fixed Nexa GitHub repository, verifies the embedded publisher key, channel, RID, version,
exact package size and digest. Caller input is only a canonical public target version.
Network redirects cannot authorize unsigned bytes. All writes are relative to retained
admitted directory handles/descriptors. Staging is fresh and private; published payloads
are readable/executable, never writable, by ordinary users. Existing unsafe permissions
are refused rather than repaired.

A bounded, flushed append-only activation journal selects a completed slot. A partial
last frame preserves the preceding activation. A private transaction journal records
the authenticated manifest, package identity, previous activation and preparation phase.
Recovery re-verifies cached publisher evidence and actual package bytes. Accepting a
version advances the private high-water journal before activation; a persisted transaction
is the only authority to resume that exact accepted version. Rollback changes activation,
never lowers high-water. Old slots are retained; updates do not delete unknown files,
user configuration or files still used by a running game.

## Runtime boundary

### Portable archives

Portable launchers use the same signed release/delta verification, immutable slots,
journals, full fallback and rollback transaction under their existing extraction root.
They never invoke a helper, request elevation or write a system installation. The portable
directory adapter refuses elevated execution and symbolic links/reparse entries, uses create-new
payloads and exclusive transaction state, and preserves unrelated files. Its user-owned
state is not a protected authority against another process with the same account; that
account can already replace the portable launcher. This is distinct from the system helper's
mandatory same-account isolation, which remains unchanged. Read-only/AppImage and unsafe
system installations retain manual links rather than falling through to portable mutation.

The extraction root is derived from the running executable's fixed layout, including the
macOS bundle and active-slot layouts, never from update arguments or journal paths.
Restart and subsequent stable-bootstrap launches select a complete canonical-version slot
by that root's journal. The slot name and executable layout are validated before launch.
Running payloads are never replaced in place. Recovery while running a slot uses the stable
bootstrap's version as baseline, so selecting the same completed version remains idempotent
and rollback can return to the original payload. Existing builds without portable bootstrap
support require one normal upgrade to acquire this capability.

Platform abstractions expose admitted update directory/file leases, exclusive durable state,
and create-new operations. Platform.Runtime implements Win32 object-relative and Unix
descriptor-relative access. Services.Updates implements signature and transaction policy.
Nexa.Update.Helper is an intentionally new privileged executable composition root. It has
no Desktop, renderer, accounts, Sidecar, telemetry or plugin references.

The unprivileged bootstrap reads only protected activation state before normal application
initialization. UI update requests run asynchronously through a typed update contract;
progress is projected from protected state, never from caller-owned staging. Restart occurs
under the original user identity. Failure to admit the helper or installation reports the
need for a trusted system installation/administrator authorization; it never falls back
to privileged user-writable code.

Rollback publishes the selected previous version with phase `rolledback`; it must not
leave a completed-new-version status after selecting old bytes. The UI offers restart
only when the selected complete/rolled-back version differs from the running version.
If exit occurs between rollback activation and status publication, projection uses the
activation version instead of advertising the superseded completed update.
Progress polling schedules a cancellation-bound 500 ms wake through presentation State;
it does not depend on input or continuous render frames. A still-running authorized helper
can finish after the GUI exits, and the next GUI continues observing protected status even
if a redundant recovery request encounters the exclusive transaction lock.
The helper has a 20-minute cancellation budget covering response bodies and preparation,
not just HTTP headers. Timeout records a recoverable pause before the short activation
commit. DEB/RPM metadata requires the fixed system pkexec tool used for Linux authorization.
Startup recovery and progress consumption run while the settings page is hidden as well;
opening settings is not a prerequisite for continuing an interrupted authorized update.
Manual retry preserves the protected pending transaction's channel, independently of a
changed discovery channel. CI offers keep manual download links: unordered commit labels
cannot authorize a public monotonic automatic update and must not display an install action.

## Validation

New Windows public directory leases request read/traverse rights without DELETE at creation.
This permits concurrent protected projection without adding delete sharing or reopening by
name. Such published directories are not eligible for the private empty-staging cleanup API;
an unexpected creation/admission failure is reported without path-based deletion. Private
staging retains its original object-bound deletion semantics.

Deterministic tests interrupt reception, extraction, durable preparation, high-water and
activation boundaries. They assert old-or-new complete activation, safe exact-version
resume, monotonic anti-rollback, bounded archive output and rejection of malformed paths,
links, special files and same-account mutation rights. Native CI runs real filesystem
admission and installer layouts on six RIDs, plus helper AOT smoke and Desktop trim/shell
checks. Native signing/notarization and physical power-loss testing remain separate external
acceptance; a green managed test is not evidence for either.
Installer smoke runs the installed helper's non-mutating `--validate-installation` mode,
which admits its exact fixed executable path and the entire real installation namespace.

Windows Setup sets its elevated process token's default owner to BUILTIN Administrators
before creating installation objects. Ownership therefore starts protected; no post-copy
owner/DACL repair window is introduced. This does not admit or change existing unsafe
objects. A prior unsafe installation still requires a clean reinstall, as reported by
the helper. MSI runs its machine-wide file installation through Windows Installer.

Local HTTP validation belongs only to the Services test executable. A loopback-only fixture
serves a freshly test-signed release; a test transport maps the fixed publisher requests
onto that fixture. It exercises the production release source, verifier and transaction
against an isolated temporary directory, without elevating or executing fixture payloads.
The installed helper has no endpoint/key override and cannot consume this test authority.
Interrupted bodies, invalid signatures/digests, tag fallback, offline recovery and rollback
are checked through actual HTTP transfers. The optional standalone fixture listener binds
only IPv4 loopback and never reads arbitrary files or accepts uploaded content.

Differential reconstruction and its independent publisher envelope are specified in
[XSR-755](migrations/XSR-755-protected-differential-update.md). Optional differential failure
falls back to the signed full package in a separate fresh slot; it cannot relax admission,
replay protection or activation rules.

## Portable publisher redirects (2026-10-27)

The shared launcher HTTP client deliberately disables automatic redirects. Publisher
assets must therefore follow bounded redirects inside GitHubUpdateReleaseSource, for
both envelopes and full/delta payloads. Follow at most five hops to HTTPS on port 443,
without userinfo or fragments, and only github.com or its fixed asset CDN hosts.
Create a new GET per hop, dispose intermediate responses, preserve cancellation,
and keep manifest/signature/hash validation unchanged. A 404 can still try the next
pinned tag/repository; other failures must not be hidden as missing assets.
