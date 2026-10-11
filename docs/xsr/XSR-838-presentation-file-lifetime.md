# XSR-838 — presentation file lifetime

Background instance metadata reads can outlive page navigation. On Windows the old
read handles denied delete sharing, so retired resource-pack reads could block
rename, disable or deletion until archive parsing completed. This also made
Desktop teardown fail intermittently in the long resource-pack list regression.

Archive, local metadata and screenshot presentation readers now hold read handles
with `FileShare.Read | FileShare.Delete`. A rename or unlink does not change the
bytes already held by that handle. Write sharing remains denied. Transaction,
hash-validation and installer readers retain their existing stronger ownership
requirements; this change is limited to non-authoritative presentation metadata.

A deterministic regression holds the real production read handle, verifies that
Windows rejects a concurrent writer, renames and deletes the file, then parses
the original archive from the still-open handle. No timing delay or retry hides
the failure. Existing bounded metadata and screenshot identity checks remain.
