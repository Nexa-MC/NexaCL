# 架构与依赖

XSR 将执行协调、业务能力和语义呈现分离。产品保留 NativeAOT，正式部署由同进程原生模块与独立 Sidecar 进程组成。

```text
Bootstrap -> 原生 Host -> UI 主线程
                   |-> Accounts / Network / Minecraft / Updates
UI -> 状态副本与意图 -> 类型化客户端 -> 原生异步端口
业务 -> 契约、Domain、Core 与显式平台接口
Host <-> Sidecar Protocol / Transport <-> 独立插件进程
```

Command 表达动作请求，接受不等于执行完成；Query 提供可取消的一次性结果；State 表达带版本的当前事实；Event 表达瞬时事实。Renderer 读取状态并产生意图，不在渲染路径查询服务或直接启动 Sidecar。UI 控制器不使用具体业务实现。事件排序作用域与资源生命周期作用域是不同机制。

基础依赖向内：Domain 依赖 Core；业务契约依赖必要的低层契约和 XSR 抽象；业务实现不依赖 Desktop、Avalonia、Renderer 后端或服务定位器。Composition 在边缘绑定实现、Runtime 和平台端口。平台进程创建、凭据存储、探测和重启通过接口进入业务。Sidecar.Protocol 不依赖产品 Core、Contracts 或 XSR；Transport 仅依赖 Protocol。

当前允许的直接项目引用由 `eng/architecture/project-references.json` 锁定，实际项目位置由同目录 `projects.json` 记录。架构检查按项目身份验证引用、循环、程序集元数据和语义边界。目录调整不允许扩大依赖清单来掩盖错误，也不能只靠命名或接口声明证明实现隔离。

跨 NativeAOT 模块只传递版本化 C ABI 的整数、长度受限字节、缓冲区和不透明句柄。禁止传递 CLR 对象、Task、Stream、托管委托和异常。模块拥有自己的对象，显式定义回调、取消、完成、释放和退出顺序。详细发布边界见 [原生模块](native-modules.md)。

修改公开契约或所有权前先修改本页或对应专题说明，随后更新实现、消费方和契约验证。外部插件进程的实现由独立仓库维护，本仓库不通过相邻工作区源码引用获得它。
