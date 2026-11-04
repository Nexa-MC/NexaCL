[English](CONTRIBUTER.md) | 简体中文

# 贡献者指南

本指南面向 NexaCL 的代码、文档、测试和平台集成贡献者。衍生项目的独立发行配置见 [衍生开发](docs/derivative-development.zh-CN.md)。

## 准备开发环境

使用 `global.json` 指定的 .NET SDK、Python 3 和目标平台原生工具链。先阅读 [目录结构](docs/repository-layout.md) 与 [架构](docs/architecture.zh-CN.md)，从仓库根目录恢复并构建：

```sh
dotnet restore NexaCL.slnx
dotnet build NexaCL.slnx -c Release -m:1
```

不要并行启动会写入相同项目 `bin/obj` 的构建。API 凭据、OAuth 身份、签名私钥与本地路径只进入受控配置，不进入提交或日志。

## 选择修改位置

业务逻辑进入 `src/modules/<feature>/`；先确认契约、实现和实际运行所有者。UI 页面进入 `src/app/Nexa.Desktop/Ui/`，通用 Renderer 在 `src/rendering/`。Composition 负责绑定路由与资源端口，不能成为收容所有业务逻辑的目录。

Renderer 只消费状态、产生意图；服务不依赖 Desktop、Avalonia 或 UI 对象。原生 ABI 与 Sidecar 不传递 CLR 对象、Task、Stream、托管异常。协议变更需要明确版本、预算、取消、回收与兼容规则。

新增或移动项目同步更新 `NexaCL.slnx`、`eng/architecture/projects.json`、相关工程脚本与文档。依赖许可不是路径清单；只有经过架构审查的真实依赖变更才调整 `project-references.json`。禁止用扩大基线掩盖错误引用。

## 验证修改

先运行直接相关的测试，再执行必要的完整检查，命令见 [测试说明](docs/testing.zh-CN.md)。本仓库大部分测试项目通过可执行入口运行，不以没有执行用例的 `dotnet test` 成功代替验证。

功能或缺陷修复需要覆盖实际触发条件与结果。修改 UI 检查深浅主题、窄窗口、键盘与手柄、失败和取消状态；修改平台代码区分模型测试、原生 CI 与真实设备验收。原生模块或目录调整需要编译、架构、自动插桩、AOT 和对应平台打包检查。

修改生成协议后运行生成器及 `--check`，不要手改生成文件。发布工具变更运行其 Python 回归；文档改动检查链接和命令。未执行或受环境限制的检查应明确写出。

## 提交与评审

一次提交围绕明确问题，保持现有常规提交标题风格，例如 `fix(updates): ...` 或 `refactor(repo): ...`。说明触发条件、最终行为和验证结果；涉及公开接口、安全或数据格式时说明兼容和恢复方式。

提交前检查差异，不包含 `bin/`、`obj/`、`artifacts/`、缓存、临时预览、个人 IDE 文件或 Secrets。保留必要的测试夹具、锁文件、授权信息和第三方通知。旧实现只作行为参考，不复制旧项目图或使用工作站相对引用。

## 文档与许可

文档描述当前可验证行为，避免把计划或历史成功数字写成当前事实。修改边界前先更新对应架构说明，再修改代码与消费方。历史发行说明由 GitHub Releases 保存。

项目采用 [Apache License 2.0](LICENSE)。提交或再分发时遵循项目及第三方依赖许可，保留适用的版权、许可和通知。代理协作额外遵循 [AGENTS.md](AGENTS.md)。
