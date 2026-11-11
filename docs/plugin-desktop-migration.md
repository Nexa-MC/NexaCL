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

There are 40 application routes in addition to the SDK kernel's 90 routes:

| Group | Application mappings |
| --- | --- |
| Files | OpenPackageFile, Stat, Read |
| UI | All 19 current UI operations |
| Platform | Notify, QueryArchitecture |
| Minecraft.Instances | List, ListRoots, ReadIdentity, Select, Watch, Discover |
| Accounts | List, ReadPublic, Select, Watch, Remove |
| Minecraft.Processes | ListSessions, ReadSession, WatchSession, ReadLogRange, Stop |

These adapters call existing typed Host commands and queries. They expose public
account projections, never credentials. Opaque IDs and cursors belong to one
admitted load; stale roster revisions cannot select/remove a replacement account.
Instance reads require the authoritative current root. Incomplete/provisional
state returns unavailable rather than a successful empty list. Discovery and
process termination use the existing Tasks operation ledger and cancellation
lifetime. Logs use the existing bounded, redacted process-output query and
load/session-bound cursors, with explicit gaps.

Instance, account and session watches accept their respective `InstanceSummary`,
`AccountPublic` and `GameSession` schemas. Each publishes an initial projection;
subsequent applied state changes trigger coalesced snapshots in a bounded queue.
A lost target emits a terminal null. Queue overflow requires resynchronization;
cursors supplied when opening a new watch set `ResyncRequired`. Release, revoked
grants and retired loads prevent further delivery. At most 256 business watches
are retained, with 64 queued snapshots per watch.

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

Fixtures record the independent SDK source identity and ZIP SHA-256 beside each
package. Tests use real Wasm over the chunked execution boundary and cover encrypted
cache tampering, approved grants, session replacement, disabled persistence,
contributions, dialogs, model updates, input answers, drawing revisions, shortcuts,
owned subscriptions, process operations, stale account IDs and retirement. The
migration also requires managed Desktop tests, architecture/layout checks and
NativeAOT/trim validation. This document does not turn missing routes into support.

**The remaining business groups are not complete.** Production Market acquisition
and entitlement/key custody also still require product wiring. Java, installation,
launch plans, content/worlds, servers, catalog, settings, themes, backups, downloads,
platform file/clipboard dialogs and other unmapped domain methods remain
unavailable. This change publishes neither a launcher Alpha nor a signed worker.

## 简体中文

本轮新增加密的本地启用记录与离线恢复。开发包仍需开发者模式和明确授权，用户可
选择“保存并启用”或“仅本次加载”。缓存使用当前用户的系统保护器封装 AES-GCM
密钥，恢复前重新验证包与权限，并等待插件引擎完成认证；引擎更换后使用新加载
身份，绝不重放未完成业务。安全模式不恢复插件，停用先删除保存的授权，损坏记录
保留并单独报告。生产 Market 的获取与权益验证尚未接入。

目前共接入 40 条应用路由，叠加 SDK 内核的 90 条。UI 的 19 条当前接口已接入，
包括组件、扩展、替换、命令、快捷键、对话框、模型、生命周期订阅、绘图与输入。
宿主仅开放插件管理页内容扩展点，认证、导航和确认控件不可替换。快捷键只在声明
的页面生效，文本编辑与弹窗期间禁用。绘图只接受有归属的文件引用，并限制尺寸、
命令数及输入队列；宿主确认框出现时停止向插件传递输入。

业务新增实例/账户/进程订阅、实例发现、账户删除、进程列表、详情、日志和停止。
全部使用现有命令与查询，账户不暴露凭据，ID、游标与订阅绑定加载身份；过期账户
列表不能删除替代条目。操作进入现有任务账本，停用或撤权后停止交付数据。

其余业务接口仍未全部接入，具体范围见上面的 Remaining scope。此轮代码与测试
不代表全部 ABI 已实现，也不发布启动器 Alpha。SDK 锁定公开构建输入，普通恢复
无需私有仓库凭据；开发样例和测试夹具另行记录精确来源及摘要。
