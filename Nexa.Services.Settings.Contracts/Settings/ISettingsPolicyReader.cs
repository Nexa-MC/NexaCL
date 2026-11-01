using Nexa.Xsr;

namespace Nexa.Services.Settings;

/// <summary>Asynchronous access to the canonical effective settings policy.</summary>
public interface ISettingsPolicyReader
{
    ValueTask<XsrResult<SettingsEffectiveSnapshot>> ReadAsync(SettingsEffectiveQuery query, CancellationToken token = default);
}

/// <summary>Captures immutable scalar settings for one native owner operation.</summary>
public interface ISettingsSnapshotReader
{
    ValueTask<CapturedSettingsSnapshot> CaptureAsync(CancellationToken token = default);
}
