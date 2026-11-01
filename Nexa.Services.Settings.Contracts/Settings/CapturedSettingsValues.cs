using System.Globalization;
using Nexa.Xsr;

namespace Nexa.Services.Settings;

public sealed record CapturedSettingValue(string Key, SettingValueType ValueType, string Value);
public sealed record CapturedSettingsSnapshot(long Revision, IReadOnlyList<CapturedSettingValue> Values);

/// <summary>Immutable request values; owns no persistence, state writer or remote call.</summary>
public sealed class CapturedSettingsValues : ISettingsValueReader
{
    private readonly Dictionary<string, CapturedSettingValue> _values;
    public CapturedSettingsValues(IReadOnlyList<CapturedSettingValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Dictionary<string, CapturedSettingValue> captured = new(StringComparer.Ordinal);
        foreach (var item in values)
        {
            _ = XsrSemanticId.Parse(item.Key);
            if (!captured.TryAdd(item.Key, item)) throw new ArgumentException("Duplicate captured setting.", nameof(values));
        }
        _values = captured;
    }
    public XsrResult<T> GetValue<T>(string key)
    {
        if (!_values.TryGetValue(key, out var item)) return Failure<T>(XsrErrorKind.NotFound, "settings.unknown_key", $"The setting '{key}' is not declared by the schema.");
        object? value = item.ValueType switch
        {
            SettingValueType.Bool when typeof(T) == typeof(bool) && bool.TryParse(item.Value, out var parsed) => parsed,
            SettingValueType.I32 when typeof(T) == typeof(int) && int.TryParse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            SettingValueType.I64 when typeof(T) == typeof(long) && long.TryParse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            SettingValueType.F64 when typeof(T) == typeof(double) && double.TryParse(item.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) => parsed,
            SettingValueType.Text when typeof(T) == typeof(string) => item.Value,
            _ => null
        };
        if (value is T typed) return XsrResult.Success(typed);
        return Failure<T>(XsrErrorKind.ContractMismatch, "settings.type_mismatch", $"The captured setting '{key}' cannot be read as {typeof(T).Name}.");
    }
    private static XsrResult<T> Failure<T>(XsrErrorKind kind, string code, string message) =>
        XsrResult.Failure<T>(new(kind, XsrSemanticId.Parse(code), message));
}
