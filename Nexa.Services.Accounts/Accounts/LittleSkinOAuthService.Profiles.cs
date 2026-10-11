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

    /// <summary>
    /// GET https://littleskin.cn/api/yggdrasil/sessionserver/session/minecraft/profile
    /// Requires <c>Yggdrasil.PlayerProfiles.Read</c>.
    /// </summary>
    public async Task<IReadOnlyList<LittleSkinProfile>> GetProfilesAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        string body = await SendBearerAsync(
                HttpMethod.Get,
                new Uri(YggdrasilApiRoot + "sessionserver/session/minecraft/profile"),
                accessToken,
                content: null,
                "获取 LittleSkin 角色档案失败",
                cancellationToken)
            .ConfigureAwait(false);

        // Unauthorized resource may return HTTP 200 with code=403.
        if (TryReadApiErrorCode(body, out int code) && code == 403)
        {
            throw new HttpRequestException(
                "获取 LittleSkin 角色档案失败：访问令牌缺少 Yggdrasil.PlayerProfiles.Read 权限，请重新授权。",
                null,
                HttpStatusCode.Forbidden);
        }

        List<LittleSkinProfile> profiles = ParseProfileArray(body);
        return profiles;
    }

    /// <summary>
    /// POST https://littleskin.cn/api/yggdrasil/authserver/oauth
    /// Body: <c>{"uuid":"&lt;undashed&gt;"}</c>. Requires <c>Yggdrasil.MinecraftToken.Create</c>.
    /// </summary>
    public async Task<LittleSkinMinecraftSession> CreateMinecraftSessionAsync(
        string accessToken,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uuid);
        string normalizedUuid = NormalizeUuid(uuid);
        string payload =
            "{\"uuid\":\"" + EscapeJsonString(normalizedUuid) + "\"}";
        string body = await SendBearerAsync(
                HttpMethod.Post,
                new Uri(YggdrasilApiRoot + "authserver/oauth"),
                accessToken,
                new StringContent(payload, Encoding.UTF8, "application/json"),
                "获取 LittleSkin Minecraft 令牌失败",
                cancellationToken)
            .ConfigureAwait(false);

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        JsonElement selected = root.TryGetProperty("selectedProfile", out JsonElement value)
            ? value
            : default;
        string username = ReadString(selected, "name");
        string profileUuid = NormalizeUuid(ReadString(selected, "id"));
        string minecraftToken = ReadString(root, "accessToken");
        string clientToken = ReadString(root, "clientToken");
        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(profileUuid) ||
            string.IsNullOrWhiteSpace(minecraftToken))
        {
            throw new InvalidDataException("LittleSkin Minecraft 令牌响应缺少选定角色。");
        }

        return new LittleSkinMinecraftSession(
            username,
            profileUuid,
            minecraftToken,
            clientToken);
    }

    public async Task<IReadOnlyList<LittleSkinPlayer>> GetPlayersAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        string body = await SendBearerAsync(
                HttpMethod.Get,
                new Uri(ApiRoot + "players"),
                accessToken,
                content: null,
                "获取 LittleSkin 角色列表失败",
                cancellationToken)
            .ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("LittleSkin 角色列表响应不是数组。");
        if (document.RootElement.GetArrayLength() > 256)
            throw new InvalidDataException("LittleSkin 角色数量超过读取限制。");

        List<LittleSkinPlayer> players = [];
        foreach (JsonElement entry in document.RootElement.EnumerateArray())
        {
            if (!TryReadInt64(entry, "pid", out long playerId))
                continue;
            players.Add(new LittleSkinPlayer(
                playerId,
                ReadString(entry, "name"),
                ReadInt64(entry, "tid_skin"),
                ReadInt64(entry, "tid_cape")));
        }

        return players;
    }

    public async Task<IReadOnlyList<LittleSkinClosetItem>> GetClosetItemsAsync(
        string accessToken,
        LittleSkinTextureKind kind,
        CancellationToken cancellationToken = default)
    {
        string category = kind == LittleSkinTextureKind.Cape ? "cape" : "skin";
        List<LittleSkinClosetItem> result = [];
        int lastPage = 1;
        for (int page = 1; page <= Math.Min(lastPage, MaximumClosetPages); page++)
        {
            string body = await SendBearerAsync(
                    HttpMethod.Get,
                    new Uri(ApiRoot + $"closet?category={category}&page={page}"),
                    accessToken,
                    content: null,
                    "获取 LittleSkin 衣柜失败",
                    cancellationToken)
                .ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            long reportedPages = ReadInt64(root, "last_page", 1);
            if (reportedPages is < 1 or > MaximumClosetPages)
                throw new InvalidDataException("LittleSkin 衣柜分页超过读取限制。");
            lastPage = (int)reportedPages;
            if (!root.TryGetProperty("data", out JsonElement data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement entry in data.EnumerateArray())
            {
                if (result.Count >= 1024) throw new InvalidDataException("LittleSkin 衣柜数量超过读取限制。");
                if (!TryReadInt64(entry, "tid", out long textureId))
                    continue;
                string hash = ReadString(entry, "hash");
                if (!IsTextureHash(hash))
                    continue;
                string name = ReadClosetItemName(entry);
                if (name.Length > 256) throw new InvalidDataException("LittleSkin 衣柜名称超过长度限制。");
                result.Add(new LittleSkinClosetItem(
                    textureId,
                    string.IsNullOrWhiteSpace(name) ? "Texture " + textureId : name,
                    ReadString(entry, "type"),
                    "https://littleskin.cn/textures/" + hash.ToLowerInvariant(),
                    kind));
            }
        }

        return result;
    }

    public async Task ApplyTextureAsync(
        string accessToken,
        long playerId,
        long textureId,
        LittleSkinTextureKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        ArgumentOutOfRangeException.ThrowIfNegative(textureId);
        if (textureId == 0 && kind != LittleSkinTextureKind.Cape)
            throw new ArgumentOutOfRangeException(nameof(textureId));
        string field = kind == LittleSkinTextureKind.Cape ? "cape" : "skin";
        using FormUrlEncodedContent content = new(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [field] = textureId.ToString(CultureInfo.InvariantCulture)
            });
        string body = await SendBearerAsync(
                HttpMethod.Put,
                new Uri(ApiRoot + $"players/{playerId}/textures"),
                accessToken,
                content,
                kind == LittleSkinTextureKind.Cape
                    ? "更换 LittleSkin 披风失败"
                    : "更换 LittleSkin 皮肤失败",
            cancellationToken)
            .ConfigureAwait(false);
        EnsureApiOperationSucceeded(body, kind == LittleSkinTextureKind.Cape
            ? "更换 LittleSkin 披风失败"
            : "更换 LittleSkin 皮肤失败");
    }

    public async Task EnsureClosetTextureAsync(
        string accessToken,
        long textureId,
        string name,
        LittleSkinTextureKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureId);
        IReadOnlyList<LittleSkinClosetItem> items =
            await GetClosetItemsAsync(accessToken, kind, cancellationToken).ConfigureAwait(false);
        if (items.Any(item => item.TextureId == textureId))
            return;

        using FormUrlEncodedContent content = new(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tid"] = textureId.ToString(CultureInfo.InvariantCulture),
                ["name"] = string.IsNullOrWhiteSpace(name) ? "Nexa Texture" : name.Trim()
            });
        string body = await SendBearerAsync(
                HttpMethod.Post,
                new Uri(ApiRoot + "closet"),
                accessToken,
                content,
                "加入 LittleSkin 衣柜失败",
                cancellationToken)
            .ConfigureAwait(false);
        EnsureApiOperationSucceeded(body, "加入 LittleSkin 衣柜失败");
    }

    /// <summary>
    /// Uploads and applies a private skin through the authlib-injector compatible
    /// Yggdrasil profile API. This endpoint authenticates with the Minecraft session
    /// token, not the provider OAuth token.
    /// </summary>
    public async Task<LittleSkinTextureUploadResult> UploadMinecraftTextureAsync(
        string minecraftAccessToken,
        string profileUuid,
        byte[] pngBytes,
        string fileName,
        bool isSlim,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftAccessToken);
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length == 0)
            throw new ArgumentException("皮肤文件为空。", nameof(pngBytes));
        string uuid = NormalizeUuid(profileUuid);
        if (uuid.Length != 32)
            throw new ArgumentException("LittleSkin 角色 UUID 无效。", nameof(profileUuid));

        using MultipartFormDataContent content = new();
        content.Add(new StringContent(isSlim ? "slim" : string.Empty), "model");
        ByteArrayContent fileContent = new(pngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(
            fileContent,
            "file",
            string.IsNullOrWhiteSpace(fileName) ? "skin.png" : Path.GetFileName(fileName));
        using HttpRequestMessage request = new(
            HttpMethod.Put,
            new Uri(YggdrasilApiRoot + $"api/user/profile/{uuid}/skin"))
        {
            Content = content
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", minecraftAccessToken.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using HttpResponseMessage response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        string body = await AccountProviderResponse.ReadTextAsync(response, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body, "上传并应用 LittleSkin 皮肤失败", TryGetRequestId(response));
        return new LittleSkinTextureUploadResult(uuid, LittleSkinTextureKind.Skin, isSlim);
    }
}
