[English](README.md) | 简体中文

# NexaCL Firefly — 衍生开发与贡献

本仓库面向 **NexaCL 衍生项目开发者、功能贡献者和维护者**。它包含基于 .NET 10、NativeAOT 与 XSR 的 Minecraft Java 启动器源码，支持 Windows、macOS、Linux 的 x64 和 ARM64。

普通用户下载安装包及阅读版本说明请访问 [Releases](https://github.com/Nexa-MC/NexaCL/releases)；这里的说明用于理解、构建和修改源码。

## 从哪里开始

| 目标 | 入口 |
| --- | --- |
| 开发衍生版本 | [衍生开发说明](docs/derivative-development.zh-CN.md) |
| 提交功能或修复 | [CONTRIBUTER.md](CONTRIBUTER.zh-CN.md) |
| 查找功能代码 | [目录结构](docs/repository-layout.md)、[完整项目索引](docs/modules.md) |
| 理解架构与 DLL 分工 | [架构](docs/architecture.zh-CN.md)、[原生模块](docs/native-modules.zh-CN.md) |
| 验证、打包与发行 | [测试](docs/testing.zh-CN.md)、[构建](docs/building.zh-CN.md)、[发布](docs/releasing.zh-CN.md) |
| 修改界面、主题或插件边界 | [呈现](docs/presentation.md)、[主题](docs/themes.md)、[Sidecar](docs/sidecar.md) |

## 本地运行

安装 `global.json` 指定的 .NET SDK，准备 Python 3；原生编译还需要对应平台的工具链。以下命令从仓库根目录执行：

```sh
dotnet restore NexaCL.slnx
dotnet build NexaCL.slnx -c Release -m:1
dotnet run --project src/app/Nexa.Desktop -c Release --no-build
```

直接运行 Desktop 是托管开发路径。正式发行使用 `eng/release/publish_launcher.py` 构建小型 Bootstrap、Host、UI 和独立业务／引擎模块，不能把一个开发 EXE 当作完整发布包。

## 源码组织

`src/app` 保存入口与组合；`foundation`、`runtime`、`rendering` 保存基础层、XSR 和呈现；`modules/<feature>` 将同一业务的契约、实现、客户端和原生入口放在一起；`engines`、`platform`、`sidecar`、`interop` 分别保存引擎、系统集成、进程通信和 ABI 基础。

`tests` 对应功能分组，`tools` 保存编译器、探针和基准，`eng` 保存工程与发布脚本。项目位置由 `eng/architecture/projects.json` 记录，允许依赖由 `project-references.json` 锁定。项目身份、源码目录与最终 DLL 是不同层面的清单。

## 开发边界

遵循 [Apache License 2.0](LICENSE) 及第三方依赖的许可和通知要求。衍生发行应配置自己的产品身份、服务凭据、更新来源及签名体系；本仓库不会提供上游私钥或私人服务凭据。

提交代码前阅读 [贡献说明](CONTRIBUTER.zh-CN.md)，修改信任或业务边界时同时维护 [项目文档](docs/README.zh-CN.md)。当前实现的正版资格、区域与更新信任规则见 [安全说明](docs/security.zh-CN.md)。不要将编译产物、缓存、个人配置或凭据提交到仓库。
