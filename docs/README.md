# NexaCL 文档

基线：2026-10-27，`v2.0.0.alpha.14` / `b1dffb61`。当前行为以源码与本索引所列文档为准；历史发布说明和迁移记录描述当时版本，不是当前待办表。

## 使用与排错

- [平台集成与资源设置](platform-integration.md)：CPU、配额、Steam、系统预览、备份快照与可用性。
- [运行诊断、遥测与日志](runtime-diagnostics.md)：实测与未知值、日志位置、隐私、问卷和公告。
- [构建、验证与发布](build-and-release.md)：本地构建、六平台 CI、签名、差分和更新源。
- [跨启动器迁移](xsr/foreign-launcher-migration.md)、[整合包安装与更新](xsr/modpack-updates-and-install-validation.md)、[失败重试](xsr/task-retry.md)。
- [代理](xsr/network-proxy.md)、[区域规则](xsr/nsc-and-regional-policy.md)、[更新事务](xsr/automatic-update-transaction.md)。

## 扩展作者

详细的中英双语教程、NXML/NSS 语言与可执行示例由 [PCLN-Docs](https://github.com/Nexa-MC/PCLN-Docs) 维护，并发布到 [docs.pcln.top](https://docs.pcln.top)。

- [NTP/1](xsr/ntp-v1.md)、[NSS/1](xsr/nss-v1.md)、[主题实时设置](xsr/theme-live-options.md)。
- [Sidecar 协议](xsr/sidecar-protocol.md)、[Host API](xsr/sidecar-host-api.md)、[共享 NXML UI](xsr/sidecar-ui-modules.md)、[自动函数插桩](xsr/automatic-function-instrumentation.md)。
- [XSR 当前状态与文档分类](xsr/current-status.md)、[架构索引](xsr/README.md)。

## 记录保留规则

`releases/` 是不可倒写的版本记录；`xsr/migrations/`、`XSR-*.md` 和带日期／提交号的验收记录保留历史证据。其旧项目数、测试数、未完成项只适用于对应提交。过时的重复缺口清单已删除，统一由当前状态页解释；不把已通过的历史测试数字改写成当前验证数字。
