# Versioning

Product versions use these exact forms:

| Channel | Canonical form |
| --- | --- |
| Stable | 2.0.0 |
| Alpha | 2.0.0.alpha.N |
| Beta | 2.0.0.beta.N |
| CI | 2.0.0.ci.ffffff |

The CI suffix is a lowercase six-digit hexadecimal commit identifier. `eng/xsr/Xsr.Version.props`, release tooling, and tests validate sequences and encoding. Tags prefix the canonical version with `v`. Alternative directory names, display strings, or SemVer rewrites cannot replace the formal product identity.

For MSBuild and NuGet only, CI builds use `2.0.0-ci.gffffff`. The `g` makes the
commit identifier nonnumeric: a real commit such as `051029` would otherwise
produce `2.0.0-ci.051029`, which NuGet rejects because numeric prerelease
identifiers cannot have leading zeroes. The product identity remains
`2.0.0.ci.051029`; display text, update metadata, and release tags keep that form.
`eng/xsr/test_version.py` restores a project graph with numeric and hexadecimal
commit identifiers and checks both identities.

`eng/xsr/Xsr.Version.props` defines the source default, independently of GitHub Release publication status. The official entry point supplies the product version to Host and UI, whose native image versions are fixed independently. Engine image versions, the Sidecar version, and shared NuGet versions are managed separately from Alpha sequence numbers.

Each public version identifies immutable bytes. Source changes require a new release version rather than overwriting signed packages. Comparison and channel selection are implemented in `eng/release/version.py` and update services.
