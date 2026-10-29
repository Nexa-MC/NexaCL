# Update center and versioned onboarding evidence

Native Avalonia offscreen rendering, Linux x64, software rendering, 800/1040 ×
700, light/dark. The checked-in screenshots are selected from 24 captures.

- [Welcome, light](SidecarOnboardingStart-800-light.png)
- [Completion, dark](SidecarOnboardingEnd-800-dark.png)
- [Update center, light](update-center-1040-light.png)
- [Sidecar retry, dark](sidecar-failed-800-dark.png)

The update preview uses controlled provider fixtures, not a claim that a live
Sidecar update is currently published. Reproduce with the Desktop test executable:
`--update-onboarding-preview <directory>` under a native display or Xvfb.

Regression coverage:

- Nonempty onboarding always starts with welcome and ends with completion.
- Completed package/page/revision is skipped across new sessions; a new revision
  reappears. Other package/page identities do not share receipts.
- Closing during a page replays welcome and resumes the incomplete content.
- Closing after the final content page still resumes welcome/completion.
- Existing first-run continuation retires UI drafts and cancels pending commands.
- Sidecar truncation/signature rejection leaves no active pointer; explicit retry
  installs verified bytes. Corrupt local catalogs/images fall back safely and can
  be repaired. Restart selects the signed package; incompatible/older offers are
  not installed.
- Independent release catalogs/images participate in the complete signed asset
  set; changing their bytes/signatures is rejected.

Validation: Desktop build (zero warnings/errors), 13 update cases, 6 onboarding
cases, 36 Sidecar protocol cases, architecture fitness for 72 projects, and
95 release-tool cases passed. Linux x64 NativeAOT publication and the native
`--validate-shell` process passed (exit 0). The dependency Jint emits IL2098 for its
reflection accessor annotation; no new application trim warning was reported.
These are Linux cloud checks, not Windows/macOS hardware acceptance.
