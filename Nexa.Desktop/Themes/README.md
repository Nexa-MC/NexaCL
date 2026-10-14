# NTP/1 author themes

The three directories are independently loadable directory themes and are copied
to Desktop build and publication outputs as external files. `BuiltIn` preserves
the host's current bright/dark appearance; its style file intentionally adds no
overrides. The session uses the built-in compatibility path rather than applying
an empty third-party program. Its options retain existing appearance/music values.

`Minimal` demonstrates typed color/range parameters, a token and matched title/
button selectors. `Advanced` demonstrates all nine option editor kinds, same-source
resources, responsive fractional grid, visual ordering, overlay positioning,
transforms, fonts, shadow, platform/mode conditions and finite keyframes. Its
selectors match actual host keys. None replaces NXML or adds commands. Parameters
with external file/directory paths still require source-bound grants.

Advanced media is disabled by default. Enable its music/video options to prepare
the packaged sample resources through normal consumers. Native materials, codec
availability and CJK font fallback remain host capabilities; the sample Latin
DejaVu font does not claim to contain all Unicode glyphs. Its separate license is
included. The example SVGs and synthesized media are CC0 fixtures, independent of
NexaCL's user-supplied product brand artwork.

From the repository root:

```sh
python eng/themes/pack.py Nexa.Desktop/Themes/Minimal --output /tmp/Minimal.ntp
python eng/themes/pack.py Nexa.Desktop/Themes/Advanced --output /tmp/Advanced.ntp
python eng/themes/pack.py Nexa.Desktop/Themes --verify-published PUBLISH_DIR/Themes
```

The packager is deterministic, unsigned and rejects links and ambiguous paths.
It does not activate or grant permissions. The runtime service validates the
complete strict manifest and content independently. Author changes should be
checked with the shipped `Nexa.Desktop --validate-themes` smoke and the compiler/
service tests. Invalid development edits retain the last valid theme.

Protocol and lifecycle rules: [NTP/1](../../docs/xsr/ntp-v1.md),
[NSS/1](../../docs/xsr/nss-v1.md),
[resources](../../docs/xsr/theme-resource-security.md).
