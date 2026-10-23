# Backup reconciliation against refactor/xsr

The comparison base is `1b69f8c3a`. The source snapshots are stash
`49b939aa10de919a432e992dfba4c891f39500cc` (28 modified and 16 new files)
and the older `codex: preserve XSR-783 work before upstream sync` stash
(10 modified files). Neither snapshot is applied wholesale or deleted.

## Boundary decisions before implementation

Restore behavior only where the current upstream has a concrete gap. Keep the
completed external NXML/NSS presentation and its typed service routes. An update
intent must identify a live, enabled control in the current card **and** match that
control's declared command; membership in the card alone is insufficient.

Keep upstream's single JVM Host, native-option encoding selection, lossless
conversion checks, Windows Job ownership and stream forwarding. Do not restore the
older two-executable scheme, Java 8 URLClassLoader reconstruction, alternate
assembly identity or the update/package requirements that exist only for that
scheme. Its initialization behavior is not an incremental change to the current
encoding contract. Test the actual selected runtime rather than infer support
from a filename or an ASCII transport of arbitrary Unicode text.

Retain the backup's separate fixture compiler option, with Java 8 target bytecode,
so JRE-only installations can be tested without changing the JVM under test. Add
the Windows portable smoke to the existing disposable-runner installation flow,
using the three executable names required by upstream. Only exact root-level
names may become temporary filesystem destinations. Reject development artifacts
and missing/empty runtime files before executing the host.

## Disposition of the saved changes

| Saved area | Upstream evidence and action |
| --- | --- |
| Settings update controller, main controller and localization; eight new UI/template/style/test files | `SettingsCustomUpdate*.d.nxml`, `SettingsPageController.Updates.cs` and `CustomUpdateProjectionRetainsCardAndRejectsRetiredActions` already implement external presentation, in-place refresh and retired-control rejection. Keep those; restore only source/command pairing and the useful regression assertions. Do not add a second text/template catalog. |
| Older XSR-783 Program/About/controller/catalog/localization changes and tests | `SettingsPageController.DiagnosticExport.cs`, `SettingsPageController.Logs.cs` and `DiagnosticExportPresentationTests.cs` already provide log opening, shared export state, retry, cancellation and leaving-page retirement. Keep upstream. |
| Contracts friend assembly; JVM Host project, manifests, NativeJvmHost and LegacyProperties; process executable selection and tests | These belong to the older dual-Host encoding approach. Keep current `JvmOptionEncoding` and the established executable/bootstrap contract; do not restore the alternate assembly or class-loader rewrite. |
| JvmChildLaunch stdout/stderr forwarding | Upstream already drains both streams, preserves inherited stdin and owns auxiliary processes with a Windows Job. Keep upstream. |
| Launcher/XSR CI changes publishing and requiring a legacy Host | Not applicable to the retained single-Host design. Keep upstream release destinations, signing/distribution preparation and Java/OS matrix. |
| Protected full/delta extractors, delta.py, package.py and associated update/payload tests | Saved changes require the old legacy executable. Upstream already checks the supported Desktop/JVM Host/helper payload. Do not impose a nonexistent executable requirement. |
| smoke_install.py, test_install_scope.py and new test_jvm_payload.py | Adapt and restore portable payload validation, real Host smoke, development-artifact rejection and the invocation regression, using upstream's supported payload. |
| Test-JvmHost.ps1, Test-JvmModCompatibility.ps1 and Java fixture | Restore the optional separate compiler and Java 8 bytecode target. Keep upstream Unicode/native-encoding rejection, quoting, inherited streams and process-tree lifecycle tests. Do not replace them with the older Unicode carrier or hardcoded vendor assumptions. |
| Runtime KernelTests/Program scheduler changes | Upstream's `SchedulerFaultsAreClassifiedAndIsolated` already checks correlation IDs, deliberately out-of-order async completion and continued execution. Keep upstream. |
| Saved XSR-837 draft | Superseded by the current encoding implementation and this reconciliation note; do not publish its historical implementation claims as current behavior. |

## Verification, 2026-10-23

- The restored update-intent regression fails against upstream with two unexpected
  opened links. With command pairing it passes; all 11 update-focused tests and
  all 359 Desktop composition tests pass. The same regression verifies the valid
  download route, literal version metadata, retained controls and retired actions.
- All 75 release-tool tests pass. The portable tests cover staging/symbol removal,
  Host execution, missing/empty/repeated runtime files, nested development artifacts
  and a broken executable. Portable extraction reuses `delta.copy_exact` for actual
  byte/declared-length limits. The installer-flow fixture verifies the portable
  check is invoked and leftover helper files are rejected between installers.
- All 173 runtime tests pass; architecture checks pass for 72 projects. These also
  verify the preserved upstream scheduler work without restoring duplicate tests.
- A fresh Windows x64 NativeAOT JVM Host publication succeeds. Both JNI lifecycle
  and CA-like auxiliary-process fixtures pass using Java 8 JRE 1.8.0_503 with a
  separate JDK 21 compiler, and using Java 21.0.10 with the default compiler.
  Normal return, Java exceptions, exit codes, cancellation, missing runtimes,
  malformed/unrepresentable text, inherited output and parent/child cleanup are
  exercised. These are real native JVM processes with repository-owned fixtures,
  not a full Minecraft or cross-platform game certification.
- Targeted C# builds finish with zero warnings/errors. Scoped whitespace formatting
  exits 0 with an existing workspace-loading warning; the PowerShell parser and
  `git diff --check` pass. No semantic formatter or API-baseline changes are made.

Logs are under `.tmp-cleanup-validation/backup-reconcile-20261023/`. Actual Windows
installer installation/uninstallation is not run on the developer machine; it
remains in the existing disposable CI runner flow. Both source stashes and the
standalone backup patch/archive are retained for recovery.
