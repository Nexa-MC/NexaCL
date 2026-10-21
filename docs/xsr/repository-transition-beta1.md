# NexaCL continuation and Beta 1 publication

The user-created destination is the public `Nexa-MC/NexaCL` repository, carrying the current
`refactor/xsr` code and its Git history. After exact-source CI verification, the migrated
branch is renamed to `main` and becomes the only source branch and default branch.
Other imported branch refs and the temporary bootstrap `main` are removed at the user’s
request; Git ancestry and historical/release tags are preserved. Product
branding stays NexaCL Firefly. Beta 1 has canonical identity `2.0.0.beta.1` and tag
`v2.0.0.beta.1`. Notes are in [the release draft](../releases/2.0.0.beta.1.md).

The independent public distribution repository is `Nexa-MC/NexaCL-Update`. It holds
publisher-signed packages, release manifests, signatures and release notes; the source
repository retains code and Git ancestry. Launcher discovery reads the pinned update
repository's public releases first, selects a strictly newer canonical version matching
the requested channel/RID, and accepts only exact package identities at that repository.
An absent repository or no eligible release falls back to the existing API discovery
contract, including its rollout checks. HTTP errors and malformed candidate releases
remain failures. A public release in the dedicated repository is an explicit full-rollout
publication; unsigned discovery metadata never grants installation authority.

The protected helper downloads from the update repository first, then the pinned source
and historical repositories after 404 only. Publisher signature, version, channel,
anti-replay, hash and installation-boundary checks are unchanged.

`eng/release/publish_updates.py` mirrors a complete public source release only after the
existing independent signature and manifest verification passes. The update repository's
workflow uses its own `GITHUB_TOKEN` to publish there; no signing private key or cross-repo
write token is transferred. The seed files live in `eng/release/updates-repository/`; copy
the existing `GPG-PUBLIC-KEY.asc` and `LICENSE` alongside them when initializing `main`. Its
scheduled/manual workflow fetches the publisher tooling from the source default branch and retains
the update repository's public key/fingerprint check. Update-repository tags identify
distribution metadata, while the source repository retains the corresponding code tag.

The branch rename does not move the immutable Beta 1 tag or interrupt its in-flight
build. Old-source archival still waits for successful signed publication and mirror verification.

The final branch topology is `Nexa-MC/NexaCL:main` only. CI push filters, contributor
instructions, the update publisher checkout and current source links must follow this
rename; recorded historical XSR branch references remain historical evidence.

Publication order:

1. Validate the global entitlement and mainland distribution changes, including local-only
   launch/update behavior, signed-update source compatibility and AOT/trim deployment.
2. Create the continuation repository, push the XSR branch with its existing ancestry, retain
   historical tags and ancestry, rename the migrated branch to `main` as the sole/default branch, and enable
   Issues and Discussions for the migrated feedback entry points.
   Create and initialize `NexaCL-Update` with the seed files and the public verification key.
3. Configure the required Actions credentials securely. GitHub never returns stored secret
   values through its secrets listing API. Keep the existing publisher signing key/trust;
   do not generate a substitute production key or publish unsigned assets.
4. Verify CI in the destination. Publish the Beta 1 tag and human-written notes using the
   existing `Launcher Build` workflow, and verify its six platform packages and signatures.
   Replace the draft/preparation notices only after the repositories are operational.
   Run the update repository's publication workflow and verify its independently mirrored
   signed distribution and the launcher discovery response.
5. Once the continuation and release are available, archive `Nexa-MC/PCL-N` and link to the
   continuation. Old releases remain accessible for the pinned fallback sources.

The existing workflow consumes `CURSEFORGE_API_KEY`, `PCLN_API_CLIENT_PFX_BASE64`,
`GPG_PRIVATE_KEY`, `GPG_PASSPHRASE` and the configured Microsoft/LittleSkin client ID aliases.
The Microsoft ID, CurseForge key and API client identity are release prerequisites. Secret
values must be supplied or transferred through an authorized secure channel, never committed
to source, printed to logs, or placed in plaintext artifacts.

Migration execution on 2026-10-21: the user created both destination repositories and
explicitly authorized a one-time Action in the old repository to migrate existing Secrets.
The cloud integration cannot administer Secrets or repository metadata; the hosted runner
uses the separately authorized PAT. An ephemeral RSA public-key artifact binds encrypted
credential delivery to the exact workflow run, temporary branch, nonce and source commit.
Only ciphertext enters that branch; the private transport key stays in runner temporary
storage and is deleted at completion. No signing identity is replaced.

The temporary workflow copies known nonempty release Secrets only when the destination
name is absent, preserves existing destination values and the initialized source `main`,
and pushes historical heads/tags without force. Destination Actions are paused during the
historical tag import, then restored. The temporary migration branch is excluded from the
copy. CI and the signed Beta 1 build must succeed before the verified update mirror is
published; old-repository archival is the final operation. A failure leaves the old source
unarchived and restores destination Actions. This describes the authorized procedure;
completion is established by the resulting GitHub runs/releases, not these instructions.

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

Independent update-source follow-up validation:

- Release solution build: zero warnings/errors after scoped IDE0055/import formatting.
- Services runner: 793 cases passed, including channel/RID discovery, strict package URLs,
  primary-source routing, 404 fallback, real loopback transfers, signature rejection,
  differential recovery and quiet-scheduler cancellation. Five native/privileged/keyring
  fixture subchecks explicitly skip in the managed run and belong to their dedicated CI jobs.
- Three Desktop automatic-update scenarios passed; all 72 architecture projects passed.
- All 70 publisher tests passed. The six new mirror tests include actual GnuPG verification
  using an isolated test key, tampered-package rejection before destination writes, draft
  retention on incomplete uploads, and immutable published releases.
- The seed workflow YAML parses successfully. A live run in the new repositories remains
  unverified until creation/access is available; unit-test publication messages describe
  mocked GitHub operations, not an actual public release.
- The follow-up Linux Native AOT services publish succeeded after replacing a test-only
  generic `JsonArray` collection initializer that raised IL2026/IL3050. All seven targeted
  discovery, real HTTP, signed-update and scheduler scenarios passed as native executables.
  The existing Jint IL2098 warning remains visible. Cross-platform deployment is verified
  separately by the destination CI and tagged build.
