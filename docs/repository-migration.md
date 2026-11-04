# Source-directory migration

Sources formerly spread across the root and `native/` moved into `src/`, grouped by capability and protocol. Tests follow feature ownership; tools distinguish compilers, probes, and benchmarks. Current documentation was rebuilt, while Git history and public Releases retain historical material.

The migration preserves the existing 101 C# project identities, assemblies, public contracts, native exports, and eleven-module distribution. It does not rewrite user configuration, installation layout, or published Alpha 19 bytes. The dependency baseline moved to `eng/architecture/project-references.json` without expanding permissions; `projects.json` records locations.

Adaptations remove directory-depth instrumentation assumptions, update compiler references/source mapping, and align protocol generation, NativeAOT builds, linked assets, tests, CI, and solution groups with disk layout.

Acceptance requires consistent project/dependency inventories, valid references, unchanged generated code, complete build/architecture checks, actual UI instrumentation coverage, and six-platform native/package CI. See [testing](testing.md); actual executed logs and workflows establish results.
