# Settings value contracts

基线：Alpha 14 / b1dffb61。以下 109 个定义直接从当前 `SettingsPolicySchema.Definitions` 导出，不能用历史 IA 数量推断当前控件数。

平台限制由 `SettingsCatalog` 决定，页面只建立实际可编辑的 Available Setting；Action/State/Group/Choice 不等于持久化设置。`java.compatibility` 仅保留兼容读取并拒绝修改。预发布诊断由通道策略强制，不能通过编辑值绕过。

G/I 表示全局值允许实例覆盖；Auto 表示允许无 payload 的 Auto 模式。reset 删除覆盖，未填写值不等于写入字符串 null。范围是 schema 数值边界，文本附加验证仍以 `Validate` 为准。机器路径、CPU 标识、凭据等本地键不能凭表格导出标记扩大权限。

| Key | Type | Default | Scope | Auto | Range / choices / unit | Applies | Export |
|---|---|---|---|---|---|---|---|
| general.language | Enum | auto | G | False | auto / zh-Hans / zh-Hant / en | Immediate | True |
| general.region | Text | auto | G | False |  | Restart | True |
| general.single-instance | Boolean | true | G | False |  | Restart | True |
| general.tray | Boolean | true | G | False |  | Immediate | True |
| general.close-to-tray | Boolean | false | G | False |  | Immediate | True |
| general.minimize-to-tray | Boolean | false | G | False |  | Immediate | True |
| appearance.animations-disabled | Boolean | false | G | False |  | Immediate | True |
| appearance.animation-fps | Number | 60 | G | False | 1..240 fps | Immediate | True |
| appearance.lock-window | Boolean | false | G | False |  | Immediate | True |
| appearance.hardware-acceleration-disabled | Boolean | false | G | False |  | Restart | True |
| appearance.theme-mode | Enum | 2 | G | False | 2 / 0 / 1 | Immediate | True |
| appearance.theme-pack-id | Text | builtin | G | False |  | Immediate | True |
| appearance.launcher-icon-mode | Enum | theme | G | False | theme / default / custom | Immediate | True |
| appearance.launcher-icon-path | Text | (empty) | G | False |  | Immediate | False |
| appearance.accent | Enum | blue | G | False | blue / purple / green / orange | Immediate | True |
| java.runtime | Path | (empty) | G/I | True |  | NextLaunch | False |
| java.auto-install | Boolean | false | G/I | False |  | NextLaunch | True |
| java.vendor | Enum | (empty) | G/I | False |  / EclipseTemurin / Microsoft / Zulu / Oracle / Liberica / Corretto / IbmSemeru / Dragonwell / TencentKona / OpenJdk / GraalVmCommunity / JetBrains / Unknown | NextLaunch | True |
| java.compatibility | Boolean | true | G/I | False |  | NextLaunch | False |
| instance.development-mode | Boolean | false | G/I | False |  | Immediate | False |
| recovery.keep-history | Boolean | false | G/I | False |  | NextTask | False |
| game.memory | Number | 2048 | G/I | True | 256..1048576 MiB | NextLaunch | True |
| game.gpu-preference | Enum | auto | G/I | False | auto / secondary | NextLaunch | True |
| game.renderer | Enum | auto | G/I | False | auto / mesa-software | NextLaunch | True |
| game.system-glfw | Boolean | false | G/I | False |  | NextLaunch | True |
| game.window-mode | Enum | windowed | G/I | False | windowed / fullscreen | NextLaunch | True |
| game.width | Number | 854 | G/I | False | 1..32768 px | NextLaunch | True |
| game.height | Number | 480 | G/I | False | 1..32768 px | NextLaunch | True |
| game.title | Text | (empty) | G/I | False |  | NextLaunch | True |
| game.default-isolation | Enum | all | G | False | none / loaders / non-release / loaders-or-non-release / all | NextTask | True |
| game.launcher-visibility | Enum | keep | G/I | False | keep / minimize / hide / hide-and-close | NextLaunch | True |
| game.jvm | Text | -XX:+UseG1GC -XX:-UseAdaptiveSizePolicy -XX:-OmitStackTraceInFastThrow -Djdk.lang.Process.allowAmbiguousCommands=true -Dfml.ignoreInvalidMinecraftCertificates=True -Dfml.ignorePatchDiscrepancies=True -Dlog4j2.formatMsgNoLookups=true | G/I | False |  | NextLaunch | False |
| game.arguments | Text | (empty) | G/I | False |  | NextLaunch | False |
| game.wrapper | Text | (empty) | G/I | False |  | NextLaunch | False |
| game.pre-launch | Text | (empty) | G/I | False |  | NextLaunch | False |
| game.pre-launch-wait | Boolean | true | G/I | False |  | NextLaunch | True |
| game.auto-repair | Boolean | true | G/I | False |  | NextLaunch | True |
| game.server | Text | (empty) | G/I | False |  | NextLaunch | True |
| game.process-memory-limit-mib | Number | 0 | G/I | False | 0..16777216 MiB | NextLaunch | False |
| game.io-read-limit-mib | Number | 0 | G/I | False | 0..1048576 MiB/s | NextLaunch | False |
| game.io-write-limit-mib | Number | 0 | G/I | False | 0..1048576 MiB/s | NextLaunch | False |
| game.cpu-quota-percent | Number | 0 | G/I | False | 0..102400 % | NextLaunch | False |
| game.gamemode | Boolean | false | G/I | False |  | NextLaunch | False |
| game.cpu-policy | Enum | system | G/I | False | system / performance / cache / numa | NextLaunch | False |
| game.cpu-sets | Text | (empty) | G/I | False |  | NextLaunch | False |
| game.process-quality | Enum | default | G/I | False | default / efficiency / background | NextLaunch | True |
| game.process-priority | Enum | normal | G/I | False | normal / below-normal / above-normal / high / real-time | NextLaunch | True |
| network.proxy-mode | Enum | 1 | G | False | 0 / 1 / 2 | NextTask | True |
| network.proxy-address | Text | (empty) | G | False |  | NextTask | False |
| network.proxy-user | Text | (empty) | G | False |  | NextTask | False |
| network.proxy-password | Text | (empty) | G | False |  | NextTask | False |
| network.doh | Boolean | true | G | False |  | NextTask | True |
| network.ip-stack | Enum | auto | G | False | auto / ipv4 / ipv6 | NextTask | True |
| network.bandwidth-kib | Number | 0 | G | False | 0..1048576 KiB/s | NextTask | True |
| network.file-concurrency | Number | 8 | G | False | 1..64 files | NextTask | True |
| network.file-retry | Boolean | true | G | False |  | NextTask | True |
| network.game-source | Enum | official-first | G | False | official-first / mirrors-first / official-only | NextTask | True |
| install.inherit-vanilla | Boolean | false | G | False |  | NextTask | True |
| diagnostics.telemetry | Boolean | false | G | False |  | Immediate | True |
| diagnostics.log-level | Enum | auto | G | False | auto / 0 / 1 / 2 / 3 / 4 | Immediate | True |
| diagnostics.log-lines | Number | 500 | G | False | 50..2000 entries | Immediate | True |
| diagnostics.disk-log-days | Number | 7 | G | False | 1..90 days | Immediate | True |
| updates.channel | Enum | build | G | False | build / stable / alpha / beta / ci | NextTask | True |
| updates.auto-check | Boolean | true | G | False |  | NextTask | True |
| game.environment | Text | (empty) | G/I | False |  | NextLaunch | False |
| game.classpath-head | Text | (empty) | G/I | False |  | NextLaunch | False |
| game.post-exit | Text | (empty) | G/I | False |  | NextLaunch | False |
| game.safe-launch | Boolean | false | G/I | False |  | NextLaunch | True |
| general.autostart | Boolean | false | G | False |  | Immediate | True |
| general.file-association | Boolean | false | G | False |  | Immediate | True |
| general.native-notifications | Boolean | true | G | False |  | Immediate | True |
| general.clipboard-detection | Boolean | false | G | False |  | Immediate | True |
| appearance.window-opacity | Number | 100 | G | False | 40..100 % | Immediate | True |
| appearance.window-blur | Boolean | false | G | False |  | Immediate | True |
| network.provider-modrinth | Boolean | true | G | False |  | NextTask | True |
| network.provider-curseforge | Boolean | true | G | False |  | NextTask | True |
| network.provider-official | Boolean | true | G | False |  | NextTask | True |
| network.provider-mirror | Boolean | true | G | False |  | NextTask | True |
| network.trace | Boolean | false | G | False |  | NextTask | True |
| network.auto-diagnose | Boolean | false | G | False |  | NextTask | True |
| appearance.custom-theme | Text | (empty) | G | False |  | Immediate | True |
| appearance.logo-path | Text | (empty) | G | False |  | Immediate | False |
| appearance.background-path | Text | (empty) | G | False |  | Immediate | False |
| appearance.background-fit | Enum | cover | G | False | cover / contain / stretch | Immediate | True |
| appearance.background-color | Text | auto | G | False |  | Immediate | True |
| appearance.reduced-motion | Boolean | false | G | False |  | Immediate | True |
| appearance.background-opacity | Number | 100 | G | False | 0..100 % | Immediate | True |
| appearance.video-path | Text | (empty) | G | False |  | Immediate | False |
| appearance.video-auto-pause | Boolean | true | G | False |  | Immediate | True |
| music.enabled | Boolean | false | G | False |  | Immediate | True |
| music.path | Text | (empty) | G | False |  | Immediate | False |
| music.startup | Boolean | false | G | False |  | Restart | True |
| music.autoplay | Boolean | true | G | False |  | Immediate | True |
| music.shuffle | Boolean | false | G | False |  | Immediate | True |
| music.volume | Number | 50 | G | False | 0..100 % | Immediate | True |
| music.media-controls | Boolean | true | G | False |  | Immediate | True |
| general.jump-list | Boolean | true | G | False |  | Immediate | True |
| general.notification-actions | Boolean | true | G | False |  | Immediate | True |
| general.startup-page | Enum | launch | G | False | launch / install / resources / settings / java / storage / about / tasks | NextLaunch | True |
| general.launch-hints | Boolean | true | G | False |  | Immediate | True |
| network.auto-install-dependencies | Boolean | true | G | False |  | NextTask | True |
| network.resource-source | Enum | follow-request | G | False | follow-request / official-first / mirrors-first | NextTask | True |
| music.auto-pause | Boolean | true | G | False |  | Immediate | True |
| network.metered-download | Enum | auto | G | False | auto / allow / restrict | NextTask | True |
| network.background-download | Boolean | true | G | False |  | NextTask | True |
| diagnostics.ai.enabled | Boolean | false | G | False |  | Immediate | True |
| diagnostics.ai.reasoning | Enum | provider | G | False | provider / low / medium / high | Immediate | True |
| storage.backup-keep-count | Number | 32 | G | False | 1..1024 backups | Immediate | True |
| developer.enabled | Boolean | false | G | False |  | Immediate | True |

低功耗模式已移除；旧 `appearance.low-power` / `UiUltraLowPowerMode` 字段不再消费。动画帧率、禁用动画与 reduced-motion 仍有独立作用。

新增资源策略的使用、平台前提与失败行为见[平台手册](../platform-integration.md)。

## Committed effective reads

Effective queries read one immutable committed revision and values snapshot without
acquiring the store's persistence gate. Ordinary pending saves leave readers on the previous
snapshot; failed saves do not publish a new snapshot. Profile/Temporary queries serialize
with their lifecycle transactions under the profile coherence lock, so they are not claimed
to be lock-free during those transactions. The durable writer publishes the complete
snapshot before revision notifications.


## Scoped reset
A sealed reset preview captures the committed revision and proposes Inherit mutations
only for available catalog-backed definitions overridden in the requested layer. Global
reset preserves reserved keys and all instances; instance reset preserves global and
other-instance values. Apply recomputes, checks the expected revision and commits once.
No-op previews do not write. Persistence failure leaves the complete prior snapshot.
