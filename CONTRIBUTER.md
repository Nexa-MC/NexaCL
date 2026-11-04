# Contributing to NexaCL

English | [简体中文](CONTRIBUTER.zh-CN.md)

Contributions should solve a concrete problem and retain the repository's service boundaries, compatibility contracts, and verification gates. Begin with [README](README.md) and [building](docs/building.md).

## Understand ownership

Find the responsible module through the [project index](docs/modules.md). Record boundary and compatibility decisions in [architecture](docs/architecture.md) before changing contracts. Services must remain independent of Desktop, Avalonia, and UI objects. Native ABI and Sidecar boundaries carry bounded bytes and opaque handles, never CLR objects, Tasks, Streams, or managed exceptions.

When adding or moving a project, update `NexaCL.slnx`, `eng/architecture/projects.json`, engineering scripts, and documentation. Change `project-references.json` only for a reviewed ownership change; do not broaden its baseline to hide an invalid dependency.

## Verify the behavior

Run focused tests before the required broader checks in [testing](docs/testing.md). Most test projects are executable suites: an empty `dotnet test` result is not evidence that their cases ran.

Cover the actual trigger, resulting behavior, failure, and cancellation. UI changes should account for light/dark themes, narrow windows, keyboard/controller interaction, and retained drafts. Separate platform model tests, native CI, and real-device acceptance. Native or repository-layout changes also require architecture, instrumentation, AOT, and relevant packaging evidence.

Regenerate changed protocols and run their `--check` commands. Do not edit generated codecs manually. Run Python regressions for release tooling and validate documentation links and commands. State which checks were not run and why.

## Prepare a reviewable change

Keep a commit focused on a concrete outcome. Use the existing conventional style, such as `fix(updates): ...` or `refactor(repo): ...`. Explain the trigger, final behavior, verification, and any contract, recovery, or data-format implications.

Review the diff for build outputs, `bin/`, `obj/`, `artifacts/`, caches, temporary previews, IDE settings, and secrets. Retain required fixtures, lock files, licenses, and notices. Legacy sources may inform behavior; they must not become copied implementation, restored project graphs, or workstation-relative dependencies.

## Documentation and licensing

English is the canonical documentation language. Main guides also provide Simplified Chinese siblings named `*.zh-CN.md`; update both when their behavior changes. Documentation describes verifiable current behavior and distinguishes pending work from completed features. Historical release notes belong to GitHub Releases.

Follow [Apache License 2.0](LICENSE), dependency licenses, and applicable copyright/notice obligations. Agent-assisted contributions also follow [AGENTS.md](AGENTS.md).
