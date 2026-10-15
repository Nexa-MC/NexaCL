# Resource mirror transport and regional ownership

Resource mirrors remain a mainland-China feature, selected by
`RegionalPolicy.IsMainlandChina`, independently of display language. Outside that
region metadata, fingerprints, archive bytes and icon bytes use permitted official
origins only. MCIM translations return no result before reading their cache or
performing HTTP. Country and effective source policy remain part of metadata and
icon cache identity; a saved mainland preference does not authorize a mirror in
another region. The network provider switches apply to every attempted origin.

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
