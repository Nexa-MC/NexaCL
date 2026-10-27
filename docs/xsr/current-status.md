# 当前实现与文档边界

源码基线：`b1dffb61` / `v2.0.0.alpha.14`，2026-10-27（Asia/Shanghai）。

## 已接通的生产能力

- XSR 状态、命令／查询、事件、调度与生命周期；Desktop 组合服务，Renderer 只读状态和产生意图。
- 共享 NXML/NSS 呈现、NTP/1 主题与实时选项。Sidecar UI 已支持 NXML schema 3 与独立页面；旧 schema 1/2 仍兼容，不能再用“仅资源卡片”概括当前能力。
- 账户、实例、Java、模组／整合包安装更新、跨启动器迁移、备份、重试、签名完整／差分更新。
- 平台资源治理与原生集成详见[平台手册](../platform-integration.md)。第 1、2 批均已接入生产路径；设备／权限不足仍须明确失败或采用已定义回退。
- 运行遥测包含启动配置、元数据分页、周期样本和结束汇总；详见[运行诊断](../runtime-diagnostics.md)。后台协议与管理页已部署，不等于每台用户设备都有全部计数器。

## 明确保留的范围

Minecraft 内存分析保持停用，不进入启动预检或 JVM 自动内存分配。操作系统内存压力管理是另一条已启用路径。

局域网发现／直传／接力／串流、云账户能力、AI 后端、HDR／VRR／OLED／多屏及后续触控陀螺仪适配不在本批交付内。没有虚构通用 GameBoost 开关，也不把普通震动称为摇杆阻尼。硬件实测范围与云 CI 分开记录。

## 设置与旧规划

当前键定义以 `Nexa.Services.Settings.Contracts/Settings/SettingsPolicySchema.cs` 为准；平台可用性由 `SettingsCatalog.cs` 决定，实际页面再筛除无操作价值的条目。历史 IA 中的 Group/Choice/Action/State、保留键和规划行不代表当前有对应设置控件。已删除低功耗模式及用户要求去除的诊断／目录信息面板。

此前 `alpha5-gap-status`、`settings-next-development-plan`、`completion-capability-audit`、`runtime-catalog-consumer-audit`、XSR-795 和 XSR-799 是重复阶段清单，包含已经解决或撤销的缺口，现移除；原文可从 Git 历史查看。详细迁移契约及原始验收材料保留，不作为当前待办。

## 验证证据

- 本地：838 Services、374 Desktop、88 发布工具测试通过；72 项目架构检查通过。
- [XSR CI 40431462367](https://github.com/Nexa-MC/NexaCL/actions/runs/40431462367)：21 项任务通过，包含真实隔离 Btrfs 快照及三平台原生验证。
- [六平台候选构建 40431462462](https://github.com/Nexa-MC/NexaCL/actions/runs/40431462462)：通过；Windows ARM64 首次运行器卡住，换运行器重试成功。
- [Alpha 14 发布构建](https://github.com/Nexa-MC/NexaCL/actions/runs/40434372674)：六平台、差分往返、签名与构件上传全部通过；公开发布与更新源状态分别以对应 Release 为准。

以上不代替真实 Steam 客户端、Siri 语音或用户 ZFS 权限环境的设备验收。
