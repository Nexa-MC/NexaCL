using Nexa.Xsr;

namespace Nexa.Services.Settings;

public sealed record SettingsRecoveryValue(string Key, SettingsOverride? Override, SettingsOverride Effective);
public sealed record SettingsRecoverySnapshot(string InstanceDirectory, IReadOnlyList<SettingsRecoveryValue> Values);
public sealed record RecoverySettingsPlan(string InstanceDirectory, long Revision,
    IReadOnlyList<SettingsMutation> Before, IReadOnlyList<SettingsMutation> After);

/// <summary>Canonical settings reads and conditional mutations at local recovery transaction boundaries.</summary>
public interface ISettingsRecoveryTransactions : ISettingsPolicyReader
{
    ValueTask<XsrResult<SettingsRecoverySnapshot>> CaptureRecoveryAsync(string instanceDirectory, CancellationToken token = default);
    ValueTask<XsrResult<RecoverySettingsPlan>> PlanRecoveryAsync(string instanceDirectory, SettingsRecoverySnapshot baseline,
        IReadOnlyList<string> selectedKeys, CancellationToken token = default);
    ValueTask<XsrResult> ApplyRecoveryAsync(RecoverySettingsPlan plan, bool reverse, CancellationToken token = default);
}
