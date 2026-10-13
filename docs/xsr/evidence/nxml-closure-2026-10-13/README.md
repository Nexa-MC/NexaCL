# NXML closure native previews — 2026-10-13

Linux Avalonia software-rendered **actual windows**, captured with
`RenderTargetBitmap(window)`. The controllers use typed fixtures and isolated
temporary directories. These captures exercise UI behavior; they do not certify
live provider responses, game execution, or Windows DWM chrome.

Both light and dark runs completed all 15 stages at 1040 and 810 DIP: 60 captures.
Each selected PNG has its adjacent JSON interaction manifest. `capture-index.json`
records the manifests and SHA-256 digests of all 60 captures. The complete images
were generated in the cloud execution workspace; this directory keeps seven
representative images.

| Page / interaction | Verified preview |
| --- | --- |
| Owned virtual content list after scrolling | [Light, 810](nxml-settings-light-instance-content-scrolled-810.png) |
| Screenshot modal with image and actions | [Dark, 810](nxml-settings-dark-instance-screenshot-modal-810.png) |
| Server status alongside an unsaved editor | [Light, 1040](nxml-settings-light-instance-server-editor-1040.png) |
| Modpack export draft and category choice | [Dark, 810](nxml-settings-dark-instance-export-editor-810.png) |
| Launch home with controlled offline profile | [Light, 1040](nxml-settings-light-launch-home-1040.png) |
| Java catalog selection and install-name draft | [Dark, 810](nxml-settings-dark-java-install-810.png) |
| Bedrock official handoff and platform gating | [Light, 810](nxml-settings-light-bedrock-install-810.png) |

Additional captured stages cover global catalog arguments, protected proxy input,
advanced tools, content search, screenshot crop controls, world snapshots,
recovery history and recovery paging. The harness asserts retained entity, draft,
caret, focus, scroll and ownership facts before capture and verifies normal native
teardown.

Reproduce after building `tests/Nexa.Desktop.Tests` in Release, with a working X11
display and session bus:

```sh
dotnet tests/Nexa.Desktop.Tests/bin/Release/net10.0/Nexa.Desktop.Tests.dll --native-nxml-settings-preview light /tmp/nxml-previews
dotnet tests/Nexa.Desktop.Tests/bin/Release/net10.0/Nexa.Desktop.Tests.dll --native-nxml-settings-preview dark /tmp/nxml-previews
```

Final managed, architecture, formatter, NativeAOT and full-trim outcomes, commands,
log digests and executable digests are recorded in [validation.json](validation.json).
[external-assets.json](external-assets.json) records all 694 external asset digests;
both published packages match it exactly. The publication logs retain the existing
Jint `IL2098` warning. The first trimmed publication had an MSBuild child-node
failure; the final single-node publication and both runtime checks passed.
