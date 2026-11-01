using Nexa.Xsr;

namespace Nexa.Services.Settings;

public sealed partial class SettingsPolicyService : ISettingsRecoveryTransactions
{
    public ValueTask<XsrResult<SettingsRecoverySnapshot>> CaptureRecoveryAsync(string instanceDirectory, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try { return ValueTask.FromResult(XsrResult.Success(SettingsRecoveryCodec.Decode(CaptureRecoverySettings(instanceDirectory)))); }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { return ValueTask.FromResult(XsrResult.Failure<SettingsRecoverySnapshot>(Invalid(error.Message))); }
    }
    public ValueTask<XsrResult<RecoverySettingsPlan>> PlanRecoveryAsync(string instanceDirectory, SettingsRecoverySnapshot baseline,
        IReadOnlyList<string> selectedKeys, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try { return ValueTask.FromResult(XsrResult.Success(PlanRecoverySettings(instanceDirectory, SettingsRecoveryCodec.Encode(baseline), selectedKeys))); }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { return ValueTask.FromResult(XsrResult.Failure<RecoverySettingsPlan>(Invalid(error.Message))); }
    }
    public ValueTask<XsrResult> ApplyRecoveryAsync(RecoverySettingsPlan plan, bool reverse, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try { return ValueTask.FromResult(ApplyRecoverySettingsPlan(plan, reverse)); }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { return ValueTask.FromResult(XsrResult.Failure(Invalid(error.Message))); }
    }
}
