using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Settings;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    private ISettingsSnapshotReader? _settingsSnapshots;
    private ISettingsPolicyReader? _settingsPolicies;
    public void AttachSettings(ISettingsSnapshotReader snapshots, ISettingsPolicyReader policies)
    {
        ArgumentNullException.ThrowIfNull(snapshots); ArgumentNullException.ThrowIfNull(policies);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_settingsSnapshots is not null) throw new InvalidOperationException("Canonical settings are already attached.");
            _settingsSnapshots = snapshots; _settingsPolicies = policies;
        }
    }
    private async Task<byte[]> ExecuteSettingsAsync(int operation, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = MinecraftServiceProtocol.Read(bytes, out _); using var writer = new ServiceWireWriter();
        if (operation == MinecraftHostOperations.SettingsSnapshot)
        {
            reader.End();
            var source = Volatile.Read(ref _settingsSnapshots) ?? throw new InvalidOperationException("Canonical settings snapshots are not attached.");
            writer.CapturedSettingsSnapshot(await source.CaptureAsync(token).ConfigureAwait(false));
        }
        else
        {
            var query = reader.SettingsEffectiveQuery() ?? throw new InvalidDataException("Missing settings policy query."); reader.End();
            var source = Volatile.Read(ref _settingsPolicies) ?? throw new InvalidOperationException("Canonical settings policies are not attached.");
            var result = await source.ReadAsync(query, token).ConfigureAwait(false);
            writer.XsrError(result.Error); if (result.IsSuccess) writer.SettingsEffectiveSnapshot(result.Value);
        }
        return writer.Finish();
    }
}
