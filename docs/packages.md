# 共享包

共享包由 `eng/packages/manifest.json` 定义，与产品 Alpha/Beta 版本独立。当前闭包包含 Core、Contracts、Sidecar.Protocol、Sidecar.Transport、XSR Abstractions、Diagnostics、State、Runtime，以及独立 BuildTools；Renderer 和平台实现不包含在该闭包中。

```sh
python eng/packages/build.py --output /absolute/path/to/feed
python eng/packages/verify.py --feed /absolute/path/to/feed
python eng/packages/verify.py --feed /absolute/path/to/feed --aot
```

需要匹配的 .NET 10 SDK、Python 3 和本地编译依赖。脚本生成本地 feed 与记录源 SHA、依赖、包 SHA-256 的清单；构建不等于已发布到公共注册表。

运行时包包含程序集、许可证、文档、匹配 `.nfp` 与复制目录的 buildTransitive 目标。BuildTools 包仅提供构建工具及其私有依赖，不能将 Roslyn、Mono.Cecil 带入产品。消费方显式引用 `PrivateAssets=all` 的 BuildTools 并启用 `NexaAutomaticFunctionPatches=true` 才会插桩自己的代码。

验证使用独立包消费项目和新缓存，检查命令接受／完成、状态、插桩安装／退休、增量编译及托管／AOT 输出。消费方不依赖 NexaCL 工作区路径。只使用本地 Runtime 不会自动启动 Sidecar 或开启远程插件。

已发布包版本与字节保持不变，内容变化必须使用新包版本。源码目录位置以 `eng/architecture/projects.json` 为准；包 API 身份不因目录迁移而改变。
