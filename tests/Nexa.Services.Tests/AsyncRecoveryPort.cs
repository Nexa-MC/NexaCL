using Nexa.Services.Settings;
using Nexa.Xsr;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private sealed class AsyncRecoveryPort(SettingsPolicyService settings) : ISettingsRecoveryTransactions
    {
        public async ValueTask<XsrResult<SettingsEffectiveSnapshot>> ReadAsync(SettingsEffectiveQuery query, CancellationToken token = default)
        { await Task.Yield(); return await settings.ReadAsync(query, token); }
        public async ValueTask<XsrResult<SettingsRecoverySnapshot>> CaptureRecoveryAsync(string instance, CancellationToken token = default)
        { await Task.Yield(); return await settings.CaptureRecoveryAsync(instance, token); }
        public async ValueTask<XsrResult<RecoverySettingsPlan>> PlanRecoveryAsync(string instance, SettingsRecoverySnapshot baseline,
            IReadOnlyList<string> keys, CancellationToken token = default)
        { await Task.Yield(); return await settings.PlanRecoveryAsync(instance, baseline, keys, token); }
        public async ValueTask<XsrResult> ApplyRecoveryAsync(RecoverySettingsPlan plan, bool reverse, CancellationToken token = default)
        { await Task.Yield(); return await settings.ApplyRecoveryAsync(plan, reverse, token); }
    }
}
