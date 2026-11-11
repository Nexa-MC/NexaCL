[English](architecture.md) | 简体中文

# 架构与依赖

XSR 将执行协调、业务能力和语义呈现分离。产品保留 NativeAOT，正式部署由同进程原生模块与独立 Sidecar 进程组成。

账户文件升级兼容由 Accounts 负责。旧 schema-1 文档缺少可选字段或字段为 null 时，在 JSON 读取入口恢复文本的声明默认值，以及离线皮肤、披风的空数组，再进入服务校验。读取加密档案不改写原文件；超限或非法数据保留原件，发布账户不可用状态，不应中断启动。原生账户异常通过现有日志桥记录类型、状态和堆栈，不记录可能包含凭据的原始异常消息；原生退役失败保留回调，并只报告一次原因。

```text
Bootstrap -> 原生 Host -> UI 主线程
                   |-> Accounts / Network / Minecraft / Updates
UI -> 状态副本与意图 -> 类型化客户端 -> 原生异步端口
业务 -> 契约、Domain、Core 与显式平台接口
Host <-> Sidecar Protocol / Transport <-> 独立插件进程
```

Command 表达动作请求，接受不等于执行完成；Query 提供可取消的一次性结果；State 表达带版本的当前事实；Event 表达瞬时事实。Renderer 读取状态并产生意图，不在渲染路径查询服务或直接启动 Sidecar。UI 控制器不使用具体业务实现。事件排序作用域与资源生命周期作用域是不同机制。

基础依赖向内：Domain 依赖 Core；业务契约依赖必要的低层契约和 XSR 抽象；业务实现不依赖 Desktop、Avalonia、Renderer 后端或服务定位器。Composition 在边缘绑定实现、Runtime 和平台端口。平台进程创建、凭据存储、探测和重启通过接口进入业务。Sidecar.Protocol 不依赖产品 Core、Contracts 或 XSR；Transport 仅依赖 Protocol。

当前允许的直接项目引用由 `eng/architecture/project-references.json` 锁定，实际项目位置由同目录 `projects.json` 记录。架构检查按项目身份验证引用、循环、程序集元数据和语义边界。目录调整不允许扩大依赖清单来掩盖错误，也不能只靠命名或接口声明证明实现隔离。

跨 NativeAOT 模块只传递版本化 C ABI 的整数、长度受限字节、缓冲区和不透明句柄。禁止传递 CLR 对象、Task、Stream、托管委托和异常。模块拥有自己的对象，显式定义回调、取消、完成、释放和退出顺序。详细发布边界见 [原生模块](native-modules.zh-CN.md)。

修改公开契约或所有权前先修改本页或对应专题说明，随后更新实现、消费方和契约验证。外部插件进程的实现由独立仓库维护，本仓库不通过相邻工作区源码引用获得它。

Windows 更新助手的目录链准入由 Platform.Runtime 负责，所有者仍限定为 SYSTEM、Administrators 和 TrustedInstaller。对于已解析的条件允许 ACE，仅在访问掩码只包含已知只读／执行权限时接受，兼容稀疏 MSIX 注册添加的权限，不依赖条件表达式判断安全。条件写入授权、未知权限位及不支持的 ACE 类型继续拒绝；校验持有祖先目录句柄，不修补权限。

Windows 内存整理由 Common 服务拥有调度、阈值、冷却与状态，Platform.Runtime 通过显式端口采样物理内存并逐进程调用 Windows 工作集 API；Composition 注入当前游戏进程保护集合。Desktop 通过命令与状态显示主页信息卡片，不直接枚举或操作进程。实现独立编写，不复制、链接或分发 Mem Reduct 代码。

普通内存整理不结束或暂停进程，不调用无法排除游戏的全局工作集／待机缓存清理。保护已登记的游戏 PID 及其后代、Java／Bedrock 进程、前台应用和系统关键进程；使用进程创建时间及持有句柄防止 PID 复用。无法确定身份或没有权限的进程跳过。工作集减少量与系统可用内存变化分别记录，均不表示释放了相同数量的私有提交。自动模式默认关闭、默认阈值 80%（可调 50–95%）、每 5 秒采样，触发后至少冷却 2 分钟；手动与自动任务互斥，退出时取消并等待工作结束。

深度清理默认关闭。开启后每次清理由独立的同版本启动器 worker 通过 UAC 请求管理员权限；该 worker 在常规启动、窗口与业务初始化之前分流。一次性命名管道只允许创建用户及本机管理员连接，并校验双方 PID，仅接受有界游戏排除 PID 集合并返回计数与内存测量，不接受程序路径、脚本或任意系统命令。游戏排除在提权后仍然生效；父进程退出、断管或超时取消后续清理。取消 UAC 或权限失败不报告成功，也不提权主 UI。worker 完成本次清理即退出，不安装常驻服务。

## Launcher feature parity additions

The current implementation scope and acceptance cases are tracked in
[launcher-feature-parity.md](launcher-feature-parity.zh-CN.md). Minecraft.Install owns
archive format normalization and per-file optional selection; Minecraft.Management
owns bounded LAN discovery, NBT/content inspection and portable export preparation.
Minecraft.Launch owns executable launch planning. Accounts owns offline identity,
offline texture data and Nide8 authentication facts; Desktop owns explicit user
consent, file pickers, tool views and renderer state projection. Native clients and
Composition expose the same typed contracts in managed and NativeAOT deployments.
New native operations use unused numeric IDs and generated binary DTO codecs; the
private service schema fingerprint changes with the coherent module build. Existing
operation IDs and public Sidecar protocol/SDK contracts remain unchanged.

局域网发现是可取消、有界的 Minecraft 多播监听。服务器页显示期间自动发现，
将唯一端点与已保存服务器并列显示，不写入 `servers.dat`。
此工作区不提供通用文件／NBT 查看器、独立内容查看入口或服务器要求编辑表单。
内部归档／NBT 解析继续执行条目数、字节数、递归和文本预算。

用户选择的本地文件或归档，在打开、计算哈希和预览前，通过 Core 的有界路径解析器
跟随有效文件链接或目录联接。提交时仍重新验证来源，归档条目和写入目标的链接校验
保持严格。命名启动配置与临时覆盖控件仅在实例开发者模式显示。

Exports stage output before atomic publication and never include account/session
stores. Launcher-mediated launch scripts refresh identity through the launcher
rather than embedding account tokens. Embedded launcher/runtime selection must be
explicit, bounded and reproducible; failures cannot produce a successful export.

Online modpack inspection uses the existing Resources owner before installation;
the confirmed archive SHA-256 and optional-file IDs travel in typed private
contracts and are rechecked for installation and retry. Offline texture bytes use
bounded binary fields rather than protocol strings. Accounts copies these bytes
on ingress/egress; Minecraft.Launch owns the random-path, loopback-only texture
endpoint, releases it with its runtime, and never redirects online identities to
that endpoint. The generic inspection tool is read-only and the instance launch
link preserves its target through the existing single-instance and first-run
handoffs.

Java policy supports an explicit version-local strategy. `java.runtime` retains its
existing Auto/absolute-path representation and additionally accepts the reserved
Custom value `@instance`; it resolves against the launched version directory, so
global/profile choices remain portable. Minecraft.Java owns bounded local runtime
discovery and compatibility selection through the existing inspection port. This
strategy never scans global runtimes or silently downloads a replacement. Desktop
exposes the strategy as “版本内 Java” without showing the stored token. Native DTO
shapes and operation IDs remain unchanged.

## Complete launcher parity (2026-11-04)

The completion checklist is [parity-completion.md](parity-completion.md). Each row
requires a reachable Desktop entry, authoritative service behavior, managed/native
parity and failure/cancellation evidence. Public upstream repositories are behavior
references only; implementation is independent.

Minecraft.Java owns bounded whole-instance discovery and explicit relative/major
preferences; Launch intersects identity-specific Nide8 requirements before probing.
Management owns region NBT decoding, resource-pack options transactions, schematic
facts and script artifacts. Install owns complete normalized modpack recipes,
reviewed launch settings, nested portable archives and server pack updates. Accounts
owns edits to existing offline appearances and their typed private texture sources.
Resources owns verified server downloads and remote document retrieval. Desktop
projects these tools, local/remote home documents and searchable help through XSR.
Remote documents are declarative data, never arbitrary XAML/code execution. Online
sessions own bounded child-process lifetimes and explicit room credentials; provider
installation must verify bytes and expose actual readiness. Native boundaries use
the existing generated numeric/binary mechanisms. Platform coverage is admitted only
after compiler/runtime and native dependency evidence, never by adding untested RIDs.

## 多实例同时启动

不同规范实例目录可以并发准备和启动，每个任务独立拥有取消、Java 决策、进度和
身份。同一实例重复启动会被拒绝；共享可写游戏目录继续受执行与覆盖层租约保护。
原生调用端必须保留启动任务标识。

## Windows 深度系统清理

用户明确开启并通过 UAC 授权后，Windows 深度清理增加八项系统操作：系统工作集、
文件缓存、卷缓存刷新、已修改页、待机页、低优先级待机页、注册表协调与物理页合并。普通模式
仍逐进程排除游戏。系统级操作无法排除单独游戏，因此只要存在游戏或排除身份就
跳过，每项操作前重新检查，并报告真实原生状态与可用内存变化。未支持或被拒绝的
操作不能计作成功。实现仅依据操作系统接口契约独立编写；没有 Windows 测量证据
前，不宣称实际效果已与 Mem Reduct 持平。

「游戏启动前自动深度清理」默认关闭，独立于手动与阈值清理设置，在启动预检通过后、
创建 JVM 前执行。Host 负责 UAC 与内存清理；原生 Minecraft 模块通过数字操作 2403
回调 Host，并传递本次启动的取消令牌。并发启动共享重叠的清理尝试；取消等待中的
启动不影响其他启动正在使用的清理。拒绝 UAC 或清理失败会记录状态并继续启动游戏，
明确取消启动仍阻止创建游戏进程。其他已运行游戏继续受系统级操作保护。

实例管理仅允许经 Core 验证目标为对应 `/private` 目录且目标祖先无链接的 macOS 系统根别名 `/var`、`/tmp`、`/etc`。自定义目录链接仍被拒绝；内容读取与修改采用相同准入规则。

## 插件 Desktop 接入

Desktop 持有插件准入、权限、原子注册和业务状态；独立 PluginSidecar 仅执行已验证的无导入 Wasm。继续使用现有认证二进制 Sidecar 通道，分块传输大包；连接失败后不自动重放操作。SDK 从锁定提交生成的公开 NuGet 包消费，源码不加入宿主项目图。插件管理页、权限和操作确认由宿主控制，安全模式禁用执行；存储故障保留管理入口。页面只使用包内引用和模型数据，不能绑定私有宿主状态或发送产品命令。具体映射与验收见 [插件接入](plugin-desktop-migration.md)。
