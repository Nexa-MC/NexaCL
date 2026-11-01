using Nexa.Xsr;

namespace Nexa.Services.Settings;

public sealed partial class SettingsService
{
    public XsrResult CompareExchangeText(string key, string expected, string value)
    {
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            var current = GetValue<string>(key);
            if (!current.IsSuccess) return XsrResult.Failure(current.Error!);
            if (!string.Equals(current.Value, expected, StringComparison.Ordinal))
                return XsrResult.Failure(SettingsErrors.InvalidValue(key, "the setting changed before the conditional save."));
            return SetValue(key, value);
        }
    }
}
