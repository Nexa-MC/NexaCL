# Instance modpack development mode

Each instance owns `instance.development-mode`, default false. Only the Instance settings layer may change it; global, launch-profile and temporary values do not participate. The setting is local, non-exportable, and excluded from recovery settings so restoring a baseline cannot switch the workspace mode.

Desktop projects the effective setting through the existing sealed settings query/command routes. Normal mode hides modpack export, dependency graph, snapshot/rollback management, the file workspace and instance diagnostics. Everyday content, servers and removed-content recovery remain accessible. The underlying management services stay available; this is presentation policy, not an authorization boundary.

The overview uses the existing on/off control. A committed mode change immediately rebuilds navigation. Disabling the current advanced page returns to overview and retires its pending reads. Changing instances never inherits the previous instance mode.

Successful normal exits continue to capture the latest recovery baseline in both modes. Effective history retention is `instance.development-mode OR recovery.keep-history`. Development mode never rewrites the manual retention preference. Disabling it does not immediately delete snapshots; existing retention cleanup applies at the next successful capture. Crash analysis and rollback commands remain available in normal mode.

Validation covers per-instance/default policy, rejected non-instance writes, recovery exclusion and actual history retention, plus navigation changes and normal content visibility.

Verified on Windows: Services 732 passed; Desktop 266 passed; architecture checks passed for 70 projects. NativeAOT Services publication executed 10 focused proxy, development-mode and recovery tests successfully. PAC publication retains the documented upstream Jint IL2098 annotation warning. C# changes were checked with dotnet format whitespace. Linux/macOS execution remains a CI check, not local evidence.
