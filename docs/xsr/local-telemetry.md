# 本地构建遥测

当前匿名事件入口为 `https://pcln.top/api/v1/telemetry/events`，与网站管理页读取的
`nexa-platform` 聚合库一致。旧 `api.pcln.top/v2/launcher/telemetry` 已返回 404。
事件格式仍为分级数组：name、level、timestamp（Unix 毫秒）与白名单 properties；
必要/诊断分级、同意设置、批次上限与失败保留策略均不改变。

该入口不需要账户或客户端证书。普通 `dotnet build/run` 同样可以按上述策略上传；
上传使用独立、禁用重定向和 Cookie 的受管 HTTP 客户端，不依赖证书加载是否成功。
以下证书规则只适用于仍要求 API 客户端身份的其他服务，不是遥测启动前提：
CI 从 secret 恢复的
`Nexa.Desktop/Assets/api-client.pfx` 不在源码仓库中。

本地测试可在启动进程的环境中设置 `NEXA_API_CLIENT_CERT_PATH`，指向有私钥、未过期且
被服务端接受的 PFX。密码通过 `NEXA_API_CLIENT_CERT_PASSWORD` 传入；不要提交证书、
密码或命令历史。不要使用自签测试证书代替服务器认可的客户端身份，也不绕过 API Shield。
这两个设置已有运行时支持，不需要重新编译。CI 内嵌证书仍是没有路径覆盖时的默认来源。
Windows 在进程没有路径覆盖时也读取当前用户环境设置，避免已有终端继承旧环境。
Windows Schannel 使用 `UserKeySet` 导入，不设置 `PersistKeySet`；其他平台使用
`EphemeralKeySet`。这避免 Windows 临时私钥握手的 `SEC_E_NO_CREDENTIALS` 错误；
证书和 HTTP 连接池随会话释放。不关闭 TLS 服务端证书验证，不修改系统信任根。
相关平台限制见 [Microsoft SslStream 故障排查](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-troubleshooting)。

完成 OOBE、进入正常主界面后才启动遥测；`--validate-shell` 不发送遥测。
必要遥测始终启用；CI/Alpha/Beta 强制诊断，正式版诊断取决于用户设置。
启动会立即尝试上传，随后每 30 秒重试。HTTP 拒绝记录状态码，不记录响应正文、
事件内容、证书路径或密码；同一失败原因只记录一次，成功后重置。
服务器接受第一批事件后记录一次 Info 日志。未覆盖版本属性的本地构建目前为
`2.0.0.alpha.1`，管理员版本筛选需与实际构建一致。

遥测失败时检查当前端点、网络、HTTP 状态码和事件校验，不引导用户配置证书。
只有服务端接受并在管理员聚合结果中出现，才算端到端验证通过。
