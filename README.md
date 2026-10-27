# NexaCL Firefly

Minecraft Java 启动器，基于 .NET 10、NativeAOT 与 XSR 状态／意图架构，支持 Windows、macOS、Linux 的 x64 和 ARM64。

[下载与发布说明](https://github.com/Nexa-MC/NexaCL/releases) · [签名更新源](https://github.com/Nexa-MC/NexaCL-Update/releases) · [网站](https://pcln.top) · [用户与开发文档](docs/README.md)

当前源码版本：**2.0.0.alpha.14**。源码版本不等于已公开发布状态，请以 Releases 为准。开发分支为 `Nexa-MC/NexaCL:main`；旧 `PCL-N` 仓库仅供历史参考。

- 管理游戏实例、账户、Java、模组与整合包，支持跨启动器导入、安装重试、备份和在线更新。
- 使用 NTP/1 主题包定制外观，通过独立进程 Sidecar 与共享 NXML 扩展界面和能力。
- 提供各平台原生集成、显式进程资源策略、内存压力下的缓存回收和 Sidecar 协作停用。
- 应用内更新校验发布签名，支持兼容版本差分与完整包回退。

正版资格检查适用于全球。中国大陆不提供 Minecraft 本体下载；整合包安装复用完整本地原版，缺失时引导通过官方 Minecraft Launcher 获取。详见[区域规则](docs/xsr/nsc-and-regional-policy.md)。

Minecraft 内存估算仍停用，不参与启动预检或自动分配 JVM 内存。局域网接力／串流、AI 后端及后续显示适配不在本轮交付范围。

## 开发

安装 `global.json` 指定的 .NET SDK，然后执行：

```sh
dotnet restore NexaCL.slnx
dotnet build NexaCL.slnx -c Release
dotnet run --project Nexa.Desktop -c Release
```

发布构建、原生依赖与验证入口见[构建和发布](docs/build-and-release.md)。架构约束见 [XSR 索引](docs/xsr/README.md)。
