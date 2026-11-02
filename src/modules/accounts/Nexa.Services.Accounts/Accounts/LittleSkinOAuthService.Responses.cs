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

    private async Task<LittleSkinOAuthTokens> RequestTokensAsync(
        string endpoint,
        Dictionary<string, string> form,
        string fallbackRefreshToken,
        string operation,
        bool requireRefreshToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        using HttpResponseMessage response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        string body = await AccountProviderResponse.ReadTextAsync(response, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body, operation, TryGetRequestId(response));
        return ParseTokenResponse(body, fallbackRefreshToken, requireRefreshToken);
    }

    private static LittleSkinOAuthTokens ParseTokenResponse(
        string body,
        string fallbackRefreshToken,
        bool requireRefreshToken)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        string accessToken = ReadString(root, "access_token");
        string refreshToken = FirstNonEmpty(ReadString(root, "refresh_token"), fallbackRefreshToken);
        string idToken = ReadString(root, "id_token");
        int expiresIn = (int)Math.Clamp(ReadInt64(root, "expires_in", 259200), 1, int.MaxValue);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidDataException("LittleSkin OAuth 响应缺少访问令牌。");
        if (requireRefreshToken && string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidDataException("LittleSkin OAuth 响应缺少刷新令牌，无法维持长期登录。");
        return new LittleSkinOAuthTokens(
            accessToken,
            refreshToken,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            string.IsNullOrWhiteSpace(idToken) ? null : idToken);
    }

    private async Task<string> SendBearerAsync(
        HttpMethod method,
        Uri uri,
        string accessToken,
        HttpContent? content,
        string operation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        using HttpRequestMessage request = new(method, uri)
        {
            Content = content
        };
        // Docs: Authorization: Bearer {{access_token}} + Accept: application/json
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken.Trim());
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        using HttpResponseMessage response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        string body = await AccountProviderResponse.ReadTextAsync(response, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body, operation, TryGetRequestId(response));
        return body;
    }

    private void ThrowIfInvalidClient(string body, string? requestId = null)
    {
        if (!string.Equals(TryReadOAuthError(body), "invalid_client", StringComparison.Ordinal))
            return;

        _log?.Warn("LittleSkinAuth", "Device authorization rejected code=invalid_client; check the configured application registration.");
        string message = InvalidClientUserMessage;
        if (!string.IsNullOrWhiteSpace(requestId))
            message += " 请求 ID：" + requestId;
        throw new InvalidOperationException(message);
    }

    private void EnsureSuccess(
        HttpResponseMessage response,
        string body,
        string operation,
        string? requestId = null,
        [CallerMemberName] string source = "")
    {
        if (response.IsSuccessStatusCode)
            return;

        _log?.Warn("LittleSkinAuth", $"Authentication request failed stage={source} http_status={(int)response.StatusCode}");
        ThrowIfInvalidClient(body, requestId);

        string detail = string.Empty;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            detail = FirstNonEmpty(
                ReadString(root, "errorMessage"),
                ReadString(root, "error_description"),
                ReadString(root, "message"),
                ReadString(root, "error"));
        }
        catch (JsonException)
        {
            if (!string.IsNullOrWhiteSpace(body) && body.Length <= 240 && body[0] is not '<')
                detail = body.Trim();
        }

        string message = string.IsNullOrWhiteSpace(detail)
            ? $"{operation}（HTTP {(int)response.StatusCode}）。"
            : $"{operation}：{detail}";
        if (!string.IsNullOrWhiteSpace(requestId))
            message += " 请求 ID：" + requestId;
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static string? TryGetRequestId(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-Yggdralt-Req-ID", out IEnumerable<string>? values))
            return values.FirstOrDefault();
        if (response.Headers.TryGetValues("X-Yggdrasil-Req-ID", out values))
            return values.FirstOrDefault();
        return null;
    }

    private static string TryReadOAuthError(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return ReadString(document.RootElement, "error");
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static bool TryReadApiErrorCode(string body, out int code)
    {
        code = 0;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (!TryReadInt64(document.RootElement, "code", out long value) ||
                value is < int.MinValue or > int.MaxValue)
            {
                return false;
            }

            code = (int)value;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void EnsureApiOperationSucceeded(string body, string operation)
    {
        if (!TryReadApiErrorCode(body, out int code) || code == 0)
            return;

        string detail = string.Empty;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            detail = ReadString(document.RootElement, "message");
        }
        catch (JsonException)
        {
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(detail) ? operation : operation + "：" + detail);
    }

    private static List<LittleSkinProfile> ParseProfileArray(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("LittleSkin 角色档案响应不是数组。");

        List<LittleSkinProfile> profiles = [];
        foreach (JsonElement entry in document.RootElement.EnumerateArray())
        {
            string uuid = ReadString(entry, "id");
            string name = ReadString(entry, "name");
            if (!string.IsNullOrWhiteSpace(uuid) && !string.IsNullOrWhiteSpace(name))
                profiles.Add(new LittleSkinProfile(name, NormalizeUuid(uuid)));
        }

        return profiles;
    }

    private static string ReadClosetItemName(JsonElement entry)
    {
        if (entry.TryGetProperty("pivot", out JsonElement pivot) &&
            pivot.ValueKind == JsonValueKind.Object)
        {
            string itemName = ReadString(pivot, "item_name");
            if (!string.IsNullOrWhiteSpace(itemName))
                return itemName;
        }

        return ReadString(entry, "name");
    }

    private static string NormalizeUuid(string uuid) =>
        new(uuid.Where(static character => character is not ('-' or ' ')).ToArray());

    private static string EscapeJsonString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static bool IsTextureHash(string hash) =>
        hash.Length == 64 &&
        hash.All(static character =>
            character is >= '0' and <= '9' or
                >= 'a' and <= 'f' or
                >= 'A' and <= 'F');

    private static string ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long ReadInt64(JsonElement element, string propertyName, long fallback = 0) =>
        TryReadInt64(element, propertyName, out long value) ? value : fallback;

    private static bool TryReadInt64(
        JsonElement element,
        string propertyName,
        out long value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt64(out value),
            JsonValueKind.String => long.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value),
            _ => false
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }
}
