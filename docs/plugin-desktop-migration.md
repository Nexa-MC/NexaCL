# Plugin Desktop integration

The Desktop now composes the public PluginSidecar SDK as a trusted Host kernel and
uses the existing authenticated Sidecar command channel to execute Wasm in the
independent `Nexa.Plugin.Sidecar` process. The immutable SDK source/version is in
`eng/plugins/sdk.lock.json`; run `python eng/plugins/prepare.py` before restore.
Only NuGet packages enter the Host project graph, with no external source project
references. The feed helper checks cached package hashes and the source commit.

The protected Plugins navigation entry displays plugin load state, failure codes,
stop controls and admitted pages. Safe mode prevents loading. A storage fault
leaves the management page available. Local unsigned ZIPs require developer mode
and explicit per-load approval of the manifest's requested permissions. Approval
uses already parsed, digest-checked bytes; the selected file is not read again.
The worker itself still requires the existing production `.nsc` release signature.

## Implemented mappings

There are 16 application mappings in addition to the SDK kernel's 90 routes:

- Files: OpenPackageFile issues owned references to verified package NXML assets.
- UI: RegisterPage, RegisterNavigation, PublishModel, UpdateModel, Navigate, Notify.
- Platform: Notify and QueryArchitecture.
- Minecraft.Instances: List, ListRoots, ReadIdentity and Select.
- Accounts: List, ReadPublic and Select.

The instance list reflects the current, authoritative library root. Provisional
or loading state is unavailable, not an empty successful result. Root/file/entity
references are scoped to the admitted plugin load. Account projections contain
public views only; selecting an account checks its roster revision. Page cursors
are bound to the load, collection and snapshot revision.

Pages register atomically with the existing definition lifecycle. NXML comes from
verified package assets, not OS paths. Models use registered ABI schemas and scalar
map fields. `UIModelUpdate.View` is the page's qualified ID string. Navigation labels
appear in the Host plugin manager. This initial adapter accepts literal localization
keys and no navigation icons. It rejects private Host state bindings, product
commands, arbitrary image loading and actionable notifications. An owned
`ui.plugin.action` intent invokes Actions.Invoke using the original plugin's grants;
required confirmations use the Host modal service. Cancellation invalidates approval.
Retirement removes page documents, file handles, model state and pending capabilities.
Session replacement never redirects an existing machine or replays its effects.

The developer test package is built from the pinned independent SDK's
`samples/WasmDesktop`; its source identity and digest are recorded beside the fixture.
It demonstrates a real Wasm callback through chunked execution, page rendering,
trusted action approval, public account reads and permission denial. Negative
coverage includes private-state markup, invalid package paths, retirement, storage
failure, safe mode and cancelled approval. Existing shell/navigation tests,
architecture rules, formatting and NativeAOT/trim checks close the migration.

## Remaining scope

This is the first working Desktop integration, not full ABI product coverage.
Production Market acquisition/key custody, persisted enablement and automatic offline
startup still need product wiring. UI contributions, replacements, shortcuts, drawing,
arbitrary dialog answers and the remaining domain methods remain unavailable. This
change does not publish a signed worker or a launcher Alpha release. Other unavailable
capabilities are never advertised as successful placeholder implementations.

## 简体中文

本次接入通过锁定版本的 SDK 包连接真实 Desktop：插件管理、页面展示、模型更新、
页面导航、通知、操作确认以及部分实例/账户业务已落地。新增 16 条宿主映射，
叠加 SDK 运行库的 90 条已实现路由。账户接口不暴露凭据，文件和实体引用绑定
插件加载实例；停用后旧页面和权限失效。执行仍走现有认证 Sidecar 二进制通道。

开发模式可以选择 ZIP，确认插件身份和请求权限后加载；安全模式禁用加载，
存储错误保留管理页。正式工作进程仍须通过发行签名验证。运行前先执行
`python eng/plugins/prepare.py`，随后正常 restore/build。

尚未完成生产 Market、密钥保管、启用状态持久化及自动离线启动；高级 UI 扩展与
其余业务路由继续保持不可用。本次代码接入不等于全部 ABI 已实现，也没有发布新 Alpha。
