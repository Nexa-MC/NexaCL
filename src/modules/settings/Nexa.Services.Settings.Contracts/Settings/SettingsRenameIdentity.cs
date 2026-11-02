using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexa.Services.Settings;

/// <summary>Stable identity of persisted rename content; no settings content crosses the native port.</summary>
public static class SettingsRenameIdentity
{
    public static string Capture(string? values, string? profiles)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, values is null ? null : JsonNode.Parse(values));
        Append(hash, profiles is null ? null : JsonNode.Parse(profiles));
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static void Text(IncrementalHash hash, string text)
    {
        byte[] bytes = new byte[checked(text.Length * 2)]; Span<byte> length = stackalloc byte[4];
        for (int index = 0; index < text.Length; index++)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * 2, 2), text[index]);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length); hash.AppendData(bytes);
    }
    private static void Append(IncrementalHash hash, JsonNode? node)
    {
        if (node is null) { hash.AppendData([0]); return; }
        switch (node)
        {
            case JsonObject obj:
                hash.AppendData([1]); Text(hash, obj.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var item in obj.OrderBy(item => item.Key, StringComparer.Ordinal))
                { Text(hash, item.Key); Append(hash, item.Value); }
                break;
            case JsonArray array:
                hash.AppendData([2]); Text(hash, array.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var item in array) Append(hash, item);
                break;
            case JsonValue value:
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String: hash.AppendData([3]); Text(hash, value.GetValue<string>()); break;
                    case JsonValueKind.True: hash.AppendData([4]); break;
                    case JsonValueKind.False: hash.AppendData([5]); break;
                    case JsonValueKind.Number: hash.AppendData([6]); Text(hash, NormalizeNumber(value.ToJsonString())); break;
                    default: throw new InvalidDataException("Unsupported persisted settings value.");
                }
                break;
            default: throw new InvalidDataException("Unsupported persisted settings node.");
        }
    }
    private static string NormalizeNumber(string raw)
    {
        int exponentIndex = raw.IndexOfAny(['e', 'E']);
        BigInteger exponent = exponentIndex < 0 ? 0 : BigInteger.Parse(raw.AsSpan(exponentIndex + 1), CultureInfo.InvariantCulture);
        string significand = exponentIndex < 0 ? raw : raw[..exponentIndex];
        bool negative = significand.StartsWith('-');
        if (negative) significand = significand[1..];
        int point = significand.IndexOf('.');
        if (point >= 0) { exponent -= significand.Length - point - 1; significand = significand.Remove(point, 1); }
        significand = significand.TrimStart('0');
        if (significand.Length == 0) return "0";
        int length = significand.Length; significand = significand.TrimEnd('0'); exponent += length - significand.Length;
        return (negative ? "-" : "") + significand + "e" + exponent.ToString(CultureInfo.InvariantCulture);
    }
}
