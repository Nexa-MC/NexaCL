# Windows 本机检查 — 2026-10-30

## 环境与结论边界

本次在 Windows 11 Pro Insider Preview **29695.1001**（`rs_prerelease`）、
125% 缩放、.NET SDK 10.0.403 上运行。显示设备包括 NVIDIA GeForce RTX
4070 Ti SUPER 和 USB Mobile Monitor Virtual Display。

本报告区分真实 HWND/API 验证、自动化合同测试和未验证交互；不能据此宣称
所有 Windows 功能均已完整验收。没有运行完整 Minecraft、安装更新、改变
用户默认应用、发送外部分享、修改用户系统设置或执行设备震动。

## 托盘恢复修复

Shell 在每次原生窗口状态变化时记录最后的非最小化状态。平台动作层另存的
最小化检查点可能在用户通过任务栏恢复、取消最大化后过期。此前托盘恢复
优先使用旧检查点，导致正常窗口被再次最大化。

现在 Shell 恢复只使用自己维护的状态，平台动作不再传入旧检查点。
普通窗口恢复普通状态，原本最大化的窗口仍恢复最大化；不强制全部恢复普通窗口。
入口、淡入淡出和原生 HWND 生命周期保持原有所有权。

修复前新增回归明确失败：`Expected 'Normal', got 'Maximized'`。
真实窗口回归覆盖最大化、最小化、系统恢复、取消最大化、调整普通窗口大小、
再次最小化及托盘恢复。原生最小化必须等待 `IsIconic`，而非仅观察托管属性。
原生命中测试必须等待新画面提交，而非仅等待布局队列。

## DWM 文档纠正

微软将 `DWMWA_TRANSITIONS_FORCEDISABLED`（3）、
`DWMWA_WINDOW_CORNER_PREFERENCE`（33）、`DWMWA_BORDER_COLOR`（34）
列为用于 **DwmSetWindowAttribute** 的属性。后两项从 Windows 11 Build 22000
起支持。通过未承诺支持的 getter 获得 `E_INVALIDARG`，不能推断功能不可用，
更不能归因于本机 Insider 构建。

测试改为在独立 HWND 上断言三个 setter 返回成功；本机均返回 S_OK。
不在产品窗口上强制设置后再验证，以免掩盖产品本身的策略缺陷。
产品窗口仍检查文档支持的非客户区绘制状态、DWM 合成状态及原生窗口样式。
setter 成功不等于已经目测系统动画流畅度。

- [微软 DWM 属性说明](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
- [29695.1001 官方发布说明](https://learn.microsoft.com/en-us/windows-insider/release-notes/experimental-future-platforms/preview-build-29695-1001)

## 覆盖情况

| 功能 | 本次证据 | 边界 |
| --- | --- | --- |
| 托盘恢复状态、窗口复用 | 真实窗口及后端回归 | 通过同一恢复入口；没有人工点击通知区域菜单 |
| 普通/最大化、窗口边框、合成透明边缘 | 原生 HWND、DWM 和几何检查 | 不代替像素截图审查、Aero Snap 及多屏切换 |
| 拖动标题栏、返回按钮、窗口按钮、八个缩放边缘 | 实际 WM_NCHITTEST | 不代替连续物理鼠标拖动 |
| 托盘淡入淡出、途中反向、隐藏后恢复 | 真实中间透明度与同一 HWND | 不宣称所有系统动画已经目测验收 |
| 启动窗口、重试、取消、隐藏主窗口释放 | native-startup / startup-cancel smoke | 测试应用使用实际 Windows 后端 |
| 字体、输入选择、预编辑、光标命中 | native-font smoke | 不代替所有第三方 IME 实测 |
| Windows 分享 | native-share-package smoke | 验证位图数据包及流所有权；没有向外发送 |
| 游戏任务栏 AUMID | native window property store roundtrip | 未启动真实 Minecraft；本轮未发布 NativeAOT |
| 系统媒体控件 | 真实 Windows SMTC 注册与元数据 | 未人工操作系统媒体面板 |
| 手柄 | 真实窗口调度，模拟输入源轮询 | 不是物理手柄按键/震动验收 |
| 键盘、鼠标、触摸/笔能力 | 原生输入枚举及能力语义测试 | PnP 有 4 个键盘、4 个鼠标条目；条目数不等于物理设备数；没有触屏手势实测 |
| 协议、单实例、开机启动、拖放、通知路由 | 385 项桌面测试覆盖相关合同 | 未更改真实启动项、默认应用；未点击真实系统通知或从 Explorer 拖入文件 |
| 更新目录权限 | 真实受保护目录读取准入及 ACL 合同 | 两项需专用提权夹具的写入测试跳过；未执行自动更新事务 |
| 安全凭据/代理及其他 Services | 部分既有回归 | 未宣称 Windows 凭据库、系统 PAC、全部代理类型端到端通过 |

额外观察：原生“最大化 → 最小化 → 系统恢复 → 取消最大化”的快速序列中，
普通窗口内容尺寸曾从 860×550 DIP 变为约 810.4×500 DIP，状态仍为 Normal。
该尺寸变化不等于托盘误最大化，尚未完成独立修复。托盘状态回归在用户重新调整
普通窗口大小后验证下一次托盘恢复；不能将其解释为整个原生恢复尺寸链已通过。

## 验证入口

- `tests/Nexa.Xsr.Renderer.Backend.Avalonia.Tests`：46 项后端测试。
- 同一测试入口：`--native-corner-smoke`、`--native-startup-smoke`、
  `--native-startup-cancel-smoke`、`--native-font-smoke`、`--native-share-package-smoke`。
- `tests/Nexa.Desktop.Tests`：385 项；另运行 `--window-integration-smoke`、
  `--native-gamepad-polling-smoke`。
- `tests/Nexa.Services.Tests`：Windows/native input 筛选共 11 项，其中两项提权写入
  实际跳过，不能按汇总 PASS 计算为已验证；额外凭据相关筛选 7 项。
- `tests/Nexa.Xsr.ArchitectureTests`：同步远端后 83 个项目架构检查通过。
- 详细本机日志在未跟踪目录 `.tmp-cleanup-validation/windows-*.log`；不发布该目录。

同步远端 `6daed7164` 后重新编译后端，46 项后端测试及上述 5 个 native smoke
全部通过；385 项桌面测试、Services、AUMID 与手柄调度证据来自同步前基线。

尚需实机/安装态专项：实际通知点击、托盘菜单点击、物理手柄/触屏/震动、
多屏不同 DPI、Windows 系统动画视觉检查、UAC 更新与安装器、真实游戏及签名包。
