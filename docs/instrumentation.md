# 自动函数插桩

自动函数编译器位于 `tools/compiler/Nexa.Xsr.Patch.Compiler`。它在构建机器上以托管工具运行，通过源码生成和 IL 重写生成有限形状的函数入口与 `.nfp` 目录。NativeAOT 产品不在运行时编译代码，也不带入 Roslyn 或 Mono.Cecil。

`Directory.Build.props` 标识 `src/` 下的产品源码，`Directory.Build.targets` 启用公共插桩并创建仅构建用途的编译器引用。NativeAOT 共享库按既有配置使用独立或共享 Core ABI。工具和测试不会因为磁盘目录层数变化而意外获得产品插桩策略。

`eng/xsr/Xsr.AutomaticFunctionPatches.targets` 在 CoreCompile 后插桩；匹配程序集的 `.nfp` 必须复制到构建和发布输出。调用边界、参数／返回值形状、进入／离开阶段、异常及退休有专门编译器测试。目录文件与程序集身份不匹配必须失败，不能吞掉错误后宣称插桩成功。

```sh
dotnet run --project tools/compiler/Nexa.Xsr.Patch.Compiler -c Release -- --self-test-automatic --core src/foundation/Nexa.Core/bin/Release/net10.0/Nexa.Core.dll
# 先构建 Desktop，再检查实际产物：
dotnet run --project tools/compiler/Nexa.Xsr.Patch.Compiler -c Release --no-build -- --verify-automatic src/app/Nexa.Desktop/bin/Release/net10.0
```

共享 NuGet 包的 BuildTools 使用同一目标实现，但由消费方显式引用和启用。包内目录的分发不等于消费方已经安装函数扩展。跨进程扩展还需协商功能位、授权和会话资源生命周期。
