# Launcher feature parity implementation

The baseline is Alpha 21 (`3c1862e4e`). This work implements the user's explicit
feature list independently; external launchers are read-only behavior/format
references and are not implementation dependencies. Internet lobbies and tunnels,
remote homepage subscriptions and remote server-pack subscriptions are outside
this explicit list.

| Capability | Implementation and acceptance |
| --- | --- |
| LAN discovery | Bounded UDP multicast listen, deduplicated endpoints, server UI import, cancellation on exit |
| HMCL/MCBBS/MultiMC imports | Detect manifests and optional wrapper directories, normalize versions/loaders/files, existing staged installer, malformed/conflicting archive rejection |
| Export formats | MRPACK/ZIP plus CurseForge, MCBBS (HMCL-compatible) and MultiMC, round-trip import tests |
| Launcher and Java bundles | Explicit portable closure/runtime selection, exclude credentials, reproducible paths and usable start entry |
| Optional files | Stable per-file IDs in preview, independent selection in install/update, persisted selections |
| Instance scripts | Windows/POSIX quoting, selected root and instance, current launcher identity refresh, no credentials in scripts |
| Content previews | Archive inventory, metadata, bounded text/image preview, schematic metadata and embedded preview |
| NBT tree | Read-only typed tree, compressed/raw Java NBT, bounded depth/arrays/nodes, paged navigation |
| Offline customization | Optional UUID at creation, local skin selection, in-game local texture/authlib lifecycle |
| Nide8 compatibility | Dedicated server ID/account flow and compatible launch preparation; no credentials in UI state |

Each capability must include its Desktop entry, managed and native registration,
binary contract coverage, focused failure/cancellation tests and architecture
checks before it is marked complete. No new alpha release is implied by this
implementation request. Check actual validation output instead of inferring
success from a button, a DTO or an upstream class name.

## 使用入口

- 实例设置 → **服务器**：进入页面自动扫描 LAN，保留“发现局域网游戏”按钮用于立即重扫；离开页面停止监听。发现项可直接加入或加入服务器列表。
- 拖入 `.zip` / `.mrpack`：识别 Modrinth、CurseForge、旧 HMCL、MCBBS 和 MultiMC 清单，支持一层外包装目录。MCBBS 的 `force` 是更新策略，不视为可选标志。自定义 MultiMC patches/JarMods、MCBBS 自定义运行库会明确拒绝，防止安装时丢失组件。
- 整合包安装的可选文件入口逐项选择包含或跳过，最后确认。在线目录安装也先检查清单并绑定所选归档的 SHA-256。更新预览显示逐文件勾选项，修改选择后需重新预览。
- 实例设置 → **整合包与导出**（先打开实例开发模式）：支持 MRPACK、游戏目录 ZIP、CurseForge、MCBBS（HMCL 兼容）和 MultiMC。可保存内容筛选配置。
- 游戏目录 ZIP 可填写完整原生启动器发布目录和 Java 根目录。仅收集启动器发布文件及所选 Java，运行时内部文件链接展开为普通文件；越界或循环链接拒绝。保留法律文件和执行权限。便携入口用相对游戏目录启动，并设置随包 Java 的 `JAVA_HOME`。首次运行仍需完成启动器设置及账户登录。目标平台/架构由所选发布包和 Java 决定，不包含跨平台下载。
- 同一导出页可导出 `.cmd` / `.bat` / `.sh` 实例启动脚本。脚本调用选定的启动器，通过二进制安全编码的本地启动链接选择实例，使用正常账户刷新与启动流程；不携带访问令牌。已有启动器进程和首次设置后的转交均保留目标。
- 实例设置 → **内容与 NBT 查看**：通用只读查看入口，可输入本地文件路径；资源包、光影包和蓝图的详情页也提供查看器。支持目录/ZIP 文件列表、UTF-8 文本、PNG、原始/gzip/zlib 的 Java 大端 NBT。NBT 使用数字子节点路径，重名标签不会混淆；数组分页。投影显示元数据、调色板信息及文件内嵌的 ARGB 缩略图，不执行光影或游戏脚本。
- 添加离线档案：可指定 UUID、本地 PNG 皮肤/披风和 Slim 模型。材质随受保护账户档案保存，公开账户列表不携带材质字节；账户服务在存取时复制字节。游戏通过仅监听 loopback、随机路径、带签名的本地材质端点读取外观，使用正常校验的 authlib-injector。该端点随启动器的 Minecraft 服务存活，退出启动器后不再服务后续材质请求；已下载的游戏材质仍由游戏缓存。
- 添加账户 → **统一通行证 Nide8**：输入 32 位服务器 ID、用户名和密码。认证端点固定为官方 `auth.mc-user.com:233/<id>/authserver`；刷新会话保持角色身份；启动时使用官方代理及其 HTTPS 元数据指定的校验和，代理参数为服务器 ID。
- 设置 → **Java**、实例 → **游戏设置**：Java 选择方式提供“自动／手动／版本内 Java”。版本内策略查找当前版本文件夹下的 `java`、`jre`、`jdk`、`runtime`（支持嵌套发行目录）或直接的 `bin/java`；验证实际版本并应用游戏兼容范围与停用设置。它不借用全局或其他版本的 Java，也不自动下载替代运行时；缺失或不兼容时停止启动并说明原因。全局、实例和启动配置的覆盖规则保持一致，移动实例后按新目录重新查找。

## 验证记录

验证结果以本次命令输出为准。相关测试位于 `LauncherFeatureParityTests.cs`、
`FeatureParityPageTests.cs`、账户表单测试，以及独立的 Accounts/Minecraft 原生调用者测试。
完整方案构建、业务/UI 回归、架构检查、协议生成校验和原生 AOT 检查共同覆盖正式调用链。

本次 Linux 验证：Release 全方案与原生服务模式 Desktop 构建均无警告、无错误；
业务测试 876 项、Desktop 测试 393 项通过，101 个项目的架构与目录检查通过。
Accounts、Minecraft 与 Host 的独立 NativeAOT 调用者验证通过，包含新增操作的
真实字节边界、准入校验及退出清理。协议与账户路由生成校验通过。
实际 Avalonia 窗口在 1040 / 810 两种宽度验证页面、滚动、输入保留及格式选项边界。
Windows 上的防火墙交互、实际游戏外观和不同发行版 Java 仍需平台实机验收。

版本内 Java 增补验证：59 项 Java/离线依赖测试、全局与实例选择及窄窗口/滚动
回归、101 个项目架构检查通过。独立 NativeAOT 调用者验证了原生模块中的
版本内策略、实际 Java 探测和缺失运行时隔离；协议生成指纹保持不变。
源码格式使用 CI 的完整 `IDE0055 IMPORTS` 检查命令验证。
