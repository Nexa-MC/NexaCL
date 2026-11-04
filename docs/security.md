# Trust, credentials, and regional policy

English | [简体中文](security.zh-CN.md)

Launcher updates and executable Sidecars must pass the defined source, pinned publisher, and content checks. The release identity for `GPG-PUBLIC-KEY.asc` is `5701218D69B531E1A7ED35BB6E31F5974A273AEE`. A trusted download location does not establish a content signature; unsigned historical archives cannot serve as trusted differential baselines.

Credentials, tokens, private keys, and API client identities must not enter source, test logs, UI state, or exportable diagnostics. Actions credentials, release private keys, and local CLI credentials are separate. The update repository holds public keys only and cannot resign source releases.

Downloads, imports, extraction, themes, and updates have bounded byte/count/path budgets. Reject traversal, link escapes, replacement races, duplicate entries, and cross-platform ambiguous paths. Protected updates retain admitted object permissions and use the installed helper; ordinary UI cannot reconstruct privileged targets from arbitrary paths.

Account eligibility checks apply worldwide. Mainland China does not provide Minecraft game downloads: pack installation reuses a complete local vanilla installation and directs users to the official Minecraft Launcher when it is missing. UI, network, and plugin changes must preserve these service rules.

Themes are restricted styling resources, with a different admission policy from executable updates. Extensions pass capability, session, and resource admission. Trust changes must update contracts, implementations, and regression evidence together.
