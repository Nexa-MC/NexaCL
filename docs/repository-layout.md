# 仓库目录

源码以基础设施分层、业务能力归组。目录相邻不代表允许相互依赖。

```text
src/
  app/          产品入口、原生 Host、服务组合、JVM Host、更新助手
  foundation/   Core、Domain、公共 Contracts
  runtime/      XSR 抽象、状态、调度和诊断
  rendering/    Renderer、后端、NXML、窗口 ABI
  modules/      账户、网络、Minecraft、更新及其他业务能力
  engines/      PAC、Crypto、Media、Markup、Artwork
  platform/     平台接口、实现、Explorer 集成
  sidecar/      独立进程通信协议和传输
  interop/      原生调用、缓冲区、异步资源和通用服务 ABI
tests/          与源码对应的功能分组，Shared 保存测试共用夹具
tools/          compiler、probes、benchmarks
eng/            build、architecture、release、生成器和工程检查
docs/           当前项目文档
```

每个项目目录保留完整项目名。账户能力例如位于 `src/modules/accounts/`：`Nexa.Services.Accounts.Contracts` 是公共消息与状态，`Nexa.Services.Accounts` 是实现，`Nexa.Native.Accounts.Client` 是调用端，`Nexa.Native.Accounts` 是 NativeAOT 入口，`protocol/` 保存双端使用的协议源码。Minecraft、Network、Updates 使用相同归组方式。并非每个业务模块都需要原生客户端或独立发布 DLL。

`src/app/Nexa.Services` 保存现有 Foundation 组合、兼容转发和 Minecraft 适配边；`Nexa.Services.Composition` 绑定路由与资源端口。这些职责不等于一个通用业务实现层。`Nexa.Desktop` 保存产品 UI、控制器和托管开发入口；正式发布的同名 EXE 来自 `Nexa.Bootstrap`，UI 则编译为 `nexa-ui`。

通用 ABI 放在 `src/interop/`；业务专用协议放在相应模块的 `protocol/`。窗口协议在 `src/rendering/protocol/window/`，Host 准入目录在 `src/app/protocol/`。共享源码可被多个 NativeAOT 项目链接编译，不因此交换 CLR 对象或形成新的程序集。

项目、程序集、命名空间和发布文件名沿用既有身份。`NexaCL.slnx` 的分组对应磁盘位置；`eng/architecture/projects.json` 记录项目名到目录的映射。新增或移动项目必须同时更新这些入口及相关工程脚本。编译产物仍在各项目 `bin/`、`obj/`，不得提交。

文档重建与目录迁移的验证说明见 [迁移记录](repository-migration.md)。
