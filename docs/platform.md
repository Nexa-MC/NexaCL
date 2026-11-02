# 平台与进程

`src/platform/Nexa.Platform.Abstractions` 定义便携接口，`Nexa.Platform.Runtime` 提供操作系统实现。Windows Explorer 集成的 C++ 工程位于 `src/platform/Nexa.ExplorerCommand`。macOS Swift 辅助程序由 `eng/xsr/Xsr.PlatformHelper.targets` 构建。

产品支持 Windows、Linux、macOS 的 x64 和 ARM64。原生窗口必须遵守系统主线程和生命周期约束。平台能力以实际探测结果和权限为准，不能因为存在设置项就宣称系统已支持或已应用。

Minecraft 进程由 `Nexa.Jvm.Host` 承担受控启动与会话所有权；启动参数、Java 运行时、环境和策略通过显式契约传递。进程创建、退出、资源租约与窗口关闭遵循取消和异步回收规则。系统凭据访问通过平台接口进入账户服务，不把秘密投影到 UI 状态。

安装器、协议关联、文件类型、托盘和系统动作由各平台的代码及集成测试验证。用户作用域和系统作用域安装需要不同权限，不能将下载内容直接作为提升权限的执行入口。

操作系统资源压力策略与 Minecraft 内存估算是两套机制。后者仍停用，不参与启动预检或 JVM 自动内存分配。云 CI 不能代替真实设备上的 SmartScreen、Gatekeeper、Steam 客户端或特定文件系统权限验收。
