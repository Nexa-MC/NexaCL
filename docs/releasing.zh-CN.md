[English](releasing.md) | 简体中文

# 发布流程

正式发布包含源码验证、六平台构建、完整分发签名和独立更新源验证。发布成功以公开 Release 和验证结果为准，不能以标签存在或工作流已启动代替。

1. 在 `eng/xsr/Xsr.Version.props` 设置规范版本，提交源码和维护者编写的发布说明；运行源码 CI。
2. 为准确的完整提交 SHA 创建有非空说明的 draft prerelease，创建并推送 `v<version>` 标签。
3. `Launcher Build` 验证六个 RID，构建原生模块、JVM Host、更新助手、安装器和便携包。若工作流令牌不能读取草稿，用手动 `release_notes` 输入传入同一份说明。
4. distribution 要求全部 18 个完整包，读取最多五个兼容受信历史版本，生成差分并验证重建，验证嵌入 Sidecar 的签名与来源，然后签名并以独立公钥环境验签完整分发。
5. 工作流把全部资源附加到既有草稿，保留说明；既有草稿不会自动公开。确认完整任务成功、资源齐全和验签通过后公开草稿。
6. 触发 `Nexa-MC/NexaCL-Update` 的 `Publish verified updates`，传入相同 tag。目标工作流验证源资源实际字节与签名后公开；核对目标资源及元数据与源一致。

```sh
gh workflow run launcher-build.yml --ref v2.0.0.alpha.N -F release_notes=@/path/to/RELEASE.md
gh release edit v2.0.0.alpha.N --draft=false --prerelease --latest=false
gh workflow run publish.yml --repo Nexa-MC/NexaCL-Update -f tag=v2.0.0.alpha.N
```

Alpha/Beta 保持 prerelease，稳定版按对应渠道设置。完整包包括 Windows 每架构 Setup/MSI/ZIP、Linux DEB/RPM/AppImage/TAR、macOS DMG/TAR。签名元数据包含 `SHA256SUMS`、`Nexa-Release.json`，存在差分时还有 `Nexa-Delta.json`。

公开已包含 `SHA256SUMS.asc` 的签名草稿后，工作流跳过重建，保护公开字节。失败时保留日志，只重试失败任务，不移动已发布标签、不覆盖公开文件、不绕过签名。更新仓库每十五分钟的计划同步可能延迟，手动同步仍需等待实际成功。

发布私钥只在源仓库 Actions Secrets 中使用；更新仓库只需要公钥。仓库 OpenPGP 身份不能代替 Windows Authenticode、Apple Developer ID 或公证。本文不宣称这些操作系统信任凭据已经配置。

## 独立 Sidecar 构建输入

发布流程通过 `eng/release/independent_sidecars.py` 对独立构建的映像进行签名和校验。
Market 沿用现有输入锁；PluginSidecar 必须在完整六平台构建成功并经过审查后，将
实际源码提交、构建编号、版本、宿主引用、最小宿主版本及各平台大小和 SHA-256 写入
`eng/release/plugin-sidecar.lock.json`，才会进入发布包。普通宿主构建仍保持独立。

具体步骤见 [Sidecar 输入流程](sidecar-release-inputs.md)：先在 PluginSidecar 仓库运行
`distribution-build.yml`，再以相同提交及成功构建编号运行 `publish-distribution-inputs.yml`，
核对其 `provenance.json` 后填写锁文件。不得用零哈希、旧平台产物或占位版本补足缺口。
缺少锁文件表示插件发布尚未完成；CI 计费或运行器问题须先解决，不能视为平台测试通过。
