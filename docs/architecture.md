# 架构与依赖

XSR 将执行协调、业务能力和语义呈现分离。产品保留 NativeAOT，正式部署由同进程原生模块与独立 Sidecar 进程组成。

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

跨 NativeAOT 模块只传递版本化 C ABI 的整数、长度受限字节、缓冲区和不透明句柄。禁止传递 CLR 对象、Task、Stream、托管委托和异常。模块拥有自己的对象，显式定义回调、取消、完成、释放和退出顺序。详细发布边界见 [原生模块](native-modules.md)。

修改公开契约或所有权前先修改本页或对应专题说明，随后更新实现、消费方和契约验证。外部插件进程的实现由独立仓库维护，本仓库不通过相邻工作区源码引用获得它。

Windows 内存整理由 Common 服务拥有调度、阈值、冷却与状态，Platform.Runtime 通过显式端口采样物理内存并逐进程调用 Windows 工作集 API；Composition 注入当前游戏进程保护集合。Desktop 通过命令与状态显示主页信息卡片，不直接枚举或操作进程。实现独立编写，不复制、链接或分发 Mem Reduct 代码。

内存整理不结束或暂停进程，不调用无法排除游戏的全局工作集／待机缓存清理。保护已登记的游戏 PID 及其后代、Java／Bedrock 进程、前台应用和系统关键进程；使用进程创建时间及持有句柄防止 PID 复用。无法确定身份或没有权限的进程跳过。工作集减少量与系统可用内存变化分别记录，均不表示释放了相同数量的私有提交。自动模式默认关闭、默认阈值 80%（可调 50–95%）、每 5 秒采样，触发后至少冷却 2 分钟；手动与自动任务互斥，退出时取消并等待工作结束。

深度清理默认关闭。开启后每次清理由独立的同版本启动器 worker 通过 UAC 请求管理员权限；该 worker 在常规启动、窗口与业务初始化之前分流。一次性命名管道只允许创建用户及本机管理员连接，并校验双方 PID，仅接受有界游戏排除 PID 集合并返回计数与内存测量，不接受程序路径、脚本或任意系统命令。游戏排除在提权后仍然生效；父进程退出、断管或超时取消后续清理。取消 UAC 或权限失败不报告成功，也不提权主 UI。worker 完成本次清理即退出，不安装常驻服务。

## Launcher feature parity additions

The current implementation scope and acceptance cases are tracked in
[launcher-feature-parity.md](launcher-feature-parity.md). Minecraft.Install owns
archive format normalization and per-file optional selection; Minecraft.Management
owns bounded LAN discovery, NBT/content inspection and portable export preparation.
Minecraft.Launch owns executable launch planning. Accounts owns offline identity,
offline texture data and Nide8 authentication facts; Desktop owns explicit user
consent, file pickers, tool views and renderer state projection. Native clients and
Composition expose the same typed contracts in managed and NativeAOT deployments.
New native operations use unused numeric IDs and generated binary DTO codecs; the
private service schema fingerprint changes with the coherent module build. Existing
operation IDs and public Sidecar protocol/SDK contracts remain unchanged.

LAN discovery is an explicit, cancellable, bounded multicast listen for Minecraft
LAN advertisements; it does not create an Internet lobby or tunnel. Archive/NBT
inspection uses entry, byte, recursion and text budgets and treats payloads as data.
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
