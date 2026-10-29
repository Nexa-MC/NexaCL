# XSR 架构与实现文档

开发位置为 `Nexa-MC/NexaCL:main`。旧 `dev` 仅作只读行为参考，不恢复旧实现或项目图。当前状态见 [current-status.md](current-status.md)，面向使用者的入口见 [文档首页](../README.md)。

## 基础边界

[architecture](architecture.md) · [dependency-rules](dependency-rules.md) · [state-model](state-model.md) · [service-model](service-model.md) · [renderer-model](renderer-model.md) · [versioning](versioning.md) · [source-reference](source-reference.md)

## 界面与作者契约

[NXML](nxml-renderer.md) · [NSS/1](nss-v1.md) · [NTP/1](ntp-v1.md) · [主题架构](theme-package-architecture.md) · [主题资源](theme-resource-security.md) · [主题能力](theme-capabilities.md) · [实时选项](theme-live-options.md) · [Sidecar 协议](sidecar-protocol.md) · [Host API](sidecar-host-api.md) · [共享 NXML 模块](sidecar-ui-modules.md) · [自动函数插桩](automatic-function-instrumentation.md)

## 产品与运行边界

[平台集成](../platform-integration.md) · [本地平台实施记录](local-platform-roadmap.md) · [运行观测](launch-observation-and-preflight.md) · [遥测分级](telemetry-tiers-and-rollouts.md) · [内存分析停用](preflight-memory-analysis.md) · [下载信任](download-trust.md) · [安装恢复](resumable-installation.md) · [整合包更新](modpack-updates-and-install-validation.md) · [跨启动器导入](foreign-launcher-migration.md) · [资源调度](work-scheduling.md) · [手柄入口](gamepad-entry.md)

[系统安装](system-installation.md) · [自动更新](automatic-update-transaction.md) · [更新中心与版本化引导](update-center-and-versioned-onboarding.md) · [更新权限](update-privilege-boundary.md) · [发布签名](release-signatures.md) · [签名策略](update-signature-policy.md) · [构建发布流程](../build-and-release.md)

## 设置

[值契约](settings-value-contracts.md) · [迁移记录](settings-migration-status.md) · [历史 IA 位置表](settings-entry-map.md) · [历史 IA 设计输入](settings-ia-source.md)。目录位置不等于当前可见控件，具体行为以键定义、可用性判定与消费方为准。

## 历史证据

[migrations/](migrations/) 和本目录 `XSR-*.md` 是逐项迁移记录；带日期、提交号及 Alpha 版本的验收报告描述当时状态。保留证据不代表旧 UI、旧分支或旧待办仍有效。[NXML 闭环记录](nxml-migration-remaining.md)与[evidence/](evidence/)供回溯，新增功能仍须有自己的验证。
