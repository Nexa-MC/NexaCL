using Nexa.Xsr;

namespace Nexa.Services.Settings;

/// <summary>Typed reads from a canonical settings owner or an immutable request snapshot.</summary>
public interface ISettingsValueReader
{
    XsrResult<T> GetValue<T>(string key);
}
