# 平台集成与资源设置

基线：Alpha 14。设置在下一次启动时捕获，不追改正在运行的 JVM。显式配置失败会取消本次启动并清理所拥有的进程，不会假装已经应用。默认值保持操作系统策略。

## CPU 与进程资源

设置键用于辨识配置契约；用户应在设置界面编辑，不能把下表当作可直接导入的 JSON 文件。实例可覆盖全局值；重置表示移除覆盖。

| 键 | 默认与取值 | 平台／含义 |
|---|---|---|
| `game.cpu-policy` | `system`；`performance`、`cache`、`numa` | Windows 11+／Linux；从已探测拓扑选择性能核心、缓存域或 NUMA 域；未知拓扑不猜测 |
| `game.cpu-sets` | 空；如 `0-3,6` | Windows 11+ 的 CPU Set ID／Linux 的 CPU 编号；机器相关，不随设置导出 |
| `game.process-quality` | `default`；`efficiency`、`background` | Windows 10 1709+ EcoQoS／Linux nice；默认不调用原生调度 |
| `game.cpu-quota-percent` | 0 关闭；最大 102400 | Linux 用户 systemd/cgroup v2；100 表示一个逻辑 CPU 的额度，200 表示两个 |
| `game.process-memory-limit-mib` | 0 关闭；最大 16777216 MiB | Linux 进程组内存限额，不等于 JVM `-Xmx` |
| `game.io-read-limit-mib` / `game.io-write-limit-mib` | 0 关闭；最大 1048576 MiB/s | Linux 针对有效游戏目录设备的 I/O 限额 |
| `game.gamemode` | false | Linux 已安装 GameMode 守护进程；启动申请、退出释放，不安装或修改系统服务 |

例：只希望限制后台实例占用两个 CPU，可保持 CPU 策略为 system，设置 quota=200；不要同时猜测 CPU ID 或把游戏堆上限当进程硬限额。配额依赖用户控制器授权，权限不足属于可诊断的启动失败。设置过低的内存硬限额可能导致系统终止游戏。

非默认调度／配额要求直接启动自有 JVM Host。自定义 wrapper 可以提前分叉，无法可靠证明限制施加到实际 JVM，因此冲突配置在创建进程前拒绝。Windows CPU Sets 不冒充整棵进程树的硬配额。不存在通用 OS 全局 GameBoost 开关。

源码：`SettingsPolicySchema.cs`、`SettingsCatalog.cs`、`MinecraftLaunchCoordinator.Request.cs`、`Nexa.Platform.Runtime` 的调度与配额适配器。

## 自动后台策略

操作系统内存压力会缩小共享缓存预算；严重压力通过 DEACTIVATE/ACTIVATE 协作暂停／恢复 Sidecar 激活。它不强制冻结进程，也不保证第三方立即释放 RSS。手动停止的包不会被自动重启。

磁盘实测延迟、已知 GPU／统一内存预算、计费网络、电池与热状态影响后台任务准入。未知状态保持未知；这些策略不启动 Minecraft 内存估算器，不自动改变 JVM 内存，也不把无硬件计数器视作零消耗。

## Windows

任务栏显示下载进度；截图操作调用原生分享面板。已有托盘、Jump Lists、通知继续使用操作系统适配器。启动时核对 Host、Java 和 jvm.dll 的架构；ARM64 系统可运行相互匹配的 x64 Host/Java。游戏附带的多架构原生 DLL 只作有界诊断，不根据未使用的 DLL 阻止启动。

## macOS

截图支持 Quick Look，文件复制优先尝试 APFS clone，失败采用经过校验的复制。macOS 13+ 随包安装 `Contents/Helpers/NexaCL Actions.app`，由 LaunchServices 注册，向 Spotlight 与 App Intents 提供启动、资源和设置三个固定入口。快捷指令可组合这些动作；Siri 可用性取决于系统设置。

辅助程序只打开 `nexacl://launch`、`nexacl://resources`、`nexacl://settings`，不索引存档内容或账户数据。macOS 12 可以继续使用主程序，但不提供该辅助能力。开发目录没有打包辅助程序时不会声称已完成注册。

## Linux / Steam Deck

窗口菜单的“添加到 Steam”或“添加 Steam / Gamescope 入口”要求先退出 Steam，手动选择 `Steam/userdata/<数字 ID>/config`。目录须有 `localconfig.vdf`。注册保留其他快捷方式、写入原文件备份并原子替换；拒绝符号链接、损坏 VDF、开发期 dotnet 启动路径和仍在运行的 Steam。

Gamescope 入口要求 `/usr/bin/gamescope` 已安装，包围启动器窗口，不把 wrapper 当自有 JVM。Steam Input 由 Steam 对普通非 Steam 游戏入口提供，启动器不会读取 Steam 账户凭据或替用户配置控制器。

## 备份快照

备份服务在已有内容存储锁内尝试捕获只读快照，随后仍逐文件哈希、验证并产生正常备份清单。恢复写入新目录，不回滚活跃文件系统。

- Btrfs：来源必须是完整子卷且无后代子卷；系统已有工具及权限。
- ZFS：来源必须精确匹配一个已挂载 dataset，且无后代 dataset；需现有委派权限与可读快照目录。
- 不支持、工具缺失、权限不足时使用经过校验的常规文件捕获；产品不会执行 sudo、挂载文件系统或创建 dataset。
- 捕获前写入 `.lease` 所有权记录。成功／失败／取消后清理本次快照；下次捕获可恢复清理中断遗留，身份或权限不确定时保留标记，不删除无关目录。

CI 已在隔离 Btrfs 文件系统验证不可变捕获及中断清理；ZFS、Steam、Siri 的真实用户环境仍依赖设备与权限，不由云端测试替代。
