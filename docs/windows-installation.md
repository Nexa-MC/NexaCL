# Windows installer and Explorer certificate trust

EXE and MSI offer a welcome flow, optional Windows 11 Explorer integration, and an interactive completion option to open NexaCL. Silent installation does not open the launcher. Installation remains machine-wide; the launcher and sparse-package registration run in the initiating user's context.

Explorer integration is **off by default**. Selecting it explicitly authorizes trusting the dedicated NexaCL code-signing certificate in `LocalMachine/TrustedPeople` for every user. No certificate is added to Root. The helper checks an embedded SHA-256 pin, end-entity Basic Constraints, and code-signing-only EKU. It never imports a PFX or private key.

The administrator-owned registry journal under `HKLM/Software/NexaCL/ExplorerTrust` records installation roots and whether Nexa added the certificate. Reinstallation is idempotent. Another installation's reference preserves trust; after the final reference is removed, Nexa deletes only a certificate it added. Preexisting trust is preserved. MSI schedules certificate rollback after copying files; EXE cleans up trust if registration fails. Uninstallation removes package registrations before removing its trust reference. An interrupted operation leaves an ownership record available for repair/removal.

## Signing identity

Generate a persistent, dedicated identity once on a protected machine:

```powershell
./eng/release/prepare_windows_signing.ps1 -CreatePfxPath E:\secure\NexaCL-Explorer.pfx
```

Back up the PFX and password outside the repository. Configure GitHub Actions secrets `NEXA_SHELL_SIGNING_PFX_BASE64` and `NEXA_SHELL_SIGNING_PFX_PASSWORD`. The release workflow imports the key into its ephemeral user's personal store, sets `NEXA_WINDOWS_SIGNING_THUMBPRINT`, builds the native helper with the certificate's SHA-256 pin, signs the helper/DLL/MSIX, and packages only the public DER `signer.cer`. The key is removed from the runner afterward. Missing credentials fail packaging instead of silently publishing an unusable unsigned extension. Do not create a different signing identity for each build.

Self-signed output is not publicly trusted. The build does not install trust to make verification pass; Windows validates the signature during optional deployment after peer trust is established. A public CA identity also remains supported and receives SignTool chain verification. Changing the signer/publisher is a migration: retire all old installations or explicitly migrate their certificate references before retiring the old identity.

## Verification boundaries

Native tests use an in-memory certificate store and an isolated current-user registry fixture; they do not modify machine trust. Release smoke tests explicitly select the integration in both installers on disposable Windows runners, register the MSIX, and uninstall. Local installer compilation and certificate-state tests are distinct from an interactive wizard check and the complete NativeAOT release smoke.
