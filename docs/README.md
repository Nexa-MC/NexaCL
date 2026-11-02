# NexaCL 项目文档

本目录描述当前源码、构建和发布契约。历史版本说明在 [GitHub Releases](https://github.com/Nexa-MC/NexaCL/releases)，旧文档和历史验收附件可从 Git 历史查阅。这里不把过往测试数字当作当前验证结果。

| 需要做什么 | 阅读入口 |
| --- | --- |
| 定位功能源码 | [仓库目录](repository-layout.md)、[模块索引](modules.md) |
| 理解执行与依赖边界 | [架构](architecture.md)、[原生模块](native-modules.md) |
| 本地开发与检查 | [构建](building.md)、[测试](testing.md)、[贡献约定](../CONTRIBUTER.md) |
| 发布和更新 | [发布流程](releasing.md)、[自动更新](updates.md)、[版本](versioning.md) |
| 开发界面、主题和扩展 | [NXML 与 NSS](presentation.md)、[主题](themes.md)、[Sidecar](sidecar.md)、[函数插桩](instrumentation.md) |
| 平台行为和安全约束 | [平台](platform.md)、[安全与区域规则](security.md) |
| 构建衍生发行 | [衍生开发](derivative-development.md) |
| 使用共享包 | [共享包](packages.md) |

文档中的仓库相对路径从仓库根目录解析。项目身份、目录位置与最终发布文件是三个不同概念；源码项目表见 `eng/architecture/projects.json`，允许的项目依赖见 `eng/architecture/project-references.json`。
