using Nexa.Xsr;

namespace Nexa.Services.Settings;

public interface ISettingsPolicyWriter
{
    ValueTask<XsrResult> SetAsync(SettingsMutation command, CancellationToken cancellationToken = default);
}
