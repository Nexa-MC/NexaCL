# XSR 全项目清理清单与拆分方案

审计基线：`24a97b9b45d4652b755ad2edbe897928fde543f5`。仓库：`Nexa-MC/PCL-N`；目标分支：`refactor/xsr`。

用户已明确将范围扩大为所有项目。本轮只交付静态清单和拆分方案，不删除成员、不合并实现、不拆源码、不增加产品功能。所有位置与统计均指固定基线。

提交前目标分支已推进到 `df64465c21065798ca6baf0cc9f797144069b682`；相对基线的 7 个变更路径涉及 FirstRun/Wardrobe 页面、测试和说明，不在本清单候选定义文件中。本轮没有把这些并发提交纳入行数或引用统计，真正删改前仍需检查新引用。本次文档提交不覆盖并发修改。

## 1. 覆盖范围与方法

| 项目 | 结果 |
|---|---:|
| 仓库 .csproj / NexaCL.slnx 项目 | 70 / 70 |
| C# 物理文件 | 1,250 |
| C# 物理行，含空行和注释 | 178,688 |
| 项目外辅助脚本 | 43 |
| 源码及脚本合计 | 1,293 文件 / 184,243 行 |
| 超过 800 行 | 13 文件 |
| 私有成员未使用候选 | 7 |
| 相同无条件 ProjectReference | 2 处 |
| 相同方法体 token 序列 | 4 组：3 业务、1 测试 |
| 相同 CI 执行命令候选 | 1 组 |
| 整个源码文件字节完全相同 | 0 组 |

这是全项目第一轮静态初筛及候选上下文复核，不是每个文件都已人工逐行证明正确。没有发现候选不代表不存在问题。

以物理目录识别项目归属，不等于 MSBuild 评估后的 Compile item 数。`tests/Nexa.Services.Tests` 显式链接 `Nexa.Jvm.Host/JvmChildLaunch.cs` 和 `JvmChildRuntimeLease.cs`；这是共享编译输入，不能按重复文件删除。43 个辅助脚本进入文件统计和文本引用筛查，不代表逐个完成行为审计。

使用 tree-sitter 0.25.2 / tree-sitter-c-sharp 0.23.5，尝试解析全部 1,250 个 C# 文件，1,239 个未产生解析告警。方法克隆阈值为至少 45 个叶 token、至少 6 个方法体物理行；忽略注释和 token 间空白，保留标识符与字面量。该算法不覆盖变量改名、近似算法或跨语言重复，相同 token 也不证明语义绑定相同。

私有成员筛查纳入源码、测试、配置、生成模板和文档文本。下面 7 个名字只出现于定义；它们仍是删除候选，动态名称、生成代码、外部契约和运行时入口必须在删除前排除。

已有 `docs/xsr/XSR-828-code-consolidation.md` 记录先前清理，本轮不把其中已完成事项重新列为剩余工作。

## 2. 私有成员未使用候选

| 编号 | 固定基线位置 | 证据和建议 |
|---|---|---|
| C01 | `Nexa.Desktop/Program.cs:33–40`，`ResolveVersionChannel` | 旧渠道解析辅助方法无其他文本引用。候选删除方法及其专属注释；保留仍使用的 ResolveInformationalVersion 和 LauncherBuildIdentity。验证 Desktop 版本显示、渠道策略及编译。 |
| C02 | `Nexa.Desktop/Ui/SettingsPageController.Platform.cs:294`，`RefreshPlatformField` | 仅为 RefreshPlatform 的私有别名且无其他引用。候选删除别名，保留真实路由标识；验证平台设置刷新。 |
| C03 | `Nexa.Desktop/Ui/SettingsPageController.Worlds.cs:84–89`，`StartWorldPlatformAction` | 包装方法没有调用，并且是 _worldPlatformAction 唯一非空写入入口。候选删除方法及单独核验失效状态链，不能删整个刷新方法。 |
| C04 | `Nexa.Services.Updates/Updates/UpdatePackagePlanner.cs:14`，`DefaultAssetNamePrefix` | 私有常量 PCL_N_ 无其他文本引用。只考虑此常量，不连带删除 DefaultBinaryBaseName 或 LastV1BlockMapVersion 等历史更新兼容标识。 |
| C05 | `Nexa.UI.Next/XsrUiRenderer.cs:234–240`，`NeedsLayout` | 私有脏标记检查包装方法无其他文本引用。文件 1,680 行，先完成 S02 拆分审查，本轮不动。 |
| C06 | `tests/Nexa.Services.Tests/LaunchProgressTests.cs:622–623`，`ReadSnapshotText` | 私有快照读取测试辅助方法无其他文本引用。文件 971 行，先完成 S07 拆分审查；保留实际快照断言。 |
| C07 | `tests/Nexa.Services.Tests/LauncherSettingsTests.cs:22–31`，`CreateLauncherService` | 私有测试构造辅助方法无其他文本引用。候选删除方法，保留测试仍直接使用的设置 schema/service/port。 |

C03 的连带状态位于同文件第 10–11 行和第 109–113 行：没有调用 StartWorldPlatformAction 时，私有任务字段没有其他非空写入，尾部完成处理没有正常启动来源。**UpdateWorldPlatformAction 的前半段仍调用 UpdateWorldHealth，处理世界元数据和数据包选择，必须保留。** 删除前覆盖元数据读取、健康状态、数据包导入和管理页刷新。

C05/C06 仅登记，不能以候选可信度高为理由绕过 800 行限制。所有候选均未删除。

## 3. 重复配置和重复逻辑

### D01 / D02：相同无条件项目引用

- `Nexa.Services/Nexa.Services.csproj:12,58` 两次引用 `../Nexa.Platform.Runtime/Nexa.Platform.Runtime.csproj`。
- `tests/Nexa.Xsr.Runtime.Tests/Nexa.Xsr.Runtime.Tests.csproj:12,15` 两次引用 `../../Nexa.Xsr.Diagnostics/Nexa.Xsr.Diagnostics.csproj`。

建议每组保留一项。实际修改后比较评估后的引用图并运行构建和架构检查，不顺便删除其他显式引用或调整项目边界。本轮未修改 csproj。

### D03：CI 重复执行同一个入口

`.github/workflows/xsr-ci.yml:237–238` 的 `Test XSR runtime kernel` 与 `282–283` 的 `Test Sidecar host session and data plane` 使用相同的无额外过滤参数命令：

```text
dotnet run --project tests/Nexa.Xsr.Runtime.Tests/Nexa.Xsr.Runtime.Tests.csproj --configuration Release --no-build
```

建议确认无特殊顺序依赖后合为一次清楚命名的执行。NativeAOT publish/run 和跨平台矩阵属于不同验证维度，不能一并删除。

### D04：Desktop 元素显隐辅助方法

`Nexa.Desktop/Ui/DesktopFeedbackPresenter.cs:605–617` 与 `Nexa.Desktop/Ui/TaskCenterController.cs:571–582` 的 SetVisible 方法体 token 相同。

可考虑在 Desktop UI 内部集中，保留无变化时提前返回及 Layout|Paint 脏标记。**不能直接替换为 DesktopBubbleLayout.SetVisible**：后者还管理关闭动画、输入禁用、计时器和销毁，行为不等价。

### D05：HTTP 响应流读取

`Nexa.Services.Minecraft.Install/Minecraft/Install/MinecraftInstallService.cs:761–767` 的 HttpConnection.ReadAsync 与 `Nexa.Services.Minecraft.Launch/Minecraft/Launch/MinecraftLaunchFileCompletion.cs:338–344` 的 HttpConnectionAdapter.ReadAsync 方法体相同。

它们的适配器上下文、初始化和连接所有权仍需比较。只为 6 行逻辑新增跨项目抽象或依赖收益不足；优先评估完整适配器的现存共同归属，当前不直接合并。

### D06：取消源释放流程

`Nexa.Services.Minecraft.Install/Minecraft/Install/MinecraftInstallStop.cs:26–32` 的 InstallExecution.Dispose 与 `Nexa.Services.Minecraft.Java/Minecraft/Java/JavaRuntimeInstaller.Lifetime.cs:24–30` 的 JavaExecution.Dispose 方法体相同。

逻辑依赖锁、_finished 标记、待完成取消任务和 ExecuteSynchronously continuation。证明完整生命周期契约等价后才集中，不简化成直接 Dispose。

### D07：测试断言辅助方法

`tests/Nexa.Pxml.Tests/Program.cs:95–104` 与 `tests/Nexa.UI.Next.Tests/Program.cs:169–178` 的 AssertSequence<T> 方法体相同。

低优先级。只有存在合理测试工具归属时才归并，不给生产项目增加测试依赖，也不删除两个测试项目各自的真实断言覆盖。

## 4. 全部 13 个大文件的拆分方案

以下全部是方案，没有执行源码拆分。优先按现有类型做 partial 拆分，单个新文件通常控制在 300–650 行且不超过 800 行，不机械截断方法。这是目标，不是已达成指标。保持公开 API、访问级别、所属程序集、序列化、错误语义及调用顺序。

### S01 — 1,703 行：`Nexa.UI.Next.Backend.Avalonia/AvaloniaUiSceneSurface.cs`

先迁出 781 行起的 AvaloniaUiSceneNodeControl 与相邻事件参数类型，再将节点绘制、呈现和覆盖动画分成职责文件；Surface 保留宿主连接和生命周期。不能把 923 行节点类型原样搬成另一个超限文件。沿用现有 partial，保持 dispatcher 线程、事件订阅/退订、命中测试及动画完成顺序。

### S02 — 1,680 行：`Nexa.UI.Next/XsrUiRenderer.cs`

保留核心状态和 Render；分离 Layout/Measure/Arrange（285 行起）、指针/焦点/键盘处理（860 行起）、CollectNodes/CollectNode 场景投影（1355 行起）。已有 Scrolling、Transitions、TextInput 等分部必须复用，不能重复造同职责文件。保持脏标记、缓存清退、布局顺序与命中结果；C05 仅登记。

### S03 — 1,346 行：`Nexa.Services.Minecraft.Launch/Minecraft/Launch/MinecraftLaunchCoordinator.cs`

按 PrepareAsync（207 行起）、StartCore/StartLocked 和窗口等待（443 行起）、Java 决策/批准（800 行起）、请求和设置装配（913 行起）拆分，主文件保留编排与共享状态。保持同一服务类型，不迁移项目归属；锁、阶段顺序、取消/最终化、Java 选择和进程所有权不变。

### S04 — 1,290 行：`tests/Nexa.Desktop.Tests/Program.cs`

Program 保留 Main 与用例注册；页面、导航和任务中心测试按现有边界归并，LaunchPageFixture 和模拟实例源独立组织。不得覆盖已有同名测试文件。保持测试名称、注册顺序、过滤参数、退出码和夹具释放，不迁移测试框架。

### S05 — 1,012 行：`Nexa.UI.Next/XsrUiShell.cs`

拆出 palette/样式模型和 factory；Shell 按创建组合、导航状态、外观应用划分 partial，主文件留公共入口与共享字段。保持语义 ID、树拓扑、选择状态、事件顺序及 renderer/state bridge 所有权。

### S06 — 995 行：`tests/Nexa.UI.Next.Backend.Avalonia.Tests/Program.cs`

Program 留入口和测试表；拆出 automation、LifetimeProbeApp 与原生烟测、动画时钟、剪贴板/窗口测试。保持原生应用初始化与 dispatcher 线程要求，不合并不同 OS 生命周期测试，不删除原生烟测参数。

### S07 — 971 行：`tests/Nexa.Services.Tests/LaunchProgressTests.cs`

按阶段权重与状态发布、Java 下载批准、取消/重入/窗口等待、X11 烟测划分，共用模拟进程和辅助方法放夹具分部。保持注册、平台门控、阶段覆盖和取消竞争测试；C06 在拆分审查后再处理。

### S08 — 959 行：`tests/Nexa.Services.Tests/MinecraftAcceptanceTests.cs`

分离 Java/版本兼容、启动参数/client jar、classpath/native libraries、manifest 规则、路由及路径穿越验收，共用 fixtures 保留在测试层。保持全部版本矩阵和安全断言，不因相似名称删除跨层验收。

### S09 — 906 行：`Nexa.Desktop/Ui/LaunchPageController.cs`

从主文件抽出构造绑定、OnIntent 路由（458 行起）、导航状态（676 行起）、帧更新和投影（767 行起）。先核对已有 NavigationBindings、LaunchFlow、Process、Startup 等分部，扩充对应文件或新增不冲突分部。保留核心生命周期和订阅、Dispose、状态发布顺序。

### S10 — 865 行：`Nexa.Services.Common/Downloads/DownloadService.cs`

主文件保留入口和操作注册；顺序传输（168 行起）、分段传输（357 行起）、进度聚合（581 行起）和 DownloadOperation 嵌套类型分别组织。保持私有可见性、断点偏移、范围请求、校验/信任、取消订阅计数及释放顺序，不修改重试或下载策略。

### S11 — 858 行：`Nexa.Services.Accounts/Accounts/LittleSkinOAuthService.cs`

按 device flow、授权码/刷新 token、玩家列表和外观操作、HTTP/错误映射及 JSON 读取划分 partial，仍为同一公共服务。保持 OAuth 字段、取消、错误分类、敏感信息处理及响应释放。该文件有解析告警，实际拆分后必须用 .NET 编译器验证。

### S12 — 824 行：`tests/Nexa.Services.Tests/Program.cs`

长测试表按 Accounts、Settings、Minecraft、Downloads、Updates 等领域拆成注册工厂；Program 留 Main，断言辅助另放同类型分部。拼接后的用例名字、顺序、数量和过滤行为必须逐项一致，不删注册，不改框架。

### S13 — 808 行：`Nexa.Pxml.Generators/PxmlControlCatalogGenerator.cs`

Initialize 留主文件；ParseCatalog/ParseProperty、验证函数、Emit/BuildSource 和内部定义模型分别组织 partial。保持 incremental pipeline、诊断 ID、生成文件 hint name 和生成源码内容。raw string 内的生成代码不是真实源类型，不能按普通类型搬出。

## 5. 规则生命周期审计

完整快照仅有根级 AGENTS.md，未发现 CLAUDE.md 或嵌套同名规则文件。现有 13 条均有唯一编号和 Why。**本轮确认失效的旧规则为 0 条，AGENTS.md 保持原样。** 上一轮已完成重叠表述归并和逐条理由；本轮不制造规则删改。

| 规则 | 判断与证据 |
|---|---|
| GIT-01 | 保留：完成后提交/推送属于工作流政策，已有 WIP、用户禁推和目标不明例外。 |
| GIT-02 | 保留：目标仍为 refactor/xsr，不能误投默认 dev。 |
| GIT-03 | 保留：提交可审阅性约束，不是寻找调用者的实现。 |
| GIT-04 | 保留：破坏性操作需确认，清理不豁免。 |
| XSR-01 | 保留：分支隔离仍适用，本轮未操作 dev 工作目录。 |
| XSR-02 | 保留：NexaCL.slnx 与项目图仍为 XSR，不引入旧实现是迁移政策。 |
| XSR-03 | 保留：docs/xsr/README.md 仍声明规范基线，project-references.json 由架构检查读取。 |
| XSR-04 | 保留：tests/Nexa.Xsr.ArchitectureTests/Program.Semantic.cs 仍检查服务对 UI/Desktop/Avalonia 的依赖。 |
| XSR-05 | 保留：同一检查拒绝 renderer 直接进入 Sidecar 执行边界；Nexa.UI.Next 项目仍存在。 |
| XSR-06 | 保留：SidecarFrameCodec 的二进制帧和 SidecarHostApi 的数字路由/二进制调用仍存在；没有规则失效依据。 |
| XSR-07 | 保留：迁移记录及 xsr-ci.yml 的 runtime/contract/NativeAOT 检查仍存在；本轮未执行这些测试。 |
| XSR-08 | 保留：docs/xsr/README.md 仍禁止靠更新 API baseline 洗白破坏性变更；DomainTypeForwards.cs 也体现兼容转发仍有职责。 |
| XSR-09 | 保留：versioning.md、Xsr.Version.props 与 Directory.Build.props 仍使用该产品版本体系。 |

没有规则违规、规则不直接对应某个函数、或当前实现通过检查，都不能推出规则过时。TypeForwardedTo、协议标识、native 布局字段、公开 API 和历史更新兼容不能按调用少直接删除。

## 6. 70 项目覆盖矩阵

每行代表完成物理文件/行数、项目配置与语法初筛，不代表人工逐行审完。项目名与同目录 .csproj 对应，tests/tools 保留目录前缀。行数为 C# 物理行。

| 项目 | 文件 | 行数 | >800 |
|---|---:|---:|---:|
| Nexa.Contracts | 2 | 148 | 0 |
| Nexa.Core | 2 | 130 | 0 |
| Nexa.Desktop | 141 | 23504 | 1 |
| Nexa.Domain | 8 | 754 | 0 |
| Nexa.Jvm.Host | 5 | 498 | 0 |
| Nexa.Platform.Abstractions | 12 | 188 | 0 |
| Nexa.Platform.Runtime | 37 | 5582 | 0 |
| Nexa.Pxml.Compiler | 5 | 929 | 0 |
| Nexa.Pxml.Generators | 1 | 808 | 1 |
| Nexa.Pxml.Runtime | 1 | 247 | 0 |
| Nexa.Services | 14 | 2598 | 0 |
| Nexa.Services.Accounts | 23 | 5713 | 1 |
| Nexa.Services.Accounts.Contracts | 15 | 582 | 0 |
| Nexa.Services.Capabilities | 7 | 1394 | 0 |
| Nexa.Services.Capabilities.Contracts | 12 | 998 | 0 |
| Nexa.Services.Common | 29 | 4154 | 1 |
| Nexa.Services.Common.Contracts | 17 | 567 | 0 |
| Nexa.Services.Composition | 26 | 1757 | 0 |
| Nexa.Services.Foundation.Contracts | 2 | 45 | 0 |
| Nexa.Services.Minecraft.Assets.Contracts | 1 | 35 | 0 |
| Nexa.Services.Minecraft.Crash.Contracts | 1 | 58 | 0 |
| Nexa.Services.Minecraft.Downloads.Contracts | 2 | 66 | 0 |
| Nexa.Services.Minecraft.Install | 35 | 5038 | 0 |
| Nexa.Services.Minecraft.Install.Contracts | 12 | 246 | 0 |
| Nexa.Services.Minecraft.Java | 12 | 1874 | 0 |
| Nexa.Services.Minecraft.Java.Contracts | 6 | 383 | 0 |
| Nexa.Services.Minecraft.Launch | 7 | 2524 | 1 |
| Nexa.Services.Minecraft.Launch.Contracts | 8 | 258 | 0 |
| Nexa.Services.Minecraft.Library.Contracts | 11 | 405 | 0 |
| Nexa.Services.Minecraft.Management | 37 | 5164 | 0 |
| Nexa.Services.Minecraft.Management.Contracts | 21 | 368 | 0 |
| Nexa.Services.Minecraft.Metadata | 27 | 3692 | 0 |
| Nexa.Services.Minecraft.Process | 7 | 1286 | 0 |
| Nexa.Services.Minecraft.Process.Contracts | 8 | 278 | 0 |
| Nexa.Services.Network | 5 | 464 | 0 |
| Nexa.Services.Network.Contracts | 4 | 114 | 0 |
| Nexa.Services.Recovery.Storage | 4 | 537 | 0 |
| Nexa.Services.Resources | 16 | 1945 | 0 |
| Nexa.Services.Resources.Contracts | 2 | 130 | 0 |
| Nexa.Services.Rollouts | 1 | 122 | 0 |
| Nexa.Services.Rollouts.Contracts | 1 | 15 | 0 |
| Nexa.Services.Settings | 14 | 2279 | 0 |
| Nexa.Services.Settings.Contracts | 9 | 648 | 0 |
| Nexa.Services.Setup | 7 | 1300 | 0 |
| Nexa.Services.Setup.Contracts | 4 | 104 | 0 |
| Nexa.Services.Telemetry | 7 | 783 | 0 |
| Nexa.Services.Telemetry.Contracts | 3 | 38 | 0 |
| Nexa.Services.Updates | 32 | 4122 | 0 |
| Nexa.Services.Updates.Contracts | 15 | 696 | 0 |
| Nexa.Sidecar.Protocol | 18 | 1778 | 0 |
| Nexa.Sidecar.Transport | 6 | 919 | 0 |
| Nexa.UI.Next | 36 | 6170 | 2 |
| Nexa.UI.Next.Backend.Avalonia | 64 | 8507 | 1 |
| Nexa.UI.Next.Benchmarks | 2 | 358 | 0 |
| Nexa.Update.Helper | 1 | 52 | 0 |
| Nexa.Xsr.Abstractions | 15 | 753 | 0 |
| Nexa.Xsr.Diagnostics | 1 | 144 | 0 |
| Nexa.Xsr.Runtime | 36 | 6134 | 0 |
| Nexa.Xsr.State | 11 | 1534 | 0 |
| tests/Nexa.Desktop.Tests | 96 | 16031 | 1 |
| tests/Nexa.Pxml.Tests | 7 | 1073 | 0 |
| tests/Nexa.Services.Tests | 191 | 32579 | 3 |
| tests/Nexa.Sidecar.Tests | 8 | 1168 | 0 |
| tests/Nexa.UI.Next.Backend.Avalonia.Tests | 29 | 4109 | 1 |
| tests/Nexa.UI.Next.Tests | 18 | 3462 | 0 |
| tests/Nexa.Xsr.ArchitectureTests | 2 | 808 | 0 |
| tests/Nexa.Xsr.Runtime.Tests | 26 | 6936 | 0 |
| tools/CapabilityProbe | 1 | 118 | 0 |
| tools/Nexa.Minecraft.Benchmarks | 1 | 182 | 0 |
| tools/Nexa.Xsr.Patch.Compiler | 3 | 305 | 0 |

## 7. 验证缺口

以下 11 个文件产生语法树解析告警，不能把它们直接称为编译错误：

- Nexa.Desktop/DesktopJumpList.cs
- Nexa.Jvm.Host/NativeJvmHost.cs
- Nexa.Platform.Runtime/Capabilities/MachineInstanceCapabilityProviders.cs
- Nexa.Pxml.Generators/PxmlControlCatalogGenerator.cs
- Nexa.Services.Accounts/Accounts/LittleSkinOAuthService.cs
- Nexa.UI.Next.Backend.Avalonia/AvaloniaUiSceneSurface.cs
- Nexa.UI.Next.Backend.Avalonia/AvaloniaUiWindowsMediaSession.cs
- tests/Nexa.Services.Tests/InstalledContentRefreshTests.cs
- tests/Nexa.Sidecar.Tests/Program.cs
- tests/Nexa.UI.Next.Backend.Avalonia.Tests/NativeCornerTests.cs
- tests/Nexa.Xsr.Runtime.Tests/CoreSnapshotApiTests.cs

本地没有 .NET SDK；没有执行 dotnet build、Roslyn 全程序引用解析、可执行测试、NativeAOT publish、UI 真机或多系统回归。临时审计工作流只导出源码和解析工具，其成功不等于产品测试成功；工作流已从隔离审计分支移除，从未加入目标分支。

文本筛查不能证明动态反射、生成输出、外部消费者和条件编译下绝无引用。公开 API、兼容转发、协议与 native 入口未纳入自动删除名单。本轮没有删后行为等价验证，因为根本没有源码删除。

## 8. 建议实施顺序

第一批考虑未超限文件中的 C01/C02/C04/C07 与 D01/D02，分开提交并运行相关编译/回归。C03 需核验状态链，同时保留世界刷新活跃逻辑。D03 确认无顺序依赖后再去重。

S01–S13 先进行保持类型不变的职责拆分并单独验证；C05/C06 后置。D04–D07 按收益及绑定/生命周期风险选择是否归并，允许保留合理的小段重复，不以消灭所有重复为目标。

本轮停在清单和拆分方案，没有自动开始上述实施。完整附件另含逐文件行数及 SHA-256、语法候选和解析告警 JSON、只读复核脚本；大型机器输出不加入产品分支。
