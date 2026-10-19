# Resource mirror transport and regional ownership

Resource mirrors remain a mainland-China feature, selected by
`RegionalPolicy.IsMainlandChina`, independently of display language. Outside that
region metadata, fingerprints, archive bytes and icon bytes use permitted official
origins only. MCIM translations return no result before reading their cache or
performing HTTP. Country and effective source policy remain part of metadata and
icon cache identity; a saved mainland preference does not authorize a mirror in
another region. The network provider switches apply to every attempted origin.

Distributed launchers embed the `CURSEFORGE_API_KEY` Actions secret as the
`NexaCurseForgeApiKey` assembly metadata value, supplied through the publish step's
environment rather than a shell-interpolated command. Desktop reads the packaged
value and passes it explicitly into resource composition; services do not inspect
Desktop assemblies. Resource transport owns configuration precedence: nonempty
`NEXA_CURSEFORGE_API_KEY`, legacy `Nexa_CURSEFORGE_API_KEY`, then
`CURSEFORGE_API_KEY` override the packaged fallback. Existing explicit transport
credentials, including an empty value disabling the official key, retain their
meaning. Keys never enter source-policy/cache identities or UI state, and are sent
only to `api.curseforge.com`, never Modrinth or mirror endpoints. Public composition
callers retain the existing overload and environment-only behavior.

Launcher Build checks configured credentials in the actual published executable
using `--validate-resource-credentials`. The check verifies presence and, when the
build environment is retained, exact equality; it reports no key and exits before
GUI startup. Official Launcher Build artifacts and tagged releases require the
secret; builds without secrets remain available to contributors in forks. AOT and
trimmed CI smoke use a synthetic key containing dollar signs so fork CI also
verifies metadata retention without real credentials.

The Host also masks the saved mirror-provider preference when it captures an
effective HTTP policy: `MirrorProviderEnabled` requires both mainland China and
the saved opt-in. This is evaluated for every new request through the common
network pool, including a mirror URL retained in an old instance. It never
rewrites the saved preference and does not disable official providers. Existing
finite mirror-host classification remains the admission boundary.

The MCIM metadata paths are `/modrinth/v2/` and `/curseforge/v1/`. Its current
CurseForge translation query is `modId`, not `mod_id`. Public requests checked on
2026-10-15 returned valid JSON, including a 543 KiB CurseForge search taking 21
seconds and a 532 KiB Modrinth version list taking 18 seconds. The old ten-second
whole-response deadline canceled these valid responses. Each metadata attempt now
has a thirty-second deadline covering both headers and bounded body reads; the
composition client permits that entire deadline. A failed or timed-out attempt
still falls back in the chosen order, never sending a CurseForge API key to MCIM.
Caller cancellation does not start a fallback. The 8 MiB actual-byte and JSON-depth
limits remain unchanged.

Production resource icons use the same committed source priority. Only validated
official icon URLs may be mapped to MCIM; every redirect is separately checked
against the finite HTTPS icon-origin/path allowlist and the region. Automatic
redirects stay disabled. HTTP failure, invalid content and unsupported redirects
can fall back to the original official icon. Four concurrent producers, the
1 MiB actual-byte limit and encoded-image ownership/cache budgets remain in force.
The standalone icon service retains its official-only default unless a source
policy is supplied. Mirror `/avatars/` requests also require CurseForge admission.

Archive metadata is re-fetched before download. A regional policy captured by the
download service controls source construction and every redirect; neither a mirror
URL in metadata nor an old page preference grants international access. Actual
byte-count/hash verification and non-overwriting publication are unchanged.

## 2026-10-19 packaged credential validation

Release build and formatting checks passed; Services 776, Desktop 356 and all
72-project architecture checks passed. The PAC fixture's initial two-second
deadline failure under concurrent compiler load passed both isolated and serial
full-suite reruns without changing its limits.

Linux NativeAOT and linked-trim Desktop both preserve a synthetic dollar-containing
key exactly, work without the build environment, reject a mismatched expected key
without printing either value, and pass shell/setup startup probes. A build with no
embedded key correctly fails the packaging check even when a runtime API key is
configured. Header fixtures prove environment precedence and official-only key
transmission, including international official requests. Workflow YAML/Bash syntax
and official/fork/tag missing-key guards passed. Publish retains the existing
third-party Jint IL2098 warning and adds no source trim/AOT warnings.
