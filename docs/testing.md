# 验证入口

从仓库根目录执行，先构建，再运行依赖编译程序集的架构检查。不要与检查同时写入相同 `bin/obj`。

```sh
dotnet restore NexaCL.slnx
dotnet build NexaCL.slnx -c Release -m:1
dotnet run --project tests/architecture/Nexa.Xsr.ArchitectureTests -c Release --no-build -- --repo-root .
dotnet run --project tests/runtime/Nexa.Xsr.Runtime.Tests -c Release --no-build
dotnet run --project tests/app/Nexa.Services.Tests -c Release --no-build
dotnet run --project tests/app/Nexa.Desktop.Tests -c Release --no-build
python -m unittest discover -s eng/release -p 'test_*.py'
python eng/xsr/generate_account_routes.py --check
python eng/xsr/generate_host_service_catalog.py --check
```

其余 Renderer、Sidecar、主题和原生端口测试按 `NexaCL.slnx` 及对应工作流执行。多数项目是可执行测试入口，不应以空的 `dotnet test` 结果代替执行。`eng/architecture/verify_layout.py` 检查项目位置、引用路径、依赖基线及文档链接。

`xsr-ci.yml` 执行构建、语义架构检查、插桩、业务回归、NXML／主题、格式与 AOT／trim 检查。`native-engines.yml` 验证独立原生边界。`launcher-build.yml` 验证六 RID 的正式发布闭包、镜像可复现、真实窗口、签名 Sidecar、安装及更新事务，最后验证完整包、差分重建和分发签名。

平台图形和系统集成检查应在相应原生平台执行。缺少显示器、权限、凭据或硬件时明确记录限制。历史报告不作为当前成功证据；失败不能通过改依赖许可、吞异常、取消检查或降低安全契约来掩盖。
