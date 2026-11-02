# 构建与开发

在仓库根目录执行命令。安装 `global.json` 指定的 .NET 10 SDK；Python 3 用于工程脚本。NativeAOT 还需要目标系统的本地编译器、链接器和平台库，实际安装项以 `.github/workflows/launcher-build.yml`、`xsr-ci.yml` 为准。

```sh
dotnet restore NexaCL.slnx
dotnet build NexaCL.slnx -c Release -m:1
dotnet run --project src/app/Nexa.Desktop -c Release --no-build
```

业务实现、UI 和测试的项目名与程序集身份保持不变。`Directory.Build.props` 定义仓库根和产品源码范围；`Directory.Build.targets` 为 `src/` 下产品项目启用自动插桩。工具与测试不通过目录深度猜测产品身份，原生项目沿用显式插桩配置。

正式原生发布使用统一入口：

```sh
python eng/release/publish_launcher.py --rid linux-x64 --output artifacts/launcher
python eng/release/smoke_native_modules.py --payload artifacts/launcher
```

RID 支持 `win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`。请在匹配平台运行原生编译与集成验证。打包入口为 `eng/release/package.py --help`；它验证运行时文件并生成平台安装器及便携包。

自动插桩编译器位于 `tools/compiler/Nexa.Xsr.Patch.Compiler`，仅作构建依赖。不得把 Roslyn、Mono.Cecil 或 CoreCLR 运行时混入 NativeAOT 产品。恢复包、生成代码、编译、架构检查依次执行；不要同时启动写入同一项目输出目录的构建。

Secrets 在 Actions 中提供，源码不得包含令牌、API 凭据或发布私钥。缺少外部依赖时明确记录限制，不以删除门禁替代验证。
