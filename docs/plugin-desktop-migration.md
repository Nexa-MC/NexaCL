# Plugin Desktop integration

Desktop composes the pinned public PluginSidecar SDK as a trusted Host kernel.
Verified Wasm executes in the authenticated independent `Nexa.Plugin.Sidecar`
process through the existing numeric binary protocol. The SDK version and public
build-input hashes are locked in `eng/plugins/sdk.lock.json`. Run
`python eng/plugins/prepare.py` before restore. Packages and the exact SDK source
snapshot are downloaded without private-repository credentials; local packages
are rechecked. `--source /path/to/PluginSidecar` builds the pinned source locally.
The Host graph never adds external source project references.

## Offline installation and recovery

Local ZIP admission requires developer mode and explicit approval of the verified
manifest and grants. Users choose **Save and enable** or **Load once**. Saved
admission contains those exact package bytes and grants, encrypted with a random
AES-GCM key wrapped by the current-user OS protector. There is no plaintext disk
fallback. The catalog permits at most 64 records, each bounded to 27 MiB. Files are
written atomically. Invalid records remain available for diagnosis and do not
prevent restoration of other records.

Restoration waits for an authenticated worker, checks package digests and grants
again, orders required dependencies, and obtains fresh load identities. Developer
packages require developer mode on every restore. Safe mode never restores them.
A worker replacement retires its guests before restoring saved admission; pending
actions are not replayed. **Disable** removes saved admission before stopping the
plugin and removes saved dependents stopped by that operation. Storage faults leave
the Host management page accessible. The worker retains its release-signature gate.

## Implemented application mappings

The current SDK catalog has 346 operations. The kernel maps 97 and Desktop adds
211 routes (308 total). A route is still subject to its permission, lifecycle,
platform availability, ownership and domain preconditions. This count is not a
claim that all SDK methods or all providers are implemented.

| Area | Integrated behavior |
| --- | --- |
| Instances and launch | Registered roots, discovery, selection, creation, profile CAS, account-bound launch grants, reviewed launch plans, start/cancel and attempt watches |
| Accounts | Public roster, device-code/form sign-in, eligibility/refresh, selected-profile skin/cape operations; credentials remain in the account owner |
| Installation and Java | Isolated install drafts, version/loader catalogs, plan execution and cancellation; exact instance Java requirements, version-local selection, multi-file managed Java offers and reviewed installation |
| Content and worlds | Content identities, graph/integrity, enabled state, catalog install/update plans, trash restore, imports/exports, transaction rollback, metadata, data packs and world snapshots |
| Catalog and servers | Modrinth/CurseForge queries, revision-bound favorites, dependency previews, saved server list, SRV-aware status, LAN discovery and join |
| Settings and themes | Effective settings, profiles and temporary overlays; reviewed theme activation/preview/revert and option import/export |
| System services | Granted file access, explicit symlink admission, clipboard/window/process operations, bounded HTTP/socket resources, real streaming downloads with pause/resume/cancel and verification |
| UI and diagnostics | All 19 UI operations, owned subscriptions, scoped machine preflight and reviewed existing remediation handlers |

Mutation plans are load-owned, expire, bind reviewed inputs/revisions, require Host
confirmation and are consumed once. Executors revalidate after confirmation and
use the existing domain owners. Resource installs carry the dependency-plan
fingerprint all the way into the native resource owner. Selected exports carry an
exact path allowlist into the exporter. Public IDs and cursors are opaque and
cannot substitute raw Host paths or credentials.

Current domain limits remain explicit: content import accepts inspected local Mod
JARs; catalog install/update plans have one primary item; optional installer files
and world-targeted launch are not yet represented by their adapters. Launch-profile
arguments reject empty values, embedded double quotes and control characters,
because the current launch-policy tokenizer cannot round-trip them. Network
credential references and custom provider callbacks are not implicitly accepted.

Managed Java uses the additive `ListManagedDistributions` and
`PrepareManagedInstall` methods: Mojang distributes a file manifest, not one
archive. The original single-archive contract is unchanged. License URLs, file
count, bytes, registry revision and expiration are retained; the result becomes a
Java runtime only after the owner has verified and registered it.

Downloads publish only to explicitly granted absent targets. Files stream through
the existing download owner, are bounded and hashed before atomic publication.
Pause retains partial bytes and the same task; cancellation/retirement cleans up
staging. Verify checks the current file against its published digest even when the
request omitted a checksum. Operations currently retain the SDK five-minute budget.

Watches have bounded queues and explicit retirement. Gaps require resynchronization;
watch handles belong to their original load and cannot survive reload. Unavailable
or incomplete owner state is an error rather than a successful empty projection.

## UI extension contract

All registrations participate in the existing atomic definition lifecycle.
`nexa:plugins/content` is the public Host extension point; protected navigation,
authentication and confirmation controls cannot be replaced. Contributions use
owned verified NXML components and support append, prepend, hide, replace and
wrap, with stable dependency ordering. Replacement pages must match the view
schema. Commands invoke owned actions with `Empty` input; shortcuts are restricted
to their declared view and do not fire while editing text or displaying a modal.

Pages and dialogs use registered ABI schemas with scalar model fields. Update a
page using its qualified ID as `UIModelUpdate.View`; update a dialog using the
returned dialog entity ID. Expected revisions reject stale writes. Owned text
input drafts are included in explicit page/dialog action submissions. Required
action approval always uses Host-owned controls. Dialog identity and close controls
remain outside guest content; closing a dialog releases its drawing surfaces.

View lifecycle subscriptions return `TextResult` values: `open`, `hidden` (pages),
and `closed` (dialogs). Page watches use the qualified page ID and admitted load ID
as view scope; dialogs use their returned ID/scope. The Host reports visibility.

Drawing surfaces target a unique dynamic slot whose key equals the name after
`:` in the surface's qualified ID. Page scope is the admitted load ID; dialog scope
is its returned view scope. Each surface is at most 2048 by 2048, with at most eight
surfaces/eight million pixels in total and 1024 commands per frame. Submitted
`Surface.Revision` supplies the next revision and `ExpectedRevision` must match the
current revision. A new frame must use a different revision. Image commands accept
only owned BlobRefs from verified package files; decoding is bounded.

Input subscriptions expose only the surface's own visible, enabled entity. Host
confirmations and unrelated overlays block delivery. Pointer coordinates use the
Host viewport coordinate system. Callbacks receive `{event, gap}`; polling returns
an initial `TextResult("attached")` followed by event maps containing `kind`,
`phase`, `x`, `y`, and `text`. Callback delivery coalesces bursts and reports gaps;
text is bounded to 4096 characters. The renderer emits observations only; it never
calls the guest or resolves services.

NXML cannot bind private Host state, execute product commands, or fetch arbitrary
images/files. Assets are bounded to 32 entries/16 MiB per plugin; package blobs are
load-owned and globally bounded. Navigation and notification labels currently use
literal localization keys. Navigation icons and actionable notifications remain
unavailable. A failed extension cannot remove the Host plugin manager.

## Verification and remaining scope

The SDK extensions preserve every existing operation and model. SDK validation
covers managed and NativeAOT runs, 368 current/legacy C calls, 300 model/collection
round trips, event ownership, command-only permissions, atomic migration rollback,
concurrent writers, retired handles and restart persistence. A separate NuGet-only
consumer verifies the packaged runtime. Public build inputs are generated by the
`Stage pinned Plugin SDK inputs` workflow from a hashed exact source snapshot.

Desktop fixtures use actual Wasm and authenticated chunked execution, plus real
file/HTTP/domain owners for the operations under test. The separate native service
suite covers account roster fencing, Java requirement selection, resource install
fingerprints and favorite conflicts. Tests and platform-specific smoke runs must
pass before this migration is declared complete.

Still unmapped operations:

- **Minecraft.Instances**: `PrepareClone`, `CommitClone`, `PrepareDelete`, `CommitDelete`.
- **Minecraft.Install**: `ListRecoveries`, `ResumeRecovery`, `DiscardRecovery`, `RegisterTemplate`, `RegisterLoaderProvider`.
- **Minecraft.Java**: `ListDistributions`, `PrepareInstall`, `RegisterDistributionProvider`.
- **Minecraft.Launch**: `RegisterContribution`, `RegisterPreflight`, `ExplainContribution`.
- **Minecraft.Content**: `ClearTrash`, `RegisterFormatProvider`.
- **Minecraft.Worlds**: `PlanDelete`, `PreviewRestore`, `Restore`.
- **Catalog**: `RegisterCollection`, `RegisterTranslationProvider`.
- **Accounts**: `SignOut`, `RegisterProvider`.
- **Settings**: `RegisterSchema`, `RegisterPage`, `Migrate`.
- **Themes**: `MigrateOptions`, `ListResources`, `ReadResource`, `ReleaseResource`.
- **Backups**: `PreviewRestore`, `Restore`, `Import`. The current restore request names an existing instance, while the storage owner only restores to a new directory. Import has no destination instance. These routes remain unavailable until explicit target contracts are added.
- **Downloads**: `RegisterSourceResolver`, `RegisterTransferProvider`.
- **Tasks**: `Retry`.
- **Minecraft.Diagnostics**: `RegisterRuleProvider`.

Additional provider families and production Market acquisition/key custody remain
separate implementation work. Local developer-package offline recovery already
works, but it does not stand in for production Market entitlement. This migration
publishes SDK build inputs; it does not publish a launcher Alpha.

## 简体中文

目前 SDK 有 346 个操作，内核接入 97 个，Desktop 新增 211 个，共 308 个。
这些路由仍受权限、生命周期、操作系统、归属及业务状态限制，不代表全部接口已完成。
上方逐项列出仍未接入的方法；后续继续实现，不用成功空结果冒充已完成。

本轮接入实例/账户/Java/安装/启动、内容和存档、目录市场、设置、主题、网络下载、
平台操作及诊断。修改类操作绑定用户审阅的输入与版本，在宿主确认后重新检查，
通过原业务服务执行。账户凭据不暴露给插件；ID、计划、游标和订阅归属当前加载。
Java 新增兼容的多文件分发契约，保留许可证、大小和有效期，安装后须通过验证和登记。
事件退订保留旧入口并增加可用的句柄契约，私有存储迁移原子提交且拒绝并发覆盖。

SDK 的托管、NativeAOT、跨语言和独立 NuGet 使用方测试已通过。Desktop 和独立
原生业务测试继续验证本轮接入；详细能力边界和未实现项以上方清单为准。
生产 Market 的获取、权益与密钥托管尚未接入。本次发布的是 SDK 构建依赖，不是启动器 Alpha。

## Custom catalog providers

SDK alpha.9 supplies the typed business-provider callback envelope. Desktop accepts
`nexa:catalog` providers with Search, ReadProject, ListVersions, ReadVersion,
ReadChangelog, ResolveFiles and ResolveDependencies capabilities. Registrations
activate atomically; callbacks run under the query-only boundary. Self-calls and
cyclic provider callback chains are rejected. Returned project
and version references must match the selected provider/request. File names, HTTPS
URLs, hashes, collection sizes and dependencies are validated before exposure.
Search/version cursors are authenticated for the requesting plugin and query.

Custom catalog searches select exactly one provider. Default and mixed built-in
searches retain the canonical resource owner's behavior. Custom-provider favorites
and installation are not mapped by this read adapter. Unsupported registration
policies are rejected explicitly; see the SDK business-provider contract.

## Validation of this integration

The Linux Desktop suite passes 407 tests, including independent catalog provider
callbacks, cursor isolation and forged-result rejection. Architecture checks pass
for 101 projects; generated UI/window wire and repository formatting checks pass.
Independent NativeAOT Minecraft and Accounts tests pass. The NativeAOT window
probe passes with a real Xvfb display, including plugin clipboard text/image round
trips. The managed-owner NativeAOT Desktop passes shell, setup and theme validation;
the separate external-module/native-service Desktop graph compiles without warnings.
This does not replace platform-specific launcher release testing. The SDK alpha.9
CI passes on Linux, Windows and macOS. Production Market delivery and the remaining
38 ABI mappings are still tracked above.


## Instance-scoped backups

The Desktop adapter now maps 11 of the 14 Backups operations through the Setup
owner. Each admitted instance has a separate durable CAS store under the launcher
backup directory. Global storage history is not exposed through this API. Listing
returns load-owned backup IDs and a scope revision; a different plugin load cannot
reuse those IDs. Counts and sizes describe logical files, including duplicate
content referenced by more than one snapshot.

Capture accepts an instance with no external target and empty include/exclude
arrays. It captures the instance directory, not shared assets, shared libraries, Java
or account data. Offline verification checks the snapshot objects; it does not
promise that the restored instance can launch without downloads. It uses a single-use, expiring plan, Host confirmation, an idle check and
the owner's file-stability validation. Capture and prune finish with `Empty`;
query the task's terminal state, then refresh the backup list. Offline verification
hashes stored objects and returns an integrity report, including missing objects.

Retention changes use the revision returned by `Backups.List`. The owner compares
that revision while holding its storage lease, preventing lost updates even when
another caller changes the policy during confirmation. Supported bounds are
1–1,024 copies and 1–3,650 days; `MaximumBytes` must be zero. Policies are stored
per instance and used by explicit prune previews; this integration does not add
an automatic scoped cleanup scheduler. Prune retains the newest snapshot and
cannot delete manifests or objects in another instance's store.

Export writes a **thin JSON manifest**, not an archive of file contents. It
requires a writable file grant, confirmation and a destination that does not
exist. Importing the manifest into another store does not make that backup
usable offline unless its referenced objects are already present. The UI or
plugin must explain this distinction before offering the export.

`PluginBackupTests` exercises real storage through the admitted plugin lifecycle:
confirmation denial, non-idle capture, single-use plans, foreign-load handles,
separate instance/global histories, retention CAS, durable reopen, thin export,
stale prune plans and object-corruption detection. SDK C fixtures use the revised
canonical-key decoder and are also exercised by the Desktop lifecycle tests.

### 简体中文：备份接入范围

Backups 本轮接入 11/14 项。备份按实例独立保存，插件使用宿主发出的不透明 ID，
无法借用其他加载会话的句柄。捕获、清理及策略修改经过宿主确认，并在提交时重新
检查版本与空闲状态。保留策略修改使用 `List` 返回的版本，存储持锁执行比较更新。

当前捕获只支持完整实例；`Target` 必须为空，包含/排除列表必须为空。保留数量为
1–1,024，天数为 1–3,650，最大字节数只接受零；策略用于手动清理预览，尚未启用
实例备份的自动清理。导出的是仅含元数据的 JSON 薄清单，不含文件本体，不能单独
作为可离线恢复的完整备份。导入、恢复预览和恢复仍未接入，需要先补齐明确的目标契约。

Validation for this increment: 408 Desktop tests, the additional concurrent
retention/permission fixture, 915 foundation service tests, architecture gates for
101 projects, and NativeAOT Desktop shell/setup/theme smokes passed. Composition
parity tests now explicitly include the previously added world-edit and process
interaction routes; their old fixed counts had not been updated with the owners.
