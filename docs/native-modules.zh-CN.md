[English](native-modules.md) | 简体中文

# 原生模块与发布文件

正式构建不要求用户安装 .NET。`Nexa.Desktop[.exe]` 是小型 NativeAOT 启动器，由 `src/app/Nexa.Bootstrap` 构建；它从安装目录的绝对路径加载 `nexa-host`，调用 `nexa_host_main_v2`，传入 UTF-8 参数、安装路径和规范产品版本。Host 校验模块身份，在原始操作系统主线程进入 `nexa-ui`。

| 发布模块 | 职责 | 源码分组 |
| --- | --- | --- |
| nexa-host | 模块目录、准入、不透明租约、退出回收 | src/app |
| nexa-ui | UI 树、控制器、Renderer、后端、窗口生命周期 | src/app、src/rendering、src/runtime |
| nexa-accounts | 账户存储、登录、刷新、衣橱 | src/modules/accounts |
| nexa-network | HTTP/TLS、代理策略、连接、下载与续传 | src/modules/network |
| nexa-minecraft | 版本、Java、安装、资源、实例和游戏生命周期 | src/modules/minecraft、src/modules/resources |
| nexa-updates | 发布发现、签名准入、差分与完整更新事务、Sidecar 更新 | src/modules/updates |
| nexa-pac | 有界 PAC 解释器 | src/engines/pac |
| nexa-crypto | OpenPGP 验签 | src/engines/crypto |
| nexa-media | 图像元数据与 PNG 验证 | src/engines/media |
| nexa-markup | NXML 与受限 XML 解析 | src/engines/markup |
| nexa-artwork | 品牌图像、图标与手柄图像 | src/engines/artwork |

模块扩展名分别为 Windows `.dll`、Linux `.so`、macOS `.dylib`。Windows 另外提供 `ShellIntegration/Nexa.ExplorerCommand.dll`。`Nexa.Jvm.Host`、`Nexa.Update.Helper` 保持独立可执行程序。`Nexa.Native.Window` 是源码中的窗口 ABI 实现与验证项目，不是当前清单中的第十二个发布模块。

Host 准入账户、网络、Minecraft、更新四个业务服务，生产 UI 使用类型化异步客户端与带版本状态副本。发布脚本检查 UI 编译闭包中没有这些业务的具体实现；Host 不链接 UI 或服务实现。原生模块不卸载；停止时先拒绝新请求，取消工作并回收相关回调和资源，再退出进程。

`eng/release/publish_launcher.py` 构建匹配 RID 的启动器、Host、UI 和各模块。引擎使用独立固定镜像版本；仅改变产品版本不应重写未改变的 UI/引擎镜像。`verify_engine_reproducibility.py` 与 `verify_ui_reproducibility.py` 检验该约束。源码路径迁移可能使首次构建字节发生变化，不代表兼容 ABI 改变。

模块与资源必须一起分发，不能只复制 EXE，也不能单独替换一个模块。完整文件清单、签名和差分重建约束覆盖整套安装。启动检查必须包含工作目录在安装目录之外、缺失模块、错误模块身份和真实窗口关闭。
