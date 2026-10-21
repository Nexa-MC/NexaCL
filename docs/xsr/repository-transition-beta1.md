# NexaMC continuation and Beta 1 publication

The requested destination is the public `Nexa-MC/NexaMC` repository, carrying the current
`refactor/xsr` code and its Git history. The working branch remains `refactor/xsr`; product
branding stays NexaCL Firefly. Beta 1 has canonical identity `2.0.0.beta.1` and tag
`v2.0.0.beta.1`. Notes are in [the release draft](../releases/2.0.0.beta.1.md).

The independent public distribution repository is `Nexa-MC/NexaMC-Updates`. It holds
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
scheduled/manual workflow fetches the publisher tooling from the source branch and retains
the update repository's public key/fingerprint check. Update-repository tags identify
distribution metadata, while the source repository retains the corresponding code tag.

Publication order:

1. Validate the global entitlement and mainland distribution changes, including local-only
   launch/update behavior, signed-update source compatibility and AOT/trim deployment.
2. Create the continuation repository, push the XSR branch with its existing ancestry, retain
   the historical source branches/tags, select `refactor/xsr` as the default branch, and enable
   Issues and Discussions for the migrated feedback entry points.
   Create and initialize `NexaMC-Updates` with the seed files and the public verification key.
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

Operational status on 2026-10-21: the current GitHub integration can access the source
repository, but `POST /orgs/Nexa-MC/repos` returned HTTP 403 (`Resource not accessible by
integration`). Repository and organization Actions secret management also returned HTTP 403.
Creation, credential configuration, destination CI/release and final archival are pending
the required GitHub connection permissions. This is an external access limitation; the
repository must not be reported migrated, archived or released while those steps are pending.
The retried creation of both repositories still returned the same HTTP 403. Authentication
diagnostics show the environment applies its existing GitHub integration even to requests
without an Authorization header or with a deliberately invalid diagnostic header. Supplying
a PAT in chat therefore does not change this connection's effective administration rights.
An initial local `main` checkout is prepared at `/workspace/NexaMC-Updates` with the seed
workflow, README, original LICENSE and verified publisher public key. Its initial commit
is `ddc0e70`; it has not been pushed and does not establish a GitHub repository exists.

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
- The follow-up Linux Native AOT publish was interrupted during native code generation;
  it did not produce a verified final candidate. The follow-up trimmed publish was not run.
  Earlier policy-only AOT/trim evidence above does not validate this subsequent update-source
  change. Its deployment validation remains pending the dedicated CI build/checks.
