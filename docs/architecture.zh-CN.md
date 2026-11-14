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

### Beta 边界（迁移目标）

插件包准入、SDK 协商、权限、生命周期、离线恢复、业务适配和管理界面全部由
PluginSidecar 承担。宿主不引用插件 SDK，不维护插件专用业务路由。所有 Sidecar
使用同一套版本化、异步二进制宿主 ABI；缺失能力以通用宿主能力补齐，不新增插件转发入口。
账户、实例、设置和平台操作仍由宿主所属服务管理，PluginSidecar 再约束每个插件的权限。
账户凭据不得作为通用状态导出。

PluginSidecar 是可选的已签名发布组件。组件缺失、准入失败、启动失败或退出均不得阻止
启动器启动及核心功能运行；安全模式不执行扩展。发布验证同时覆盖组件存在和缺失的情况。
ABI 100 Beta 基线冻结，后续插件契约扩展使用不可变 EXP_ID。此目标取代下文的进程内
所有权；完成迁移和验证后才发布 Beta。

Desktop 持有插件准入、权限、原子注册和业务状态；独立 PluginSidecar 仅执行已验证的无导入 Wasm。继续使用现有认证二进制 Sidecar 通道，分块传输大包；连接失败后不自动重放操作。SDK 从锁定提交生成的公开 NuGet 包消费，源码不加入宿主项目图。自动插桩检查仍覆盖全部宿主自有程序集；四个明确列出的 `Nexa.Plugin` SDK 程序集只有在输出依赖清单标记为 NuGet 包时才归为外部依赖，不能仅凭名称前缀跳过。插件管理页、权限和操作确认由宿主控制，安全模式禁用执行；存储故障保留管理入口。页面只使用包内引用和模型数据，不能绑定私有宿主状态或发送产品命令。具体映射与验收见 [插件接入](plugin-desktop-migration.md)。

### 插件离线准入与界面延续

Composition 持有独立的有界加密安装目录，仅保存用户明确批准的包字节与权限。每条记录使用 AES-GCM 加密，密钥由现有当前用户系统保护器封装，不写入明文暂存包。恢复时重新校验，等待认证工作进程并取得新的加载身份；开发包仅在开发模式恢复，安全模式禁止恢复。用户停用先移除持久准入，再停止运行；损坏记录隔离报告并保留。高级 UI 注册仍需原子发布、所有者校验，不能替换受保护宿主控件；业务接口走既有类型化命令和公开状态。

NXML 层负责扩展组合与挂载：已编译文档只能组合已编译片段并挂载到明确的 Dynamic
插槽。Desktop 传入文档和扩展方式，不构造展示 IR 或直接修改输入组件。绘图实体
来自外部宿主模板，插件展示与产品 UI 遵守同一套所有权检查。

## ABI 100 业务接口的完整接入

原有 346 项操作保持编号、请求和结果模型不变；新增 12 项兼容操作，用于表达明确的恢复目标、实例范围、回收站预览、版本化设置和 Provider 消费入口。宿主支持清单由公共 SDK 的 `contracts/host-support.json` 记录，并固定到经过验证的宿主提交。

实例、世界和备份操作继续由对应业务服务拥有。恢复目标必须来自调用方持有的可写文件授权，或已获准的世界与名称组合；预览、确认、版本检查和实际执行使用同一目标。插件不能把任意路径字符串转化为访问权限。原有备份导入形式可用已存在的备份标识确定实例范围，新增形式支持向首次使用的实例导入。

Provider 的声明通过暂存、原子发布和卸载撤销流程生效，并配有实际消费者。只读回调返回经过验证的类型化数据；下载落盘、账户凭据、进程启动和文件发布仍由宿主完成。插件提供的内存与窗口参数仅作为当次启动的临时设置应用，不写回用户配置。卸载会使所属资源、回调和依赖它们的计划失效。

任务重试只允许显式可重放的工作，或由插件自己拥有的失败、取消任务；结果未知的副作用不能自动重放。持久设置迁移保留版本和并发写入检查。原生部署使用生成的数字路由及二进制契约，保持业务所有者与 Desktop 的边界。

通用 Sidecar 业务 DTO 超过原有单字段 65,535 字节上限时，须协商
`LargeGeneratedDtos`（位 256）。仅 codec 6 可使用，完整值最多 8 MiB。
原有消息编号、基础类型限制和字段含义不变：字段 6 表示总长度，字段 7 起
按顺序承载后续分块。缺块、多块和乱序均拒绝，未协商的连接不得发送或接收
大值。此扩展属于通用传输协议，不增加或更改冻结的插件 ABI 100 操作。

### 通用宿主效果目录

基础业务目录（发现操作 255）及其 DTO 指纹保持不变。独立的效果目录
（发现操作 511，操作 512–522）提供不含凭据的账户启动资格与刷新、Java
检查／选择／需求、电源状态、组合预检报告和公开设置定义／校验结果，以及游戏进程停止和世界快照列表。
各目录独立冻结版本和指纹，新增目录不使基础目录客户端失效。接口不含
插件或 SDK 类型。账户刷新在宿主内部读取并检查档案版本，不返回令牌
或启动身份；能力对象和设置校验委托也留在宿主，只传输普通结果数据。

### 会话拥有的动态界面

独立的界面目录（发现操作 767、版本查询 768、替换命令 769）允许已准入的
Sidecar 在激活后替换整组 NXML 文档。宿主使用通信连接确认调用会话，请求
不能指定其他包或会话。初始版本为 0，每次成功替换递增一次；请求必须携带
预期版本，以拒绝覆盖并发更新。确认消息丢失后应先查询版本，不能直接重试。

候选文档沿用已有编译器、页面协商和容量限制，全部编译成功后一次性发布。
编译失败保留原文档及版本；成功替换使旧展示标识失效，并取消其未完成命令。
空文档组撤回当前会话的全部界面。停用会话时撤回展示，重新激活时以新的展示
标识恢复最后提交的文档；断开连接则撤销全部资源。宿主不接收插件 SDK 模型、
插件身份或权限声明。插件专用的模型翻译和准入由 Sidecar 负责，当前仍在迁移。

视图状态目录独立使用发现操作 1023 和操作 1024–1026，不修改原界面目录。
源码采用已有二进制 NXML 文档格式，因此单个文件可超过通用字符串编解码器的
32 KiB 限制。文档模型以严格 UTF-8 传输，先复制和校验，再编译及原子发布。
模型更新保留展示标识、导航和控件身份；源码替换仍撤销旧标识。两者共享同一
界面版本，防止并发覆盖。模型在停用后重新激活时恢复；会话最多保留 4 MiB
模型文本，单文档最多 4,096 项，单值最多 512 KiB。模型只覆盖所属文档的
同名数据，不修改会话镜像或其他文档的数据。

### 会话拥有的表单提交

可选能力 `UiForms`（位 512）允许使用生成 DTO 编码 6 的 NXML 命令接收表单；
原有标量命令保持原格式。生成的二进制信封带模式指纹，包含动作参数和按
Ordinal 顺序排列的输入草稿，不包含插件 SDK 类型、身份或权限判断。

Desktop 在 UI 线程采集调用文档所属展示标识的输入，排除其他页面、宿主控件和
其他展示标识拥有的嵌套挂载。重复或无名称字段使整个提交失败，不静默合并。
每次最多 128 个字段，键最多 256 个字符，单值最多 512 KiB，编码后最多 4 MiB。
大信封还需协商 `LargeGeneratedDtos`。旧展示标识不能提交，撤销展示时取消未完成
的命令。转换为插件动作及模型的逻辑留在 Sidecar。

框架无关的渲染模型、NXML 编译器和 NXML 运行时作为共享包提供，依赖仅涉及
Core、XSR 契约及状态，不引入 Desktop、Avalonia、宿主服务或插件 SDK。
编译器包随附标准组件与样式，构建和发布时复制到 `Ui/Components`、`Ui/Styles`。
Sidecar 使用同一编译器检查插件页面，并自行执行插件专用限制；共享编译器
不会授予宿主 UI 访问权限，也不会绕过准入。

### 会话所有的页面控制

独立页面控制目录（发现 1279，操作 1280–1282）查询实际页面可见状态，并打开或关闭调用 Sidecar 自己发布的普通页面。请求携带文档索引和预期展示版本，不能指定其他会话或全局页面名称；引导页仍由宿主控制。

Desktop 在 UI 线程处理请求，队列上限为全局 64、每会话 8。独立状态脉冲唤醒渲染，不会重新发布过期的页面快照。请求五秒超时；取消、展示退役和 UI 分离均释放排队容量。执行前重新校验会话所有权与版本。锁只保护快照，不在锁内执行 UI 或取消回调。关闭只影响调用方当前可见页面，并恢复仍然有效的返回页。服务依赖异步端口，协议不携带插件 SDK 类型；本次扩展不更改 guest ABI，也不意味着发布 SDK Beta。

### 会话所有的确认请求

独立确认目录（发现 1535，查询 1536）接受纯文本标题和 UTF-8 详情，返回用户的明确选择。宿主将请求绑定到已认证的 Sidecar 会话，在宿主标题中展示包身份，并提供确认与取消控件。插件动作、权限和确认策略留在 Sidecar。

请求不能替换已有对话框；UI 不可用或已有模态框时返回拒绝。取消、会话退役、UI 分离及两分钟超时均撤销对应对话框。反馈服务通过线程安全快照交给渲染器，IPC 线程不修改 UI 实体。外部文字按原样显示，不进入翻译或标记解析；详情最多 256 KiB UTF-8，标题最多 256 字符。

## 通用 Sidecar 系统交互

独立发现的 Shell 目录为已认证的原生 Sidecar 会话提供 HTTPS 导航、显示目录和
显示器查询。Desktop 通过异步委托向 Composition 绑定平台操作；Composition
不引用渲染器或插件 SDK。绑定拥有取消生命周期，UI 解绑会取消尚未完成的请求，
旧回调不能继续使用。执行前验证输入与会话，成功应答前再次验证调用方和绑定。
结果不确定时不得自动重试。仅接受不含用户信息的 HTTPS 地址和已存在的绝对目录
路径；显示器结果在编码前检查数量和字段。插件权限、文件授权与确认由 Sidecar
负责，已有目录和插件 ABI 保持兼容。
