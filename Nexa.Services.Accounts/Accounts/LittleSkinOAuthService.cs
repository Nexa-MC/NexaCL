// Copyright (c) MUXUE1230. All rights reserved.
// Modifications Copyright (c) 2026 Nexa contributors.
// Licensed under the Apache License, Version 2.0.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Nexa.Services.Logging;

namespace Nexa.Services.Accounts;

/// <summary>
/// LittleSkin OAuth 2 client per
/// <see href="https://manual.littlesk.in/advanced/oauth2/">OAuth 2</see> and
/// <see href="https://manual.littlesk.in/advanced/api">LittleSkin API</see>.
/// <para>
/// Desktop launchers should use the <b>device authorization grant</b>
/// (<c>open.littleskin.cn</c>). After obtaining a Bearer access token:
/// </para>
/// <list type="number">
/// <item><c>GET …/sessionserver/session/minecraft/profile</c> (scope <c>Yggdrasil.PlayerProfiles.Read</c>)</item>
/// <item><c>POST …/authserver/oauth</c> (scope <c>Yggdrasil.MinecraftToken.Create</c>)</item>
/// </list>
/// OAuth access tokens are only for LittleSkin APIs; Minecraft receives the Yggdrasil
/// access token from step 2.
/// </summary>
public sealed partial class LittleSkinOAuthService : ILittleSkinOAuthService
{
    public const string YggdrasilServer = "https://littleskin.cn/api/yggdrasil";
    public const string DefaultRedirectUri =
        "http://127.0.0.1:17342/oauth/littleskin/callback";

    /// <summary>Device-flow callback URL required when applying for device-code whitelist.</summary>
    public const string DeviceFlowRedirectUri = "https://open.littleskin.cn/oauth/callback";

    /// <summary>
    /// Shown when LittleSkin returns <c>invalid_client</c> (app not on device-code whitelist).
    /// </summary>
    public const string InvalidClientUserMessage =
        "LittleSkin OAuth 设备代码流申请暂未通过（invalid_client）。请改用「第三方登录」输入 Yggdrasil 地址与账号密码。";

    private const string AuthorizationEndpoint = "https://littleskin.cn/oauth/authorize";
    private const string PassportTokenEndpoint = "https://littleskin.cn/oauth/token";
    private const string DeviceCodeEndpoint = "https://open.littleskin.cn/oauth/device_code";
    private const string OpenTokenEndpoint = "https://open.littleskin.cn/oauth/token";
    private const string ApiRoot = "https://littleskin.cn/api/";
    private const string YggdrasilApiRoot = "https://littleskin.cn/api/yggdrasil/";

    /// <summary>
    /// LittleSkin's production OAuth server exposes the write scope with this
    /// historical spelling. Keep the wire value exact even though the public
    /// documentation displays <c>Closet.ReadWrite</c>.
    /// </summary>
    public const string ClosetReadWriteScope = "Closet.ReadWrtie";

    /// <summary>Scopes for the current launcher device flow.</summary>
    /// <remarks>
    /// Must not combine <c>PlayerProfiles.Read</c> with <c>PlayerProfiles.Select</c>.
    /// </remarks>
    public const string RequestedScopes =
        "openid offline_access " +
        "User.Read Player.ReadWrite " + ClosetReadWriteScope + " " +
        "Yggdrasil.PlayerProfiles.Read Yggdrasil.MinecraftToken.Create";

    /// <summary>
    /// Authorization-code flow scopes. LittleSkin only supports OpenID Connect
    /// and <c>offline_access</c> on its device flow; code exchange returns a
    /// refresh token without requesting either scope.
    /// </summary>
    public const string AuthorizationCodeScopes =
        "User.Read Player.ReadWrite " + ClosetReadWriteScope + " " +
        "Yggdrasil.PlayerProfiles.Read Yggdrasil.MinecraftToken.Create";

    private const int MaximumClosetPages = 50;

    private readonly HttpClient _client;
    private readonly LogService? _log;

    public LittleSkinOAuthService(HttpClient client, LogService? log = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _log = log;
    }

    public static LittleSkinOAuthConfiguration ResolveConfiguration()
    {
        string clientId = FirstNonEmpty(
            Environment.GetEnvironmentVariable("NEXA_LITTLESKIN_CLIENT_ID") ?? Environment.GetEnvironmentVariable("PCL_LITTLESKIN_CLIENT_ID"),
            Environment.GetEnvironmentVariable("LITTLESKIN_CLIENT_ID"));
        string clientSecret = FirstNonEmpty(
            Environment.GetEnvironmentVariable("NEXA_LITTLESKIN_CLIENT_SECRET") ?? Environment.GetEnvironmentVariable("PCL_LITTLESKIN_CLIENT_SECRET"),
            Environment.GetEnvironmentVariable("LITTLESKIN_CLIENT_SECRET"));
        string redirect = FirstNonEmpty(
            Environment.GetEnvironmentVariable("NEXA_LITTLESKIN_REDIRECT_URI") ?? Environment.GetEnvironmentVariable("PCL_LITTLESKIN_REDIRECT_URI"),
            Environment.GetEnvironmentVariable("LITTLESKIN_REDIRECT_URI"),
            DefaultRedirectUri);

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                "缺少 LittleSkin OAuth 配置：NEXA_LITTLESKIN_CLIENT_ID。" +
                "桌面启动器使用设备代码流，仅需 Client ID（须在 LittleSkin 申请设备代码流白名单，" +
                "回调 URL 设为 https://open.littleskin.cn/oauth/callback）。");
        }

        if (!Uri.TryCreate(redirect, UriKind.Absolute, out Uri? redirectUri))
            throw new InvalidOperationException("NEXA_LITTLESKIN_REDIRECT_URI 不是有效的绝对 URL。");

        return new LittleSkinOAuthConfiguration(clientId, clientSecret, redirectUri);
    }
}
