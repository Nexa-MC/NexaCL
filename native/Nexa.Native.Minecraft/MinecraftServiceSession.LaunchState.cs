using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Launch;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private bool PublishLaunchState(XsrStateChange change)
    {
        var key = change.SemanticId;
        if (key == MinecraftLaunchProgressState.SnapshotKey) PublishLaunchCell<MinecraftLaunchProgressSnapshot>(key, 0, (writer, value) => writer.MinecraftLaunchProgressSnapshot(value));
        else if (key == MinecraftLaunchProgressState.AcquirePendingKey) PublishLaunchCell<bool>(key, 1, (writer, value) => writer.Boolean(value));
        else if (key == MinecraftLaunchProgressState.AcquireComponentKey) PublishLaunchCell<string>(key, 2, (writer, value) => writer.String(value));
        else if (key == MinecraftLaunchProgressState.AcquireMajorKey) PublishLaunchCell<int>(key, 3, (writer, value) => writer.Int32(value));
        else if (key == MinecraftLaunchProgressState.JavaChoicesKey) PublishLaunchCell<IReadOnlyList<int>>(key, 4, (writer, value) =>
        { writer.Count(value.Count); foreach (int major in value) writer.Int32(major); });
        else if (key == LaunchPreflightContract.StateKey) PublishLaunchCell<LaunchPreflightPrompt?>(key, 5, (writer, value) => writer.LaunchPreflightPrompt(value));
        else if (key == MinecraftLaunchProgressState.FinalizationKey) PublishLaunchCell<bool>(key, 6, (writer, value) => writer.Boolean(value));
        else return false;
        return true;
    }
    private void PublishLaunchCell<T>(XsrSemanticId key, int field, Action<ServiceWireWriter, T> write)
    {
        var captured = _store.Read<T>(_store.Resolve(key));
        _publications.Post(35 + field, captured.Revision, () =>
        {
            using var writer = MinecraftServiceProtocol.Request(MinecraftServiceProtocol.LaunchState + field);
            writer.Int64(captured.Revision); writer.Int32((int)captured.Availability); writer.Boolean(captured.HasValue);
            if (captured.HasValue) write(writer, captured.Value); return writer.Finish();
        });
    }
}
