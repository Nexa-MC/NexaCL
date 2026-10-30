# 构建、验证与发布

源码：`Nexa-MC/NexaCL:main`。SDK 以 `global.json` 为准，版本由 `eng/xsr/Xsr.Version.props` 和 CI 元数据决定。默认源码版本为 `2.0.0.alpha.17`；产品版本、NuGet 版本、Sidecar 协议版本与主题 style API 是不同版本轴。

## 本地检查

```sh
dotnet restore NexaCL.slnx
dotnet build NexaCL.slnx -c Release
dotnet run --project tests/Nexa.Services.Tests -c Release --no-build
dotnet run --project tests/Nexa.Desktop.Tests -c Release --no-build
dotnet run --project tests/Nexa.Xsr.ArchitectureTests -c Release -- --repo-root .
python3 -m unittest discover -s eng/release -p 'test_*.py'
```

服务测试可用位置参数筛选；Desktop 使用 `--filter <文本>`，不要把裸文本误认为筛选成功。完整原生窗口和设备能力在对应 OS 上验证；Linux 云容器通过不代表 Windows/macOS 硬件实测。

`xsr-ci.yml` 验证依赖边界、原生账户、主题、Sidecar、JNI 与真实隔离 Btrfs 场景。`launcher-build.yml` 原生构建六个平台并验证安装、启动、更新恢复、差分消费。macOS helper 使用 Xcode 工程生成 App Intents 元数据，不能改成缺少元数据的裸 swiftc 编译；打包前移动至 `Contents/Helpers`，再签名完整 bundle。

## NativeAOT 多文件发布

正式构建使用同一 RID 的小型 `Nexa.Desktop[.exe]` 入口与独立 Host 模块。
模块文件名全小写：Windows 为 `nexa-host.dll`，Linux 为 `nexa-host.so`，
macOS 为 `nexa-host.dylib`。渲染与服务目前仍在 Host 的同一托管调用边界内，
尚未完成业务模块的独立拆分。PAC、GPG 验签、DNS/DoH 与连接引擎分别使用
`nexa-pac`、`nexa-crypto`、`nexa-network` 动态库。引擎采用独立模块版本，
未修改的引擎不随 Alpha 版本号重编版本信息。第三方原生依赖保留上游名称。

```sh
python3 eng/release/publish_launcher.py --rid linux-x64 --output artifacts/launcher
python3 eng/release/smoke_native_modules.py --payload artifacts/launcher
```

该入口同时构建 NativeAOT 动态库和 NativeAOT 启动器，不需要安装 .NET 运行时。
不要只复制 EXE；模块、资源和其他运行依赖必须一起分发。完整包和差分更新都必须
通过签名及文件清单验证。ABI、主线程和资源所有权约束见[原生模块](xsr/native-modules.md)。

## 发布步骤

1. 提交并推送候选代码，等待源码 CI 与六平台验证。
2. 编写人工更新说明，准备对应 SHA 的 draft prerelease；不要自动拼接 Git 提交充当用户说明。
3. 创建 `v2.0.0.alpha.N` 标签并推送。工作流令牌若读不到草稿，使用已有 `release_notes` 参数传入同一份文件：

   ```sh
   gh workflow run launcher-build.yml --ref v2.0.0.alpha.17 \
     -F release_notes=@docs/releases/2.0.0.alpha.17.md
   ```

4. 工作流验证全部安装包，读取最多五个受信兼容历史版本，生成并往返验证差分包，然后签名并独立验签整个构件集合。
5. `release_notes.py publish` 将构件上传到已有草稿，保留人工说明；不会自动将已有草稿公开。先确认 distribution 成功及全部资源上传，再发布草稿。
6. `Nexa-MC/NexaCL-Update` 的 `Publish verified updates` 工作流每 15 分钟计划同步，也可手动给定 tag。GitHub 定时任务可能延迟；只有目标 Release 和工作流验证完成，才能宣称更新源同步成功。

公开含 `SHA256SUMS.asc` 的已签名草稿不会再次重建覆盖同版本构件。空 Release 仍可触发初始构建。发布后的字节按不可变契约处理，不能为了重试破坏源签名或更新仓库内容。

## 凭据和排错

构建使用 Actions Secrets，包括发布签名、OAuth 客户端配置、CurseForge API Key 与需要的 API 客户端身份。源码不携带秘密，日志不得输出其值；详情以工作流引用为准。环境中的 `GH_TOKEN` 与 Actions 自己的令牌互相独立：本地 401 不会终止已经启动的工作流，但会阻止本地查询私有草稿、触发重试或公开发布。

构建失败先区分编译／测试失败、缺少发布说明、缺失 Secrets 与运行器失联。只对未成功的任务重试，保留原始结果，不通过删门禁来发布。
