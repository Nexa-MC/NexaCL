# NexaMC continuation and Beta 1 publication

The requested destination is the public `Nexa-MC/NexaMC` repository, carrying the current
`refactor/xsr` code and its Git history. The working branch remains `refactor/xsr`; product
branding stays NexaCL Firefly. Beta 1 has canonical identity `2.0.0.beta.1` and tag
`v2.0.0.beta.1`. Notes are in [the release draft](../releases/2.0.0.beta.1.md).

Publication order:

1. Validate the global entitlement and mainland distribution changes, including local-only
   launch/update behavior, signed-update source compatibility and AOT/trim deployment.
2. Create the continuation repository, push the XSR branch with its existing ancestry, retain
   the historical source branches/tags, select `refactor/xsr` as the default branch, and enable
   Issues and Discussions for the migrated feedback entry points.
3. Configure the required Actions credentials securely. GitHub never returns stored secret
   values through its secrets listing API. Keep the existing publisher signing key/trust;
   do not generate a substitute production key or publish unsigned assets.
4. Verify CI in the destination. Publish the Beta 1 tag and human-written notes using the
   existing `Launcher Build` workflow, and verify its six platform packages and signatures.
5. Once the continuation and release are available, archive `Nexa-MC/PCL-N` and link to the
   continuation. Old releases remain accessible for the pinned fallback sources.

The existing workflow consumes `CURSEFORGE_API_KEY`, `PCLN_API_CLIENT_PFX_BASE64`,
`GPG_PRIVATE_KEY`, `GPG_PASSPHRASE` and the configured Microsoft/LittleSkin client ID aliases.
The Microsoft ID, CurseForge key and API client identity are release prerequisites. Secret
values must be supplied or transferred through an authorized secure channel, never committed
to source, printed to logs, or placed in plaintext artifacts.

Operational status on 2026-10-21: the current GitHub integration can access the source
repository, but `POST /orgs/Nexa-MC/repos` returned HTTP 403 (`Resource not accessible by
integration`). Repository and organization Actions secret management also returned HTTP 403.
Creation, credential configuration, destination CI/release and final archival are pending
the required GitHub connection permissions. This is an external access limitation; the
repository must not be reported migrated, archived or released while those steps are pending.

Prepared-code validation on 2026-10-21:

- Release solution build: zero warnings and zero errors.
- Services contract/regression suite: 791 passed. Coverage includes global non-Microsoft
  admission, mainland rejection before network activity, each missing base-game file kind,
  loader-only completion, verified local base reuse during pack updates, and pinned update
  repository fallback only after 404 responses.
- Desktop composition suite: 359 passed, including disabled mainland installation actions,
  direct-intent rejection and live Simplified Chinese, Traditional Chinese and English copy.
- Architecture validation: all 72 projects passed; no API baseline changes or boundary bypasses.
- Avalonia backend: 43 passed. Native startup smoke under Xvfb also passed, including themed
  splash ownership, loading/retry topmost behavior and presentation of the warmed window.
- Release tooling: all 64 Python tests passed.
- Managed executable: `--validate-shell` and `--validate-setup` passed for both `CN` and `US`,
  each using an isolated data directory.
- Linux x64 Native AOT publish and both executable validation modes passed for `CN` and `US`.
- Linux x64 self-contained trimmed publish and both executable validation modes also passed
  for `CN` and `US`. Both publishes record the existing Jint IL2098 warning without suppressing it.
- The final external About template and language catalog were refreshed from source in the
  local deployment candidates before the final executable checks; related UI checks and the
  architecture suite passed again after the source-link update.

These local checks do not constitute destination CI verification or a signed public release.
