using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Settings;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    private ISettingsSnapshotReader? _settingsSnapshots;
    private ISettingsPolicyReader? _settingsPolicies;
    private ISettingsRenameTransactions? _settingsTransactions;
    private ISettingsPolicyWriter? _settingsWriter;
    public void AttachSettings(ISettingsSnapshotReader snapshots, ISettingsPolicyReader policies)
    {
        ArgumentNullException.ThrowIfNull(snapshots); ArgumentNullException.ThrowIfNull(policies);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_settingsSnapshots is not null) throw new InvalidOperationException("Canonical settings are already attached.");
            _settingsSnapshots = snapshots; _settingsPolicies = policies; _settingsTransactions = policies as ISettingsRenameTransactions;
            _settingsRecovery = policies as ISettingsRecoveryTransactions;
            _settingsWriter = policies as ISettingsPolicyWriter;
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
        else if (operation == MinecraftHostOperations.SettingsPolicySet)
        {
            var command = reader.SettingsMutation() ?? throw new InvalidDataException("Missing settings mutation."); reader.End();
            var target = Volatile.Read(ref _settingsWriter) ?? throw new InvalidOperationException("Canonical settings mutations are not attached.");
            writer.XsrError((await target.SetAsync(command, token).ConfigureAwait(false)).Error);
        }
        else if (operation == MinecraftHostOperations.SettingsPolicyRead)
        {
            var query = reader.SettingsEffectiveQuery() ?? throw new InvalidDataException("Missing settings policy query."); reader.End();
            var source = Volatile.Read(ref _settingsPolicies) ?? throw new InvalidOperationException("Canonical settings policies are not attached.");
            var result = await source.ReadAsync(query, token).ConfigureAwait(false);
            writer.XsrError(result.Error); if (result.IsSuccess) writer.SettingsEffectiveSnapshot(result.Value);
        }
        else if (operation == MinecraftHostOperations.SettingsRenamePrepare)
        {
            string source = reader.String() ?? throw new InvalidDataException("Missing rename source.");
            string destination = reader.String() ?? throw new InvalidDataException("Missing rename destination."); reader.End();
            var transactions = Volatile.Read(ref _settingsTransactions) ?? throw new InvalidOperationException("Canonical rename transactions are not attached.");
            var result = await transactions.PrepareRenameAsync(source, destination, token).ConfigureAwait(false);
            writer.XsrError(result.Error); if (result.IsSuccess) writer.SettingsRenameReceipt(result.Value);
        }
        else
        {
            var receipt = reader.SettingsRenameReceipt() ?? throw new InvalidDataException("Missing rename receipt.");
            bool reverse = reader.Boolean(); reader.End();
            var transactions = Volatile.Read(ref _settingsTransactions) ?? throw new InvalidOperationException("Canonical rename transactions are not attached.");
            writer.XsrError((await transactions.ApplyRenameAsync(receipt, reverse, token).ConfigureAwait(false)).Error);
        }
        return writer.Finish();
    }
}
