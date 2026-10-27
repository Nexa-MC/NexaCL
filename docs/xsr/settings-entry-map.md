# Settings catalog source map

Alpha 14：从 `SettingsCatalog.json` 导出的 529 个位置。此表是原始声明，不是当前 UI；实际可用性还要经过 `SettingsCatalog.ResolveAvailability`、平台和消费方检查，页面只显示有操作意义的条目。目录含结构、事实、旧位置和保留规划，禁止把 raw Available 当作实现或显示证据。

| ID | Scope / page | Section / label | Kind | Key | Raw availability | Developer |
|---|---|---|---|---|---|---|
| global.general.054298dae5c3 | global/general | 语言与区域 / 界面语言 | Setting | general.language | Available | False |
| global.general.79751f64326b | global/general | 语言与区域 / 格式区域 | Setting | general.region | Available | False |
| global.general.22b6c81eb644 | global/general | 语言与区域 / 语言与区域 | Group | — | Available | False |
| global.general.7abc4abe665d | global/general | 语言与区域 / 跟随系统 | Choice | — | Available | False |
| global.general.c47c7f1742cc | global/general | 启动与主页 / 启动时页面 | Setting | general.startup-page | Available | False |
| global.general.308624e46ea7 | global/general | 启动与主页 / 单实例运行 | Setting | general.single-instance | Available | False |
| global.general.13a5ea9202aa | global/general | 启动与主页 / 开机启动 | Setting | general.autostart | Available | False |
| global.general.65aedbdb1011 | global/general | 窗口与托盘 / 启用系统托盘 | Setting | general.tray | Available | False |
| global.general.289fddae96dd | global/general | 窗口与托盘 / 最小化到托盘 | Setting | general.minimize-to-tray | Available | False |
| global.general.79e1995bccb4 | global/general | 窗口与托盘 / 关闭时最小化到托盘 | Setting | general.close-to-tray | Available | False |
| global.general.38dfb6023189 | global/general | 通知与交互 / 通知 | Setting | general.native-notifications | Available | False |
| global.general.7fb116563a13 | global/general | 通知与交互 / 通知点击操作 | Setting | general.notification-actions | Available | False |
| global.general.d3abe2892c94 | global/general | 通知与交互 / 启动提示 | Setting | general.launch-hints | Available | False |
| global.general.c8c6e2568cd3 | global/general | 通知与交互 / 剪贴板内容检测 | Setting | general.clipboard-detection | Available | False |
| global.general.51d0bd32a3c1 | global/general | 系统集成 / 文件关联 | Setting | general.file-association | Available | False |
| global.general.7354b59155bf | global/general | 系统集成 / 任务栏跳转列表 | Setting | general.jump-list | Available | False |
| global.general.b046e15c6647 | global/general | 系统集成 / 系统集成 | Group | — | Available | False |
| global.general.7e2629a1945d | global/general | 系统集成 / nexacl:// URI | State | — | Available | False |
| global.general.be7df665521d | global/general | 启动器行为 / 启动器行为 | Group | — | Available | False |
| global.general.f5c04528a6dd | global/general | 交互 / 交互 | Group | — | Available | False |
| global.general.116b4caf0595 | global/general | 交互 / 操作确认 | State | — | Available | False |
| global.appearance.d7137f424ce4 | global/appearance | 主题与配色 / 系统 / 浅色 / 深色 | Setting | appearance.theme-mode | Available | False |
| global.appearance.theme-pack | global/appearance | 主题与配色 / 主题包 | Setting | appearance.theme-pack-id | Available | False |
| global.appearance.launcher-icon | global/appearance | 图标 / 启动器图标 | Setting | appearance.launcher-icon-mode | Available | False |
| global.appearance.5113a7179b1f | global/appearance | 动画与动效 / 启用动画 | Setting | appearance.animations-disabled | Available | False |
| global.appearance.8acdc9767f4b | global/appearance | 动画与动效 / 减少动态效果 | Setting | appearance.reduced-motion | Available | False |
| global.advanced.2981dd4c9f21 | global/appearance | 显示与兼容 / 硬件加速 | Setting | appearance.hardware-acceleration-disabled | Available | False |
| global.appearance.0078a26f82d8 | global/appearance | 主题 / 主题 | Group | — | Available | False |
| global.appearance.d999201ab2ad | global/appearance | UI Renderer 信息 / UI Renderer 信息 | State | — | Available | True |
| global.appearance.6357798f5b55 | global/appearance | Layout / Paint 调试 / Layout / Paint 调试 | State | — | Available | True |
| global.appearance.d44cc0e413d3 | global/appearance | Animation Scheduler / Animation Scheduler | State | — | Available | True |
| global.appearance.d1957652c919 | global/appearance | UI 性能指标 / UI 性能指标 | State | — | Available | True |
| global.game.a015362a3b3b | global/game | 资源与性能 / 默认内存 | Setting | game.memory | Available | False |
| global.game.56d6a5a2ff73 | global/game | 资源与性能 / GPU / Renderer | Setting | game.gpu-preference | Available | False |
| global.game.980b1535d671 | global/game | 资源与性能 / 进程优先级 | Setting | game.process-priority | Available | False |
| global.game.process-memory-limit-mib | global/game | 进程资源配额 / 进程树内存硬上限（0 不限制，超限可能终止游戏） | Setting | game.process-memory-limit-mib | Available | False |
| global.game.io-read-limit-mib | global/game | 进程资源配额 / 游戏磁盘读取限速（0 不限制） | Setting | game.io-read-limit-mib | Available | False |
| global.game.io-write-limit-mib | global/game | 进程资源配额 / 游戏磁盘写入限速（0 不限制） | Setting | game.io-write-limit-mib | Available | False |
| global.game.gamemode | global/game | 进程资源配额 / 使用 Linux GameMode（需要已安装服务） | Setting | game.gamemode | Available | False |
| global.game.cpu-policy | global/game | 进程资源配额 / 自动处理器拓扑策略 | Setting | game.cpu-policy | Available | False |
| global.game.cpu-quota-percent | global/game | 进程资源配额 / CPU 配额（0 不限制，100 为一个 CPU） | Setting | game.cpu-quota-percent | Available | False |
| global.game.cpu-sets | global/game | 资源与性能 / 逻辑处理器集合（留空使用系统默认） | Setting | game.cpu-sets | Available | False |
| global.game.process-quality | global/game | 资源与性能 / 进程调度模式 | Setting | game.process-quality | Available | False |
| global.game.736cc8decfb4 | global/game | 游戏窗口 / 默认窗口模式 | Setting | game.window-mode | Available | False |
| global.game.e50f31850521 | global/game | 游戏窗口 / 默认宽度 | Setting | game.width | Available | False |
| global.game.be97685d3414 | global/game | 游戏窗口 / 默认高度 | Setting | game.height | Available | False |
| global.game.2d098079e2b7 | global/game | 游戏窗口 / 默认窗口标题 | Setting | game.title | Available | False |
| global.game.1cf65ca0a095 | global/game | 游戏窗口 / 游戏窗口 | Group | — | NotImplemented | False |
| global.game.d8816b9ba07a | global/game | 启动行为 / Minecraft 启动后 Nexa 行为 | Setting | game.launcher-visibility | Available | False |
| global.game.default-server | global/game | 启动行为 / 默认服务器 | Setting | game.server | Available | False |
| global.game.a0a1c7543f09 | global/game | 启动行为 / 启动提示 | Setting | general.launch-hints | Available | False |
| global.game.f98b60511c9c | global/game | 启动行为 / 自动修复 | Setting | game.auto-repair | Available | False |
| global.game.519289dc7549 | global/game | 启动行为 / 启动行为 | Group | — | NotImplemented | False |
| global.game.eecd18a1df09 | global/game | 启动行为 / 游戏退出后 Nexa 行为 | Setting | — | Available | False |
| global.game.2fc72ad5a99f | global/game | 启动行为 / Game Resource Handoff | Setting | — | Available | False |
| global.network.install-inherit-vanilla | global/game | 版本与隔离 / 作为原版的附属版本安装 | Setting | install.inherit-vanilla | Available | False |
| global.game.e24bf83fc5d5 | global/game | 版本与隔离 / 默认实例隔离 | Setting | game.default-isolation | Available | False |
| global.game.7e8a400a0114 | global/game | 启动命令与参数 / JVM Arguments | Setting | game.jvm | Available | False |
| global.game.f5ccfc2a0f7f | global/game | 启动命令与参数 / Game Arguments | Setting | game.arguments | Available | False |
| global.game.f77ab330f4f5 | global/game | 启动命令与参数 / Wrapper Command | Setting | game.wrapper | Available | False |
| global.game.a6b1948627df | global/game | 启动命令与参数 / Pre-launch Command | Setting | game.pre-launch | Available | False |
| global.game.pre-launch-wait | global/game | 启动命令与参数 / 等待启动前命令完成 | Setting | game.pre-launch-wait | Available | False |
| global.game.preflight-memory-analysis | global/game | 启动预检 / 内存分析 | Setting | — | TemporarilyUnavailable | False |
| global.game.546d4a3ef466 | global/game | 默认资源配置 / 默认资源配置 | Group | — | NotImplemented | False |
| global.game.42cb17c26e70 | global/game | 默认资源配置 / 内存策略 | State | — | Available | False |
| global.game.2a9b6563042d | global/game | 实例 / 实例 | Group | — | NotImplemented | False |
| global.game.b176ae552c3b | global/game | 实例 / 默认游戏目录策略 | Setting | — | NotImplemented | False |
| global.game.8238cd399639 | global/game | 实例 / Assets 验证策略 | Setting | — | NotImplemented | False |
| global.game.e9780c3f4978 | global/game | 智能启动 / 智能启动 | Group | — | NotImplemented | False |
| global.game.603d5db0bd30 | global/game | 智能启动 / Launch Preflight | State | — | Available | False |
| global.game.dc34f9e868e6 | global/game | 智能启动 / 资源预算检查 | State | — | Available | False |
| global.game.c7fd5c2fa7a3 | global/game | 智能启动 / Compatibility Check | State | — | Available | False |
| global.game.b670d35098d3 | global/game | 智能启动 / 自动补全游戏文件与需确认的修复计划 | State | — | Available | False |
| global.game.34b8302ef006 | global/game | 默认高级启动 / 默认高级启动 | Group | — | NotImplemented | False |
| global.game.b4337614dfd8 | global/game | 默认 Renderer Backend / 默认 Renderer Backend | Setting | game.renderer | Available | True |
| global.game.63ad38145ea7 | global/game | Native Compatibility / Native Compatibility | Setting | — | NotImplemented | True |
| global.game.37621820582f | global/game | System GLFW / System GLFW | Setting | game.system-glfw | Available | True |
| global.game.05daf2cf55e0 | global/game | Force X11 on Wayland / Force X11 on Wayland | Setting | — | PlatformUnsupported | True |
| global.game.80f3021332cf | global/game | LWJGL Unsafe Agent / LWJGL Unsafe Agent | Setting | — | PlatformUnsupported | True |
| global.game.722e1a55163d | global/game | Effective Launch Template / Effective Launch Template | Setting | — | NotImplemented | True |
| global.java.c9d16badc618 | global/java | 自动策略 / 首选 Java | Setting | java.runtime | Available | False |
| global.java.365ec24bd51d | global/java | 自动策略 / 自动安装缺失 Java | Setting | java.auto-install | Available | False |
| global.java.4d7b6b78879f | global/java | 自动策略 / 首选发行版 | Setting | java.vendor | Available | False |
| global.java.5c5cc626dde8 | global/java | 自动策略 / 自动策略 | Group | — | NotImplemented | False |
| global.java.118291439447 | global/java | 自动策略 / 自动选择兼容 Java | State | — | Available | False |
| global.java.4defce197f9a | global/java | 自动策略 / Java 兼容性检查 | State | — | Available | False |
| global.java.dc83e72f0f27 | global/java | 已安装 Java / 已安装 Java | Group | — | Available | False |
| global.java.083d3d8fb21c | global/java | 已安装 Java / Runtime List | Group | — | Available | False |
| global.java.10b4985ac691 | global/java | 已安装 Java / Version | State | — | Available | False |
| global.java.a2b8901854e7 | global/java | 已安装 Java / Vendor | State | — | Available | False |
| global.java.3c069c85eeed | global/java | 已安装 Java / Architecture | State | — | Available | False |
| global.java.ab5a57e0ac74 | global/java | 已安装 Java / Path | State | — | Available | False |
| global.java.d0186e8f64ba | global/java | 已安装 Java / Status | State | — | Available | False |
| global.java.cfa054115fb1 | global/java | Java 管理 / Java 管理 | Group | — | NotImplemented | False |
| global.java.98b7b2d91fdf | global/java | Java 管理 / 扫描本机 Java | Action | — | Available | False |
| global.java.c3e7c58d3f01 | global/java | Java 管理 / 添加自定义 Java | Action | — | Available | False |
| global.java.27cb7147cd93 | global/java | Java 管理 / 下载 Java | Action | — | Available | False |
| global.java.7342ba416866 | global/java | Java 管理 / 验证 Java | Action | — | Available | False |
| global.java.6c6d4d76291f | global/java | Java 管理 / 删除托管 Java | Action | — | Available | False |
| global.java.dc1399b7a8ba | global/java | 版本 / 版本 | Setting | — | Available | False |
| global.java.4c869c02a251 | global/java | Vendor / Vendor | State | — | Available | False |
| global.java.8236e49ebcbe | global/java | Architecture / Architecture | State | — | Available | False |
| global.java.1f8d2f784a60 | global/java | Path / Path | State | — | Available | False |
| global.java.dc5ff6fcd886 | global/java | Compatibility / Compatibility | State | — | Available | False |
| global.java.5a06021a0456 | global/java | Managed / External / Managed / External | State | — | Available | False |
| global.java.0f6d5bc77e9b | global/java | 设为首选 / 设为首选 | Action | — | Available | False |
| global.java.38e1f8df3aa0 | global/java | 验证 / 验证 | State | — | Available | False |
| global.java.aef617d61a94 | global/java | 打开目录 / 打开目录 | Action | — | Available | False |
| global.java.64589a6632ae | global/java | 删除 / 删除 | Action | — | Available | False |
| global.java.90551624db0d | global/java | JVM Runtime Probe / JVM Runtime Probe | Setting | — | Available | True |
| global.java.d503e51b6737 | global/java | Runtime Capabilities / Runtime Capabilities | State | — | Available | True |
| global.java.2fb41ba03e43 | global/java | Detected Modules / Detected Modules | State | — | Available | True |
| global.java.ea5ffef60a9e | global/java | Raw Runtime Information / Raw Runtime Information | State | — | Available | True |
| global.network.a1296e090a28 | global/network | 下载与重试 / 游戏文件下载源 | Setting | network.game-source | Available | False |
| global.network.48ce222474c2 | global/network | 下载与重试 / 游戏文件并发数 | Setting | network.file-concurrency | Available | False |
| global.network.0c6b42629eb3 | global/network | 下载与重试 / 下载速度限制 | Setting | network.bandwidth-kib | Available | False |
| global.network.metered-download | global/network | 下载与重试 / 计费网络后台下载策略 | Setting | network.metered-download | Available | False |
| global.network.b6a315a0224d | global/network | 下载与重试 / 后台下载 | Setting | network.background-download | Available | False |
| global.network.ad7bf51a43d1 | global/network | 下载与重试 / 游戏文件失败重试 | Setting | network.file-retry | Available | False |
| global.network.47c8ce074515 | global/network | 下载与重试 / 自动安装依赖 | Setting | network.auto-install-dependencies | Available | False |
| global.network.271cd2983917 | global/network | 内容来源 / 来源优先级 | Setting | network.resource-source | Available | False |
| global.network.8762cc888a7a | global/network | 内容来源 / Modrinth | Setting | network.provider-modrinth | Available | False |
| global.network.8db0316e6811 | global/network | 内容来源 / CurseForge | Setting | network.provider-curseforge | Available | False |
| global.network.fb11a5bd57dd | global/network | 内容来源 / Mojang | Setting | network.provider-official | Available | False |
| global.network.4f68f579c79b | global/network | 内容来源 / 镜像源 | Setting | network.provider-mirror | Available | False |
| global.network.221b5fccc8ba | global/network | 内容来源 / 内容来源 | Group | — | Available | False |
| global.network.b2d990613011 | global/network | 连接与解析 / 首选 IP 栈 | Setting | network.ip-stack | Available | False |
| global.network.1c40ec59e81e | global/network | 连接与解析 / DNS over HTTPS | Setting | network.doh | Available | False |
| global.network.d963a6e0b46d | global/network | 连接与解析 / 网络自动检测 | Setting | network.auto-diagnose | Available | False |
| global.network.proxy-mode | global/network | 代理 / 代理模式 | Setting | network.proxy-mode | Available | False |
| global.network.92a9bfe92c4c | global/network | 代理 / 地址 | Setting | network.proxy-address | Available | False |
| global.network.fc76daf8059d | global/network | 代理 / 用户名 | Setting | network.proxy-user | Available | False |
| global.network.663af85795c8 | global/network | 代理 / 密码 | Setting | network.proxy-password | Available | False |
| global.network.85b1be372b57 | global/network | 代理 / 代理 | Group | — | Available | False |
| global.network.d0dc18a116ac | global/network | 代理 / 不使用 | Choice | — | Available | False |
| global.network.0952d66c5180 | global/network | 代理 / 跟随系统 | Choice | — | Available | False |
| global.network.d05c9f0df6ef | global/network | 代理 / 自定义 | Group | — | Available | False |
| global.network.4ca4fbf70bee | global/network | 下载 / 下载 | Group | — | Available | False |
| global.network.abf85d83507b | global/network | 网络 / 网络 | Group | — | Available | False |
| global.network.1bb75025c08e | global/network | 网络 / 网络故障诊断 | Action | — | Available | False |
| global.network.4099bfafe908 | global/network | Endpoint 状态 / Endpoint 状态 | State | — | Available | True |
| global.network.10e1dbcf8399 | global/network | Network Probe / Network Probe | Action | — | Available | True |
| global.network.6be5d646627f | global/network | Mirror Resolution / Mirror Resolution | State | — | Available | True |
| global.network.a6c7666f328b | global/network | Retry Trace / Retry Trace | Setting | network.trace | Available | True |
| global.network.fe0101aca467 | global/network | Download Diagnostics / Download Diagnostics | State | — | Available | True |
| global.storage.0f96f92a2c85 | global/storage | 数据位置 / 数据位置 | Group | — | Available | False |
| global.storage.f0e1f8e49c73 | global/storage | 数据位置 / 游戏目录 | State | — | Available | False |
| global.storage.515eb03d238d | global/storage | 数据位置 / 版本目录 | State | — | Available | False |
| global.storage.52f5b9975060 | global/storage | 数据位置 / 默认实例位置 | Setting | — | NotImplemented | False |
| global.storage.245e94a31c8f | global/storage | 数据位置 / 托管 Java 目录 | State | — | Available | False |
| global.storage.c0888e611264 | global/storage | 数据位置 / 下载缓存 | State | — | Available | False |
| global.storage.aa43fd2c91cc | global/storage | 数据位置 / 临时目录 | Setting | — | NotImplemented | False |
| global.storage.launcher-data | global/storage | 数据位置 / 启动器数据目录 | State | — | Available | False |
| global.storage.move-launcher-data | global/storage | 数据位置 / 迁移启动器数据 | Action | — | Available | False |
| global.storage.a85b167c467a | global/storage | 快照与备份 / 保留数量 | Setting | storage.backup-keep-count | Available | False |
| global.storage.8abd6775de97 | global/storage | 空间使用 / 空间使用 | Group | — | NotImplemented | False |
| global.storage.5850232d3ef3 | global/storage | 空间使用 / 实例逻辑占用 | State | — | NotImplemented | False |
| global.storage.ac38c015eccb | global/storage | 空间使用 / 实际磁盘占用 | State | — | NotImplemented | False |
| global.storage.023fe065000a | global/storage | 空间使用 / Cache | Setting | — | NotImplemented | False |
| global.storage.c540ba28a134 | global/storage | 空间使用 / Snapshot | Setting | — | NotImplemented | False |
| global.storage.3f698f014c69 | global/storage | 空间使用 / 已节省空间 | State | — | Available | False |
| global.storage.e54a4c664f84 | global/storage | 清理 / 清理 | Group | — | Available | False |
| global.storage.0114892ad6d0 | global/storage | 清理 / 下载缓存 | Setting | — | NotImplemented | False |
| global.storage.5ac61e098807 | global/storage | 清理 / 清理过期临时文件 | Action | — | Available | False |
| global.storage.7170d2a7255a | global/storage | 清理 / 无引用内容 | Setting | — | Available | False |
| global.storage.97fa31c2bbb3 | global/storage | 清理 / 旧 Snapshot | Setting | — | Available | False |
| global.storage.43b0e1b7d84d | global/storage | 清理 / 存储优化建议 | Setting | — | Available | False |
| global.storage.finished-tasks | global/storage | 清理 / 清除成功任务卡片 | Action | — | Available | False |
| global.storage.5cd2a8424fa9 | global/storage | 迁移 / 迁移 | Group | — | NotImplemented | False |
| global.storage.829f943b8c35 | global/storage | 迁移 / Nexa | Setting | — | Available | False |
| global.storage.0b8d3a443c57 | global/storage | 迁移 / Prism | Setting | — | NotImplemented | False |
| global.storage.b189310f58a5 | global/storage | 迁移 / Modrinth | Setting | — | NotImplemented | False |
| global.storage.055353100619 | global/storage | 迁移 / CurseForge | Setting | — | NotImplemented | False |
| global.storage.dbd600988484 | global/storage | 迁移 / HMCL | Setting | — | NotImplemented | False |
| global.storage.f2a3a0583f18 | global/storage | 迁移 / XMCL | Setting | — | NotImplemented | False |
| global.storage.88a48475677a | global/storage | 迁移 / ATLauncher | Setting | — | NotImplemented | False |
| global.storage.a1ef63f26a65 | global/storage | 迁移 / 普通 Minecraft 目录 | Setting | — | Available | False |
| global.storage.72c49f03a5de | global/storage | Portable / Portable | Group | — | NotImplemented | False |
| global.storage.705f42b2b46f | global/storage | Portable / Portable Mode | Setting | — | NotImplemented | False |
| global.storage.9c7477008a7a | global/storage | Portable / 搬家检查 | Setting | — | Available | False |
| global.storage.b215d366b6b7 | global/storage | Portable / 路径依赖检查 | Setting | — | Available | False |
| global.storage.f8446fa361ba | global/storage | 存储优化 / 存储优化 | Group | — | NotImplemented | False |
| global.storage.1f8794991a2c | global/storage | 存储优化 / CAS | Setting | — | Available | False |
| global.storage.f92a7eb47ad6 | global/storage | 存储优化 / Deduplication | Setting | — | Available | False |
| global.storage.5b71d6487d6e | global/storage | 存储优化 / Hardlink / Reflink | Setting | — | NotImplemented | False |
| global.storage.baf8045b4e0a | global/storage | 存储优化 / Copy-on-Write | Setting | — | NotImplemented | False |
| global.storage.2466a31fd706 | global/storage | 存储优化 / Integrity Verification | Setting | — | Available | False |
| global.storage.ab4beadb3467 | global/storage | Snapshot / Snapshot | Group | — | NotImplemented | False |
| global.storage.9e0b1aa11571 | global/storage | Snapshot / 自动 Snapshot | Setting | — | NotImplemented | False |
| global.storage.ec641c618a41 | global/storage | Snapshot / 最大占用 | State | — | NotImplemented | False |
| global.storage.fd0f2da3d270 | global/storage | Snapshot / 自动清理策略 | Setting | — | Available | False |
| global.advanced.b05e9746f990 | global/storage | 设置数据 / 设置数据 | Group | — | NotImplemented | False |
| global.advanced.0a2a572d1179 | global/storage | 设置数据 / 导入设置 | Action | — | Available | False |
| global.advanced.f629377e6bbc | global/storage | 设置数据 / 导出设置 | Action | — | Available | False |
| global.advanced.c9b13355b1bc | global/storage | 设置数据 / 恢复默认 | Action | — | Available | False |
| global.advanced.3256b2eff654 | global/advanced | 启动器更新 / 自动检查更新 | Setting | updates.auto-check | Available | False |
| global.advanced.1cb852864ed6 | global/advanced | 启动器更新 / 更新通道 | Setting | updates.channel | Available | False |
| global.advanced.d74b17ef4663 | global/advanced | 启动器更新 / 后台下载 | Setting | network.background-download | Available | False |
| global.advanced.c23d1969c6c0 | global/advanced | 启动器更新 / Stable | Choice | — | NotImplemented | False |
| global.advanced.212580f8dfa6 | global/advanced | 启动器更新 / Beta | Choice | — | NotImplemented | False |
| global.advanced.ce3196c20673 | global/advanced | 启动器更新 / Preview / Nightly | Choice | — | NotImplemented | False |
| global.advanced.9d82c4016ac6 | global/advanced | 更新安全 / 更新安全 | Group | — | NotImplemented | False |
| global.advanced.1efbbd47c2cd | global/advanced | 更新安全 / Signature Verification | Setting | — | Available | False |
| global.advanced.abd4fdbab063 | global/advanced | 更新安全 / Atomic Update | Setting | — | Available | False |
| global.advanced.df63204ed949 | global/advanced | 更新安全 / Last Known Good | Setting | — | Available | False |
| global.advanced.8bc788cc8548 | global/advanced | 更新安全 / Auto Rollback | Setting | — | Available | False |
| global.advanced.12ff7500770c | global/advanced | 更新安全 / Launcher Safe Mode | Setting | — | Available | False |
| global.advanced.913925497bbb | global/advanced | 系统兼容 / 系统兼容 | Group | — | NotImplemented | False |
| global.advanced.b625c3bec7db | global/advanced | 系统兼容 / 系统兼容策略 | Setting | — | NotImplemented | False |
| global.advanced.c4b7fc6352e9 | global/advanced | 系统兼容 / Compatibility Workaround | Setting | — | NotImplemented | False |
| global.advanced.3fe65eb928ee | global/advanced | 自动化 / 自动化 | Group | — | NotImplemented | False |
| global.advanced.918eee239cdb | global/advanced | 自动化 / nexa:// Deep Link | Setting | — | Available | False |
| global.advanced.fd858ddc777f | global/advanced | 自动化 / CLI Integration | Setting | — | Available | False |
| global.advanced.d06eb743bc69 | global/advanced | 自动化 / Command Palette Integration | Setting | — | Available | False |
| global.advanced.115156b4d96e | global/advanced | 开发与实验 / [ Off / On ] | Setting | developer.enabled | Available | False |
| global.advanced.ac3f1de1da0e | global/advanced | 开发者选项 / 开发者选项 | Group | — | NotImplemented | False |
| global.advanced.f152873176d7 | global/advanced | Nexa 更新 / Nexa 更新 | Group | — | NotImplemented | False |
| global.advanced.1c1d87d5b9a2 | global/advanced | Nexa 更新 / Release Notes | Setting | — | Available | False |
| global.advanced.88586d59987e | global/advanced | 实验功能 / 实验功能 | Group | — | NotImplemented | False |
| global.advanced.f67f27634920 | global/advanced | 实验功能 / Experimental Homepage | Setting | — | NotImplemented | False |
| global.advanced.d9987876215e | global/advanced | 实验功能 / Next Render Backend | Setting | — | NotImplemented | False |
| global.advanced.46c60fc2f8f9 | global/advanced | 实验功能 / Launch Shortcuts | Setting | — | NotImplemented | False |
| global.advanced.b3697e87e175 | global/advanced | 实验功能 / Minecraft AI Repair | Setting | — | NotImplemented | False |
| global.advanced.bc11fed7a65b | global/advanced | Debug Mode / Debug Mode | Setting | — | Available | True |
| global.advanced.3cf9555686bb | global/advanced | Debug Delay / Debug Delay | Setting | — | Available | True |
| global.advanced.59178c9ecad5 | global/advanced | Debug Animation / Debug Animation | Setting | — | Available | True |
| global.advanced.28957465d6fb | global/advanced | Debug Skip Copy / Debug Skip Copy | Setting | — | NotImplemented | True |
| global.advanced.01c0534c8b37 | global/advanced | XSR State Inspector / XSR State Inspector | State | — | Available | True |
| global.advanced.dc7ae54c530b | global/advanced | XSR Operation Log / XSR Operation Log | Setting | — | Available | True |
| global.advanced.8d723d8dcdfb | global/advanced | Renderer Diagnostics / Renderer Diagnostics | State | — | Available | True |
| global.advanced.40eee0bd4639 | global/advanced | Runtime Diagnostics / Runtime Diagnostics | Setting | — | Available | True |
| global.advanced.0255fcfeb638 | global/advanced | Raw Settings / Raw Settings | Setting | — | Available | True |
| global.advanced.a4b638d31598 | global/advanced | 打开 settings.json / 打开 settings.json | Action | — | Available | True |
| global.advanced.ce82bbfb31fd | global/advanced | Internal Feature Flags / Internal Feature Flags | Setting | — | Available | True |
| global.advanced.ui-policy | global/advanced | 界面与恢复 / 界面与恢复 | Group | — | Available | False |
| global.privacy.6da846c36100 | global/privacy | 数据与隐私 / 使用体验计划 | Setting | diagnostics.telemetry | Available | False |
| global.privacy.38d57b46395a | global/privacy | 数据与隐私 / 数据与隐私 | Group | — | NotImplemented | False |
| global.privacy.09cef506324c | global/privacy | 数据与隐私 / Crash Report | Setting | — | Available | False |
| global.privacy.6b6d7c5093d2 | global/privacy | 数据与隐私 / Diagnostic Data | Setting | — | Available | False |
| global.privacy.15f7c98cd9de | global/privacy | 日志 / 日志等级 | Setting | diagnostics.log-level | Available | False |
| global.privacy.64bbcd6cfb99 | global/privacy | 日志 / 最大日志数量 | Setting | diagnostics.log-lines | Available | False |
| global.privacy.35d95a7c9d2d | global/privacy | 日志 / 日志保留 | Setting | diagnostics.disk-log-days | Available | False |
| global.privacy.c68cf1d3e3d2 | global/privacy | 日志 / 日志 | Group | — | NotImplemented | False |
| global.privacy.ea4ad91ab2ba | global/privacy | 日志 / 打开日志目录 | Action | — | Available | False |
| global.privacy.25ff9ed3a4bf | global/privacy | 日志 / 导出日志 | Action | — | Available | False |
| global.privacy.6e8c77bf30bf | global/privacy | 隐私保护 / 隐私保护 | Group | — | NotImplemented | False |
| global.privacy.0fb9a2e35f78 | global/privacy | 隐私保护 / 自动脱敏 | Setting | — | Available | False |
| global.privacy.cde451802b77 | global/privacy | 隐私保护 / 安全复制 | Setting | — | Available | False |
| global.privacy.d3893574d03c | global/privacy | 隐私保护 / Diagnostic Bundle Privacy | Setting | — | Available | False |
| global.privacy.f07ee7c87d0e | global/privacy | AI 诊断 / 启用 AI | Setting | diagnostics.ai.enabled | Available | False |
| global.privacy.c1b151abf947 | global/privacy | AI 诊断 / Reasoning | Setting | diagnostics.ai.reasoning | Available | False |
| global.privacy.b0a44bbbe676 | global/privacy | AI 诊断 / AI 诊断 | Group | — | NotImplemented | False |
| global.privacy.3b2402362709 | global/privacy | AI 诊断 / Provider | Setting | — | Available | False |
| global.privacy.99192c77480a | global/privacy | AI 诊断 / Model | Setting | — | Available | False |
| global.privacy.c11af566f670 | global/privacy | AI 诊断 / Token Budget | Setting | — | Available | False |
| global.privacy.7d9209f704f6 | global/privacy | AI 诊断 / 数据共享范围 | Setting | — | Available | False |
| global.privacy.eb62ee215bbc | global/privacy | 内容可信 / 内容可信 | Group | — | NotImplemented | False |
| global.privacy.cf1e12358567 | global/privacy | 内容可信 / 当前内容的来源匹配事实 | State | — | Available | False |
| global.privacy.5ef935587d14 | global/privacy | 内容可信 / 已知下载文件强制校验 | State | — | Available | False |
| global.privacy.3ba209f3348d | global/privacy | 内容可信 / 与已保存更新基准的修改对照 | State | — | Available | False |
| global.privacy.93f8747a7762 | global/privacy | 内容可信 / Unknown JAR Policy | Setting | — | NotImplemented | False |
| global.privacy.4d3567ac419b | global/privacy | Minecraft 诊断 / Minecraft 诊断 | Group | — | NotImplemented | False |
| global.privacy.4a4af3012aa4 | global/privacy | Minecraft 诊断 / Crash Analyzer | Setting | — | Available | False |
| global.privacy.d695f66ed522 | global/privacy | Minecraft 诊断 / Launch Diagnostics | Setting | — | Available | False |
| global.privacy.f48f3318e658 | global/privacy | Minecraft 诊断 / Resource Exhaustion | Setting | — | Available | False |
| global.privacy.3350ed9a9f35 | global/privacy | Minecraft 诊断 / Recent-change Diagnosis | Setting | — | Available | False |
| global.privacy.e77e0a4d7c11 | global/privacy | 系统诊断 / 系统诊断 | Group | — | NotImplemented | False |
| global.privacy.b28439ff7cc3 | global/privacy | 系统诊断 / Windows Event Correlation | Setting | — | Available | False |
| global.privacy.04ecbba47ef4 | global/privacy | 系统诊断 / GPU / TDR | Setting | — | Available | False |
| global.privacy.9958a9f23f56 | global/privacy | 系统诊断 / WHEA | Setting | — | Available | False |
| global.privacy.7ac2f9bfa1ea | global/privacy | 系统诊断 / Native Crash Correlation | Setting | — | Available | False |
| global.privacy.c2d61e1dc584 | global/privacy | Realtime Log / Realtime Log | Setting | — | Available | True |
| global.privacy.6c8db73e33b8 | global/privacy | XSR Operation Trace / XSR Operation Trace | State | — | Available | True |
| global.privacy.1e51861a3aee | global/privacy | State Trace / State Trace | State | — | Available | True |
| global.privacy.25c2ac9fa4ed | global/privacy | Launch Trace / Launch Trace | State | — | Available | True |
| global.privacy.c3667ce23f3b | global/privacy | Raw Diagnostic Data / Raw Diagnostic Data | Setting | — | Available | True |
| instance.development-mode | instance/overview | 整合包开发 / 整合包开发模式 | Setting | instance.development-mode | Available | False |
| instance.recovery.keep-history | instance/recovery | 快照保留 / 保留历史快照 | Setting | recovery.keep-history | Available | False |
| instance.overview.fbfe8d779951 | instance/overview | 名称 / 图标 / 名称 / 图标 | Setting | — | Available | False |
| instance.overview.78c89aeac0d1 | instance/overview | Minecraft / Minecraft | Setting | — | Available | False |
| instance.overview.70d026453c0f | instance/overview | Loader / Loader | Setting | — | Available | False |
| instance.overview.d1e245c8ad1c | instance/overview | Modpack / Modpack | Setting | — | Available | False |
| instance.overview.8228a17a1eac | instance/overview | Java / Java | Setting | — | Available | False |
| instance.overview.a3f8bc20aaf9 | instance/overview | 最近启动 / 最近启动 | State | — | Available | False |
| instance.overview.a0a265bd720b | instance/overview | 游戏时间 / 游戏时间 | State | — | Available | False |
| instance.overview.988b4b01b3eb | instance/overview | Instance Health / Instance Health | State | — | Available | False |
| instance.overview.732a45f9e791 | instance/overview | Resource Summary / Resource Summary | State | — | Available | False |
| instance.overview.56e33e480cdf | instance/overview | 启动 / 启动 | Action | — | Available | False |
| instance.overview.17a004456ff2 | instance/overview | 修改版本 / 修改版本 | Action | — | Available | False |
| instance.overview.90172f2592b8 | instance/overview | 打开目录 / 打开目录 | Action | — | Available | False |
| instance.overview.7fb60a50d168 | instance/overview | 更多操作 / 更多操作 | Action | — | Available | False |
| instance.content.05803a333d18 | instance/content | Mods / Mods | Setting | — | Available | False |
| instance.content.d0c2287854d6 | instance/content | Resource Packs / Resource Packs | Setting | — | Available | False |
| instance.content.fc3ff537cf56 | instance/content | Shaders / Shaders | Setting | — | Available | False |
| instance.content.619a816efe84 | instance/content | Data Packs / Data Packs | Setting | — | Available | False |
| instance.content.2e3fd2c9385f | instance/content | 已安装 / 已安装 | Setting | — | Available | False |
| instance.content.22b442dcb594 | instance/content | 可更新 / 可更新 | Setting | — | Available | False |
| instance.content.2ee458fdbb24 | instance/content | 禁用 / 禁用 | Setting | — | Available | False |
| instance.content.e0991a1031cc | instance/content | 冲突 / 异常 / 冲突 / 异常 | Setting | — | Available | False |
| instance.worlds.17db261a8c32 | instance/worlds | World List / World List | State | — | Available | False |
| instance.worlds.777fa6b3b8a5 | instance/worlds | Last Played / Last Played | Setting | — | Available | False |
| instance.worlds.3fff1c03d28a | instance/worlds | Minecraft / DataVersion / Minecraft / DataVersion | State | — | Available | False |
| instance.worlds.ee27261308f3 | instance/worlds | Size / Size | Setting | — | Available | False |
| instance.worlds.6c5d0a10ab50 | instance/worlds | Health / Health | State | — | Available | False |
| instance.worlds.0f68adbfffa2 | instance/worlds | 打开 / 打开 | Action | — | Available | False |
| instance.worlds.ae94fe88e32c | instance/worlds | 备份 / 备份 | Setting | — | Available | False |
| instance.worlds.ab4beadb3467 | instance/worlds | Snapshot / Snapshot | Setting | — | Available | False |
| instance.worlds.faad5f3cce5e | instance/worlds | Duplicate / Duplicate | Action | — | Available | False |
| instance.worlds.22effc520481 | instance/worlds | Delete / Delete | Action | — | Available | False |
| instance.servers.a8a1cc4caac7 | instance/servers | Server List / Server List | State | — | Available | False |
| instance.servers.8c6092b5ce84 | instance/servers | 默认服务器 / 默认服务器 | Setting | — | Available | False |
| instance.servers.b485db8ebf9f | instance/servers | Compatibility Status / Compatibility Status | State | — | Available | False |
| instance.servers.de9988a1e55f | instance/servers | Environment Match / Environment Match | Setting | — | Available | False |
| instance.servers.73ea41d2f3c0 | instance/servers | Quick Play / Quick Play | Setting | — | Available | False |
| instance.files.29b2e7f92b05 | instance/files | Instance Root / Instance Root | Setting | — | Available | False |
| instance.files.8f0192eaa51c | instance/files | Config / Config | Setting | — | Available | False |
| instance.files.3f21274a1129 | instance/files | Logs / Logs | Setting | — | Available | False |
| instance.files.0133bf26089d | instance/files | Crash Reports / Crash Reports | Setting | — | Available | False |
| instance.files.ff0bb86d9c48 | instance/files | Saves / Saves | Setting | — | Available | False |
| instance.files.05803a333d18 | instance/files | Mods / Mods | Setting | — | Available | False |
| instance.files.d0c2287854d6 | instance/files | Resource Packs / Resource Packs | Setting | — | Available | False |
| instance.files.83c37904da0b | instance/files | Other / Other | Setting | — | Available | False |
| instance.screenshots.3eae7fd3de23 | instance/screenshots | Gallery / Gallery | State | — | Available | False |
| instance.screenshots.eacf056aa1f9 | instance/screenshots | Timeline / Timeline | State | — | Available | False |
| instance.screenshots.998b17a3bbc7 | instance/screenshots | Folder / Folder | Setting | — | Available | False |
| instance.screenshots.0a4632048b4e | instance/screenshots | Copy / Copy | Action | — | Available | False |
| instance.screenshots.b4a843861188 | instance/screenshots | Share / Share | Action | — | Available | False |
| instance.screenshots.22effc520481 | instance/screenshots | Delete / Delete | Action | — | Available | False |
| instance.recovery.544d531cda5c | instance/recovery | Change Timeline / Change Timeline | State | — | Available | False |
| instance.recovery.300297281702 | instance/recovery | Last Known Good / Last Known Good | Setting | — | Available | False |
| instance.recovery.32ed8e6c59a9 | instance/recovery | Snapshots / Snapshots | Setting | — | Available | False |
| instance.recovery.589eb002b816 | instance/recovery | Update History / Update History | State | — | Available | False |
| instance.recovery.de1acce7e91a | instance/recovery | Diff / Diff | Action | — | Available | False |
| instance.recovery.1696b3e83743 | instance/recovery | Undo / Undo | Action | — | Available | False |
| instance.recovery.6908855c15d5 | instance/recovery | Restore / Restore | Action | — | Available | False |
| instance.recovery.0042f30a2a2c | instance/recovery | Rollback / Rollback | Action | — | Available | False |
| instance.diagnostics.5ee7568c2526 | instance/diagnostics | 当前状态 / 当前状态 | State | — | Available | False |
| instance.diagnostics.bb95141d8acf | instance/diagnostics | Launch Preflight / Launch Preflight | Setting | — | Available | False |
| instance.diagnostics.d57b2a3b0791 | instance/diagnostics | Launch History / Launch History | State | — | Available | False |
| instance.diagnostics.17e6ddd6fbb6 | instance/diagnostics | Crash History / Crash History | State | — | Available | False |
| instance.diagnostics.220cd35381a6 | instance/diagnostics | 启动性能 / 启动性能 | Setting | — | Available | False |
| instance.diagnostics.e77c11a5601a | instance/diagnostics | 最近变化分析 / 最近变化分析 | Setting | — | Available | False |
| instance.diagnostics.872253956c5e | instance/diagnostics | 依赖 / 冲突 / 依赖 / 冲突 | Setting | — | Available | False |
| instance.diagnostics.ef6ad908dc24 | instance/diagnostics | Resource Analysis / Resource Analysis | Setting | — | Available | False |
| instance.diagnostics.164862be0259 | instance/diagnostics | System Correlation / System Correlation | Setting | — | Available | False |
| instance.diagnostics.e120d139a2e5 | instance/diagnostics | Repair Plan / Repair Plan | Setting | — | Available | False |
| instance.diagnostics.4ba3086d0572 | instance/diagnostics | Diagnostic Bundle / Diagnostic Bundle | Setting | — | Available | False |
| instance-settings.basic.79e47f9ac834 | instance-settings/basic | 名称 / 名称 | Setting | — | Available | False |
| instance-settings.basic.3a0d5d5aa2ac | instance-settings/basic | 描述 / 描述 | Setting | — | Available | False |
| instance-settings.basic.b557a4bf44aa | instance-settings/basic | 图标 / 图标 | Setting | — | Available | False |
| instance-settings.basic.771f820442b1 | instance-settings/basic | 收藏 / 收藏 | Setting | — | Available | False |
| instance-settings.basic.595a4ca570a5 | instance-settings/basic | Tags / Group / Tags / Group | Setting | — | Available | False |
| instance-settings.basic.1588c496fbc8 | instance-settings/basic | Notes / Notes | Setting | — | Available | False |
| instance-settings.basic.fecf8ceb8303 | instance-settings/basic | Custom Info / Custom Info | Setting | — | Available | False |
| instance-settings.basic.4c5dfb0bcc3d | instance-settings/basic | 实例目录 / 实例目录 | Setting | — | Available | False |
| instance-settings.basic.09d985efd446 | instance-settings/basic | Instance Isolation / Instance Isolation | Setting | — | Available | False |
| instance-settings.basic.d1e245c8ad1c | instance-settings/basic | Modpack / Modpack | Group | — | NotImplemented | False |
| instance-settings.basic.b7c88a7cee30 | instance-settings/basic | Modpack / Project | Setting | — | Available | False |
| instance-settings.basic.99c5e92def4c | instance-settings/basic | Modpack / Version | State | — | Available | False |
| instance-settings.basic.43a10f6cd588 | instance-settings/basic | Window Title / Window Title | Setting | game.title | Available | False |
| instance-settings.basic.38c99e71b0f6 | instance-settings/basic | Window Title / 继承全局 | Choice | — | NotImplemented | False |
| instance-settings.basic.60e197b6e86e | instance-settings/basic | Window Title / 自定义 | Choice | — | NotImplemented | False |
| instance-settings.java.02571b3e8889 | instance-settings/java | 继承全局 / 继承全局 | Choice | — | NotImplemented | False |
| instance-settings.java.8a8db1e84a91 | instance-settings/java | 自动 / 自动 | Choice | — | NotImplemented | False |
| instance-settings.java.e461a9116826 | instance-settings/java | 指定 Runtime / 指定 Runtime | Choice | — | NotImplemented | False |
| instance-settings.java.d3dc2c1d0f3a | instance-settings/java | Java Policy / Java Policy | Setting | java.runtime | Available | False |
| instance-settings.java.886543d4b4d6 | instance-settings/java | Selected Runtime / Selected Runtime | Setting | java.runtime | Available | False |
| instance-settings.java.597a0470d275 | instance-settings/java | Runtime Information / Runtime Information | State | — | Available | False |
| instance-settings.java.dc5ff6fcd886 | instance-settings/java | Compatibility / Compatibility | State | — | Available | False |
| instance-settings.java.8252c8559e09 | instance-settings/java | 扫描 / 扫描 | Action | — | Available | False |
| instance-settings.java.b35a483986d4 | instance-settings/java | 下载 / 下载 | Action | — | Available | False |
| instance-settings.java.38e1f8df3aa0 | instance-settings/java | 验证 / 验证 | State | — | Available | False |
| instance-settings.resources.31e7e4133f38 | instance-settings/resources | 内存 / 内存 | Setting | game.memory | Available | False |
| instance-settings.resources.4439664d9511 | instance-settings/resources | 内存 / 继承全局 | Choice | — | NotImplemented | False |
| instance-settings.resources.671919d7fd25 | instance-settings/resources | 内存 / 自动 | Choice | — | NotImplemented | False |
| instance-settings.resources.d7fe582f8d7a | instance-settings/resources | 内存 / 自定义 | Choice | — | NotImplemented | False |
| instance-settings.resources.56402644adf3 | instance-settings/resources | Resource Estimator / Resource Estimator | Group | — | NotImplemented | False |
| instance-settings.resources.2e46ef3f04c1 | instance-settings/resources | Resource Estimator / Recommended Xmx | State | — | Available | False |
| instance-settings.resources.9927df5e4ef0 | instance-settings/resources | Resource Estimator / RAM Estimate | State | — | Available | False |
| instance-settings.resources.dd69318b8bc1 | instance-settings/resources | Resource Estimator / Startup Peak | State | — | Available | False |
| instance-settings.resources.67976deb5cf9 | instance-settings/resources | Resource Estimator / Commit Estimate | State | — | Available | False |
| instance-settings.resources.1dab25552608 | instance-settings/resources | Resource Estimator / Historical Calibration | State | — | Available | False |
| instance-settings.resources.ae582c07f394 | instance-settings/resources | GPU / GPU | Group | — | NotImplemented | False |
| instance-settings.resources.c5663ae98c45 | instance-settings/resources | GPU / 自动 | Choice | — | NotImplemented | False |
| instance-settings.resources.8c486af00d94 | instance-settings/resources | GPU / 指定 GPU | Setting | game.gpu-preference | Available | False |
| instance-settings.resources.7f105865bd84 | instance-settings/resources | GPU / Renderer | Setting | game.renderer | Available | False |
| instance-settings.resources.5be5eac17a86 | instance-settings/resources | GPU / VRAM Estimate | State | — | Available | False |
| instance-settings.resources.278b55f814ea | instance-settings/resources | GPU / iGPU Shared Memory | State | — | Available | False |
| instance-settings.resources.e59e6c9e791d | instance-settings/resources | Hardware Advice / Hardware Advice | Group | — | NotImplemented | False |
| instance-settings.resources.140aa6a41093 | instance-settings/resources | Hardware Advice / Shader | State | — | Available | False |
| instance-settings.resources.437184290d06 | instance-settings/resources | Hardware Advice / Resource Pack | State | — | Available | False |
| instance-settings.resources.03f3222cf10f | instance-settings/resources | Hardware Advice / Render Distance | State | — | Available | False |
| instance-settings.resources.b9b0340358d5 | instance-settings/resources | Hardware Advice / Risk | State | — | Available | False |
| instance-settings.resources.cadfbbd79205 | instance-settings/resources | Process Priority / Process Priority | Setting | — | Available | False |
| instance-settings.resources.3a4bed17a856 | instance-settings/resources | Game Resource Handoff / Game Resource Handoff | Setting | — | Available | False |
| instance-settings.window.1cf65ca0a095 | instance-settings/window | 游戏窗口 / 游戏窗口 | Group | — | NotImplemented | False |
| instance-settings.window.ea72ea9fe550 | instance-settings/window | 游戏窗口 / 继承全局 | Choice | — | NotImplemented | False |
| instance-settings.window.eb8c4beea573 | instance-settings/window | 游戏窗口 / Window Mode | Setting | game.window-mode | Available | False |
| instance-settings.window.75e2c3cc34fc | instance-settings/window | 游戏窗口 / Width | Setting | game.width | Available | False |
| instance-settings.window.89def67dd796 | instance-settings/window | 游戏窗口 / Height | Setting | game.height | Available | False |
| instance-settings.window.422e9c1723d3 | instance-settings/window | 标题 / 标题 | Setting | game.title | Available | False |
| instance-settings.window.3f6a8aeccce7 | instance-settings/window | 标题 / 使用全局标题 | Choice | — | NotImplemented | False |
| instance-settings.window.632f85b8dff8 | instance-settings/window | 标题 / 自定义 | Choice | — | NotImplemented | False |
| instance-settings.window.0f518f9f7f9f | instance-settings/window | 启动时 / 启动时 | Group | — | NotImplemented | False |
| instance-settings.window.b28c9d125ee6 | instance-settings/window | 启动时 / Nexa 保持显示 | Setting | — | Available | False |
| instance-settings.window.44effed05745 | instance-settings/window | 启动时 / 最小化 | Setting | — | Available | False |
| instance-settings.window.9ff25c818325 | instance-settings/window | 启动时 / 启动器窗口 | Setting | game.launcher-visibility | Available | False |
| instance-settings.window.0322e116710e | instance-settings/window | 启动时 / 关闭 | Setting | — | Available | False |
| instance-settings.window.bb58bd6e61d7 | instance-settings/window | 游戏结束 / 游戏结束 | Group | — | NotImplemented | False |
| instance-settings.window.f7c129eae162 | instance-settings/window | 游戏结束 / Nexa 恢复行为 | Setting | — | Available | False |
| instance-settings.hooks.56e906523d01 | instance-settings/hooks | JVM / JVM | Group | — | NotImplemented | False |
| instance-settings.hooks.0e333c85ccd9 | instance-settings/hooks | JVM / JVM Arguments | Setting | game.jvm | Available | False |
| instance-settings.hooks.a5ca14fb0e8c | instance-settings/hooks | JVM / Game Arguments | Setting | game.arguments | Available | False |
| instance-settings.hooks.65f76a8312d0 | instance-settings/hooks | JVM / Classpath Head | Setting | game.classpath-head | Available | False |
| instance-settings.hooks.1ee9054623a9 | instance-settings/hooks | JVM / Environment Variables | Setting | game.environment | Available | False |
| instance-settings.hooks.0beb2cd090d6 | instance-settings/hooks | Wrapper / Wrapper | Group | — | NotImplemented | False |
| instance-settings.hooks.361d4ddf309e | instance-settings/hooks | Wrapper / Wrapper Command | Setting | game.wrapper | Available | False |
| instance-settings.hooks.932881441271 | instance-settings/hooks | Pre-launch / Pre-launch | Group | — | NotImplemented | False |
| instance-settings.hooks.40aa4034e8d4 | instance-settings/hooks | Pre-launch / Command | Setting | game.pre-launch | Available | False |
| instance-settings.hooks.5f3baf5fd6e4 | instance-settings/hooks | Pre-launch / Wait for Completion | Setting | game.pre-launch-wait | Available | False |
| instance-settings.hooks.58acb0984829 | instance-settings/hooks | Post-exit / Post-exit | Group | — | NotImplemented | False |
| instance-settings.hooks.27198ce02cfc | instance-settings/hooks | Post-exit / Command | Setting | game.post-exit | Available | False |
| instance-settings.hooks.10c620ce1c99 | instance-settings/hooks | Native Compatibility / Native Compatibility | Group | — | NotImplemented | False |
| instance-settings.hooks.d675482eefee | instance-settings/hooks | Native Compatibility / Disable JLW | Setting | — | PlatformUnsupported | False |
| instance-settings.hooks.d5047c9dd72e | instance-settings/hooks | Native Compatibility / Disable RW | Setting | — | PlatformUnsupported | False |
| instance-settings.hooks.09ebeb623281 | instance-settings/hooks | Native Compatibility / Debug Log4j | Setting | — | PlatformUnsupported | False |
| instance-settings.hooks.3a071fa4c0d7 | instance-settings/hooks | Native Compatibility / Disable LWJGL Unsafe Agent | Setting | — | PlatformUnsupported | False |
| instance-settings.hooks.d32f82a77b56 | instance-settings/hooks | Native Compatibility / System GLFW | Setting | game.system-glfw | Available | False |
| instance-settings.hooks.50de4c103e2a | instance-settings/hooks | Native Compatibility / Force X11 on Wayland | Setting | — | PlatformUnsupported | False |
| instance-settings.profiles.1c54db785358 | instance-settings/profiles | Profiles / Profiles | Group | — | NotImplemented | False |
| instance-settings.profiles.5dc07bac3718 | instance-settings/profiles | Profiles / Default | Choice | — | NotImplemented | False |
| instance-settings.profiles.a844242b59cd | instance-settings/profiles | Profiles / Performance | Choice | — | NotImplemented | False |
| instance-settings.profiles.4c10402439cd | instance-settings/profiles | Profiles / Screenshot | Choice | — | NotImplemented | False |
| instance-settings.profiles.0caa7d60d543 | instance-settings/profiles | Profiles / Multiplayer | Choice | — | NotImplemented | False |
| instance-settings.profiles.0202d396c1fb | instance-settings/profiles | Profiles / Debug | Choice | — | NotImplemented | False |
| instance-settings.profiles.251fd5a0bdeb | instance-settings/profiles | Profile Override / Profile Override | Group | — | NotImplemented | False |
| instance-settings.profiles.f209065b1798 | instance-settings/profiles | Profile Override / Mods | Setting | — | Available | False |
| instance-settings.profiles.813507d1c795 | instance-settings/profiles | Profile Override / Resource Packs | Setting | — | Available | False |
| instance-settings.profiles.5932fb33f692 | instance-settings/profiles | Profile Override / Shader | Setting | — | Available | False |
| instance-settings.profiles.2f0ae4b3f9a0 | instance-settings/profiles | Profile Override / Config | Setting | — | Available | False |
| instance-settings.profiles.d535f1769202 | instance-settings/profiles | Profile Override / JVM | Setting | — | Available | False |
| instance-settings.profiles.2cbc9526055b | instance-settings/profiles | Profile Override / Memory | Setting | — | Available | False |
| instance-settings.profiles.bbe9aad82dcf | instance-settings/profiles | Profile Override / Server | Choice | — | Available | False |
| instance-settings.profiles.e8601c21ffcf | instance-settings/profiles | Temporary Overlay / Temporary Overlay | Group | — | NotImplemented | False |
| instance-settings.profiles.8eec3283a0a3 | instance-settings/profiles | Temporary Overlay / 临时 Mods | Setting | — | Available | False |
| instance-settings.profiles.141700b7dc2e | instance-settings/profiles | Temporary Overlay / 临时 Resource Packs | Setting | — | Available | False |
| instance-settings.profiles.ca34ed2f75fe | instance-settings/profiles | Temporary Overlay / 临时 Shader | Setting | — | Available | False |
| instance-settings.profiles.636077328a1e | instance-settings/profiles | Temporary Overlay / 临时 Config | Setting | — | Available | False |
| instance-settings.profiles.8557766cf81a | instance-settings/profiles | Temporary Overlay / 临时 JVM Arguments | Setting | — | Available | False |
| instance-settings.profiles.ccae094de147 | instance-settings/profiles | Safe Launch / Safe Launch | Group | — | NotImplemented | False |
| instance-settings.profiles.412a989699be | instance-settings/profiles | Safe Launch / 禁用全部 Mods | State | — | Available | False |
| instance-settings.profiles.6a86c1d65288 | instance-settings/profiles | Safe Launch / Disable Shader | State | — | Available | False |
| instance-settings.profiles.aa2b636c351e | instance-settings/profiles | Safe Launch / Disable Resource Packs | State | — | Available | False |
| instance-settings.profiles.fe088946fb87 | instance-settings/profiles | Safe Launch / Disable Hooks | State | — | Available | False |
| instance-settings.profiles.cb1c4275b7f6 | instance-settings/profiles | Safe Launch / Disable Custom JVM Args | State | — | Available | False |
| instance-settings.servers.73ea41d2f3c0 | instance-settings/servers | Quick Play / Quick Play | Group | — | NotImplemented | False |
| instance-settings.servers.c43a6b3cbe99 | instance-settings/servers | Quick Play / None | Choice | — | NotImplemented | False |
| instance-settings.servers.156b446fd858 | instance-settings/servers | Quick Play / Server | Choice | — | NotImplemented | False |
| instance-settings.servers.8c6092b5ce84 | instance-settings/servers | 默认服务器 / 默认服务器 | Group | — | NotImplemented | False |
| instance-settings.servers.e6f6b15010be | instance-settings/servers | 默认服务器 / Address | Setting | game.server | Available | False |
| instance-settings.servers.cf6085f51bf5 | instance-settings/servers | 服务器认证 / 服务器认证 | Group | — | NotImplemented | False |
| instance-settings.servers.693438d90d7c | instance-settings/servers | 服务器认证 / Requirement | Setting | — | Available | False |
| instance-settings.servers.d6c3e6592b58 | instance-settings/servers | 服务器认证 / Auth Server | Setting | — | Available | False |
| instance-settings.servers.da73ed798ad8 | instance-settings/servers | 服务器认证 / Register URL | Setting | — | Available | False |
| instance-settings.servers.5a8d8adc344f | instance-settings/servers | 服务器认证 / Display Name | Setting | — | Available | False |
| instance-settings.servers.36e4dd9de50c | instance-settings/servers | 服务器认证 / Lock Authentication Settings | Setting | — | Available | False |
| instance-settings.security.55677450cba6 | instance-settings/security | 内容更新 / 内容更新 | Group | — | NotImplemented | False |
| instance-settings.security.4a298edce3d5 | instance-settings/security | 内容更新 / 自动检查 | Setting | — | NotImplemented | False |
| instance-settings.security.23f5aba95cca | instance-settings/security | 内容更新 / Update Policy | Setting | — | NotImplemented | False |
| instance-settings.security.26f1b3f51bf2 | instance-settings/security | 内容更新 / 已识别更新来源必须匹配 | State | — | Available | False |
| instance-settings.security.aea49566a9f6 | instance-settings/security | Update Impact / Update Impact | Group | — | NotImplemented | False |
| instance-settings.security.2493090ad0dc | instance-settings/security | Update Impact / Dependency Impact | Setting | — | NotImplemented | False |
| instance-settings.security.a0f9cc2b646e | instance-settings/security | Update Impact / Addon Compatibility | Setting | — | NotImplemented | False |
| instance-settings.security.71af5e87c864 | instance-settings/security | Update Impact / Loader Compatibility | Setting | — | NotImplemented | False |
| instance-settings.security.0f3047cf5381 | instance-settings/security | Update Impact / Risk Assessment | Setting | — | NotImplemented | False |
| instance-settings.security.7c8cbde454d7 | instance-settings/security | Safe Update / Safe Update | Group | — | NotImplemented | False |
| instance-settings.security.ef79c7f9b1a5 | instance-settings/security | Safe Update / 更新前保存所选原件 | State | — | Available | False |
| instance-settings.security.a5339dbd7bc7 | instance-settings/security | Safe Update / 更新失败回滚所选文件 | State | — | Available | False |
| instance-settings.security.bb95141d8acf | instance-settings/security | Launch Preflight / Launch Preflight | Group | — | NotImplemented | False |
| instance-settings.security.86c9be1f2bef | instance-settings/security | Launch Preflight / Critical → Block | State | — | Available | False |
| instance-settings.security.acbccd7bdaac | instance-settings/security | Launch Preflight / Warning → Ask | State | — | Available | False |
| instance-settings.security.871aab865ad2 | instance-settings/security | Launch Preflight / Ignore Policy | Setting | — | NotImplemented | False |
| instance-settings.security.badd614b29d4 | instance-settings/security | World Guardian / World Guardian | Group | — | NotImplemented | False |
| instance-settings.security.88ba1be4830d | instance-settings/security | World Guardian / Upgrade Protection | Setting | — | NotImplemented | False |
| instance-settings.security.0f7c5e74b592 | instance-settings/security | World Guardian / Downgrade Protection | Setting | — | NotImplemented | False |
| instance-settings.security.89e2cc2879c7 | instance-settings/security | World Guardian / Auto Snapshot | Setting | — | NotImplemented | False |
| instance-settings.security.bf130c6c074c | instance-settings/security | World Guardian / World Health | State | — | Available | False |
| instance-settings.security.d6211ce48af2 | instance-settings/security | 文件完整性 / 文件完整性 | Group | — | NotImplemented | False |
| instance-settings.security.bfec83b02373 | instance-settings/security | 文件完整性 / Assets Verification | State | — | Available | False |
| instance-settings.security.6605dca90be2 | instance-settings/security | 文件完整性 / Libraries Verification | State | — | Available | False |
| instance-settings.security.9a1792212a14 | instance-settings/security | 文件完整性 / Content Integrity | Setting | — | NotImplemented | False |
| instance-settings.security.abf86fed9019 | instance-settings/security | Content Provenance / Content Provenance | Group | — | NotImplemented | False |
| instance-settings.security.19bb05d719d3 | instance-settings/security | Content Provenance / Source | State | — | Available | False |
| instance-settings.security.ae662ab0ef1f | instance-settings/security | Content Provenance / SHA-256 | State | — | Available | False |
| instance-settings.security.42f5355c7bce | instance-settings/security | Content Provenance / Modified Status | State | — | Available | False |
| instance-settings.security.b585c2e079b2 | instance-settings/security | Content Provenance / Unknown Content Policy | Setting | — | NotImplemented | False |
| instance-settings.sync.b86078ade8ac | instance-settings/backup | Backup / Backup | Group | — | NotImplemented | False |
| instance-settings.sync.15876104b29b | instance-settings/backup | Backup / Worlds | Setting | — | Available | False |
| instance-settings.sync.336c2dcf7947 | instance-settings/backup | Backup / Config | Setting | — | Available | False |
| instance-settings.sync.bbf555e9930a | instance-settings/backup | Backup / Metadata | State | — | Available | False |
| instance-settings.sync.ccf26ac2deb3 | instance-settings/backup | Backup / Screenshots | Setting | — | Available | False |
| instance-settings.sync.2759a030c0b7 | instance-settings/backup | Thin Backup / Thin Backup | Group | — | NotImplemented | False |
| instance-settings.sync.bfdfe9808880 | instance-settings/backup | Thin Backup / Manifest | State | — | Available | False |
| instance-settings.sync.c8f63f87f291 | instance-settings/backup | Thin Backup / Lockfile | State | — | NotImplemented | False |
| instance-settings.sync.914c3ea7cbf1 | instance-settings/backup | Thin Backup / Non-reproducible Data | Setting | — | NotImplemented | False |
| instance-settings.sync.18c748d31124 | instance-settings/backup | Offline Readiness / Offline Readiness | Group | — | NotImplemented | False |
| instance-settings.sync.b85acee235c1 | instance-settings/backup | Offline Readiness / Account | Setting | — | NotImplemented | False |
| instance-settings.sync.cee29c346407 | instance-settings/backup | Offline Readiness / Java | Setting | — | Available | False |
| instance-settings.sync.129a08c3a04a | instance-settings/backup | Offline Readiness / Minecraft Client | Setting | — | Available | False |
| instance-settings.sync.b9a4c8bd47bf | instance-settings/backup | Offline Readiness / Libraries | Setting | — | Available | False |
| instance-settings.sync.4ba5c5689c79 | instance-settings/backup | Offline Readiness / Assets | Setting | — | Available | False |
| instance-settings.sync.3134ce164b15 | instance-settings/backup | Offline Readiness / Loader | Setting | — | NotImplemented | False |
| instance-settings.sync.cafc197c813b | instance-settings/backup | Offline Readiness / Mods | Setting | — | NotImplemented | False |
| instance-settings.sync.bd57ef32387c | instance-settings/backup | Offline Readiness / Prepare Offline | Action | — | NotImplemented | False |
| instance-settings.advanced.5f06019abe3e | instance-settings/advanced | Ignore Compatibility / Ignore Compatibility | Setting | — | PlatformUnsupported | False |
| instance-settings.advanced.787b274645ff | instance-settings/advanced | Disable Asset Verification / Disable Asset Verification | Setting | — | PlatformUnsupported | False |
| instance-settings.advanced.e8914ed0d7e2 | instance-settings/advanced | Other Compatibility Overrides / Other Compatibility Overrides | Setting | — | PlatformUnsupported | False |
| instance-settings.advanced.d5129739b1ae | instance-settings/advanced | Effective State / Effective State | Setting | — | Available | True |
| instance-settings.advanced.bdc6b2186762 | instance-settings/advanced | Effective Launch Plan / Effective Launch Plan | Setting | — | Available | True |
| instance-settings.advanced.9c7f83375a0b | instance-settings/advanced | Effective JVM Command / Effective JVM Command | Setting | — | Available | True |
| instance-settings.advanced.b84f343da972 | instance-settings/advanced | Effective Classpath / Effective Classpath | Setting | — | Available | True |
| instance-settings.advanced.74dac7537617 | instance-settings/advanced | Environment Variables / Environment Variables | Setting | game.environment | Available | True |
| instance-settings.advanced.504c5f4447d2 | instance-settings/advanced | Native Libraries / Native Libraries | Setting | — | Available | True |
| instance-settings.advanced.6f200d616dd1 | instance-settings/advanced | Manifest / Manifest | State | — | Available | True |
| instance-settings.advanced.8acf76e45c8e | instance-settings/advanced | Lockfile / Lockfile | State | — | NotImplemented | True |
| instance-settings.advanced.e89dd4f9c2a4 | instance-settings/advanced | Raw Instance Metadata / Raw Instance Metadata | State | — | Available | True |
| instance-settings.advanced.25c2ac9fa4ed | instance-settings/advanced | Launch Trace / Launch Trace | State | — | Available | True |
| instance-settings.advanced.8c5e81a76a9d | instance-settings/advanced | XSR State / XSR State | Setting | — | NotImplemented | True |
| instance-settings.advanced.8b445a04eee7 | instance-settings/advanced | Operation History / Operation History | State | — | Available | True |
