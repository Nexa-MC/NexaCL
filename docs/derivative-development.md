# Derivative development

English | [简体中文](derivative-development.zh-CN.md)

This guide describes building, modifying, and distributing an independent NexaCL derivative. It does not grant access to upstream credentials or publisher identities.

## Licensing and provenance

Read the root Apache License 2.0 and every dependency's licenses, copyrights, and notices. Identify modifications and maintain your own source and distribution provenance. Third-party resources, plugins, and branding have independent permissions; verify them before redistribution.

## Establish a working baseline

Follow [building](building.md) to verify managed development and a target-platform NativeAOT distribution before customization. Find owners through the [project index](modules.md). Branding does not require renaming every project, namespace, and ABI. Assembly or binary-protocol renames are compatibility work that needs its own plan.

Product assets and UI reside in `src/app/Nexa.Desktop/Assets`, `Ui`, and `Themes`. Artwork embedding is defined in `eng/build/ArtworkResources.props`. Project names, logical resources, installation identities, protocol associations, and display names have different consumers; verify each affected path.

## External services and credentials

Configure OAuth clients, API keys, client identities, and remote endpoints under your own authorization. Source and workflows define the required inputs. Do not copy local credentials or assume upstream services permit shared derivative use. Missing credentials must produce an explicit unavailable/error state rather than hidden authentication failures.

## Independent distribution and trust

The upstream public key authenticates upstream releases only. An independent publisher needs its own private-key management, embedded public key, pinned fingerprint, source/update repositories, version/channel policy, and verification evidence. Review release scripts, client validation, protected helpers, and Sidecar verification together when changing trust. Replacing one key file does not update every fingerprint or authorize old installations to trust a new publisher.

The launcher, Sidecar, NuGet packages, and OS code signatures have separate identities and versions. Choose installation identifiers, application protocols, and update sources that do not overwrite upstream installations. Configure and verify Windows Authenticode, Apple Developer ID, and notarization independently; OpenPGP does not replace OS publisher trust.

Follow the current [security and regional rules](security.md). Complete checks for the platforms you actually support and identify unsupported or unverified platforms. Do not bypass account eligibility, download, signature, or protected-update verification.

## Working with upstream

Use the [contribution guide](../CONTRIBUTER.md) for suitable upstream fixes. Keep customization points focused, and exclude local absolute paths, secrets, and workstation-dependent project references. Published source revisions and release bytes must be traceable to each other.
