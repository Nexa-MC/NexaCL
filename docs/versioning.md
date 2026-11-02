# 版本

产品版本只使用以下形式：

| 渠道 | 规范形式 |
| --- | --- |
| 稳定版 | 2.0.0 |
| Alpha | 2.0.0.alpha.N |
| Beta | 2.0.0.beta.N |
| CI | 2.0.0.ci.ffffff |

CI 后缀是小写六位十六进制提交标识；序号规则和编码由 `eng/xsr/Xsr.Version.props`、发布工具及测试共同检查。标签在规范版本前添加 `v`。不能用另一套文件夹名、显示文案或 SemVer 转写替代正式产品身份。

默认源码版本以 `eng/xsr/Xsr.Version.props` 为准。它与 GitHub Release 的公开状态不同。正式入口提供产品版本给固定镜像版本的 Host 和 UI。独立引擎镜像版本、Sidecar 自身版本和共享 NuGet 包版本分别管理，不随每个 Alpha 序号一起改写。

同一公开版本对应不可变的构件字节。更改代码后重新发布使用新版本，不覆盖既有签名包。版本比较和渠道选择见 `eng/release/version.py` 及更新模块的对应实现。
