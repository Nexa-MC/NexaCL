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

public sealed partial class LittleSkinOAuthService
{

    public LittleSkinAuthorizationRequest CreateAuthorizationRequest(
        LittleSkinOAuthConfiguration configuration,
        string state)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        string url = AuthorizationEndpoint +
                     "?client_id=" + Uri.EscapeDataString(configuration.ClientId) +
                     "&redirect_uri=" + Uri.EscapeDataString(configuration.RedirectUri.AbsoluteUri) +
                     "&response_type=code" +
                     "&scope=" + Uri.EscapeDataString(AuthorizationCodeScopes) +
                     "&state=" + Uri.EscapeDataString(state);
        return new LittleSkinAuthorizationRequest(new Uri(url), state);
    }

    public async Task<LittleSkinDeviceCodeInfo> RequestDeviceCodeAsync(
        LittleSkinOAuthConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        using FormUrlEncodedContent form = new(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["client_id"] = configuration.ClientId,
                ["scope"] = RequestedScopes
            });
        using HttpRequestMessage request = new(HttpMethod.Post, DeviceCodeEndpoint)
        {
            Content = form
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        using HttpResponseMessage response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        string body = await AccountProviderResponse.ReadTextAsync(response, cancellationToken).ConfigureAwait(false);
        string? requestId = TryGetRequestId(response);
        ThrowIfInvalidClient(body, requestId);
        EnsureSuccess(response, body, "申请 LittleSkin 设备代码失败", requestId);

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        string userCode = ReadString(root, "user_code");
        string deviceCode = ReadString(root, "device_code");
        string verificationUri = ReadString(root, "verification_uri");
        string verificationUriComplete = FirstNonEmpty(
            ReadString(root, "verification_uri_complete"),
            verificationUri);
        int expiresIn = (int)Math.Clamp(ReadInt64(root, "expires_in", 300), 30, int.MaxValue);
        int interval = (int)Math.Clamp(ReadInt64(root, "interval", 5), 1, 120);
        if (string.IsNullOrWhiteSpace(userCode) ||
            string.IsNullOrWhiteSpace(deviceCode) ||
            string.IsNullOrWhiteSpace(verificationUri))
        {
            throw new InvalidDataException("LittleSkin 设备代码响应缺少必要字段。");
        }

        return new LittleSkinDeviceCodeInfo(
            userCode,
            deviceCode,
            verificationUri,
            verificationUriComplete,
            expiresIn,
            interval);
    }

    public async Task<LittleSkinOAuthTokens> WaitForDeviceAuthorizationAsync(
        LittleSkinOAuthConfiguration configuration,
        LittleSkinDeviceCodeInfo deviceCode,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(deviceCode);

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(deviceCode.ExpiresInSeconds);
        int intervalMs = Math.Max(1000, deviceCode.IntervalSeconds * 1000);
        double started = Environment.TickCount64;
        double totalMs = Math.Max(1, deviceCode.ExpiresInSeconds * 1000d);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(Math.Clamp((Environment.TickCount64 - started) / totalMs * 0.55d, 0d, 0.55d));

            using FormUrlEncodedContent form = new(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = configuration.ClientId,
                    ["device_code"] = deviceCode.DeviceCode
                });
            using HttpRequestMessage request = new(HttpMethod.Post, OpenTokenEndpoint)
            {
                Content = form
            };
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            using HttpResponseMessage response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            string body = await AccountProviderResponse.ReadTextAsync(response, cancellationToken)
                .ConfigureAwait(false);
            string? requestId = TryGetRequestId(response);

            if (response.IsSuccessStatusCode)
            {
                progress?.Report(0.6d);
                return ParseTokenResponse(body, fallbackRefreshToken: string.Empty, requireRefreshToken: true);
            }

            string error = TryReadOAuthError(body);
            if (string.Equals(error, "authorization_pending", StringComparison.Ordinal))
            {
                _log?.Trace("LittleSkinAuth", "Device authorization is pending.");
                await Task.Delay(intervalMs, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (string.Equals(error, "slow_down", StringComparison.Ordinal))
            {
                _log?.Debug("LittleSkinAuth", "Device authorization polling was slowed by the provider.");
                intervalMs = Math.Min(intervalMs + 5000, 60_000);
                await Task.Delay(intervalMs, cancellationToken).ConfigureAwait(false);
                continue;
            }

            ThrowIfInvalidClient(body, requestId);
            EnsureSuccess(response, body, "LittleSkin 设备授权失败", requestId);
        }

        throw new TimeoutException("LittleSkin 设备授权超时，请重新发起登录。");
    }

    public Task<LittleSkinOAuthTokens> ExchangeAuthorizationCodeAsync(
        LittleSkinOAuthConfiguration configuration,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (string.IsNullOrWhiteSpace(configuration.ClientSecret))
        {
            throw new InvalidOperationException(
                "授权代码流需要 NEXA_LITTLESKIN_CLIENT_SECRET。桌面启动器请使用设备代码流。");
        }

        return RequestTokensAsync(
            PassportTokenEndpoint,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = configuration.ClientId,
                ["client_secret"] = configuration.ClientSecret,
                ["redirect_uri"] = configuration.RedirectUri.AbsoluteUri,
                ["code"] = code
            },
            fallbackRefreshToken: string.Empty,
            "兑换 LittleSkin OAuth 授权码失败",
            requireRefreshToken: true,
            cancellationToken);
    }

    public async Task<LittleSkinOAuthTokens> RefreshOAuthTokenAsync(
        LittleSkinOAuthConfiguration configuration,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        // Device-flow tokens (primary for launchers) refresh on open.littleskin.cn.
        try
        {
            return await RequestTokensAsync(
                    OpenTokenEndpoint,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["grant_type"] = "refresh_token",
                        ["client_id"] = configuration.ClientId,
                        ["refresh_token"] = refreshToken
                    },
                    refreshToken,
                    "刷新 LittleSkin OAuth 令牌失败",
                    requireRefreshToken: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException) when (!string.IsNullOrWhiteSpace(configuration.ClientSecret))
        {
            // Legacy authorization-code tokens refresh on littleskin.cn with client_secret.
            return await RequestTokensAsync(
                    PassportTokenEndpoint,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["grant_type"] = "refresh_token",
                        ["client_id"] = configuration.ClientId,
                        ["client_secret"] = configuration.ClientSecret,
                        ["refresh_token"] = refreshToken
                    },
                    refreshToken,
                    "刷新 LittleSkin OAuth 令牌失败",
                    requireRefreshToken: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
