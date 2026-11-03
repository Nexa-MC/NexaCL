using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Settings;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private static Func<string, IDisposable>? WorldSessionLock => OperatingSystem.IsMacOS() ? Nexa.Platform.MinecraftWorldSessionLease.Acquire : null;
    private InstanceWorldSnapshotService WorldSnapshots => new(new RemoteWorldSnapshots(this), _store, WorldSessionLock);
    private async Task<InstanceManagementSnapshot> ReadManagementAsync(InstanceManagementQuery query, CancellationToken token)
    {
        var snapshot = await InstanceManagementService.ReadAsync(query, _cache, token).ConfigureAwait(false);
        if (query.IncludeRecoveryStorage)
        {
            var comparison = await new InstanceRecoveryService(_settings, _store) { WorkScheduler = _work }.ReadAsync(new(query.InstanceDirectory), token).ConfigureAwait(false);
            snapshot = snapshot with { RecoveryComparison = comparison.IsSuccess ? comparison.Value : new(query.InstanceDirectory, null, null, [], "", "暂时无法比较更改，请刷新重试。") };
        }
        return snapshot;
    }
    private Task<InstanceOfflineReadinessReport> ReadOfflineReadinessAsync(InstanceOfflineReadinessQuery query, CancellationToken token)
    {
        var selection = new JavaSelectionService(_javaLocator!);
        var service = new InstanceOfflineReadinessService(async (instance, requirement, metadata, cancellation) =>
        {
            var snapshot = await _settings.ReadAsync(new SettingsEffectiveQuery(instance), cancellation).ConfigureAwait(false);
            if (!snapshot.IsSuccess) throw new IOException(snapshot.Error?.Message ?? "无法读取实例 Java 偏好。");
            JavaPreference fallback = metadata.JavaSelectionMode == 2 && metadata.SelectedJavaPath.Length > 0
                ? new ExistingJavaPreference(metadata.SelectedJavaPath) : new AutoSelectJavaPreference();
            return await selection.SelectAsync(requirement, MinecraftLaunchCoordinator.ApplyJavaPreference(fallback, snapshot.Value!, instance), cancellation).ConfigureAwait(false);
        });
        return service.ReadAsync(query, token);
    }
    private async Task<InstanceWorldSnapshotListing> ListWorldSnapshotsAsync(InstanceWorldSnapshotListQuery query, CancellationToken token)
        => new(await WorldSnapshots.ListAsync(query, token).ConfigureAwait(false));
    private async Task CaptureWorldAsync(string path, CancellationToken token)
    {
        using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.CaptureWorld); writer.String(path);
        byte[] bytes = await _endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); reader.End();
    }
    private async Task<byte[]> CropScreenshotAsync(ReadOnlyMemory<byte> source, int x, int y, int width, int height, CancellationToken token)
    {
        using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.CropScreenshot); writer.Buffer(source);
        writer.Int32(x); writer.Int32(y); writer.Int32(width); writer.Int32(height);
        byte[] bytes = await _endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var result = reader.Buffer(); reader.End(); return result;
    }
    private sealed class RemoteWorldSnapshots(MinecraftServiceSession owner) : IInstanceWorldSnapshotStore
    {
        public async Task<IReadOnlyList<InstanceWorldSnapshot>> ListAsync(string scope, CancellationToken token)
        {
            using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.WorldSnapshotList); writer.String(scope);
            byte[] bytes = await owner._endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes); var result = reader.InstanceWorldSnapshotListing() ?? throw new InvalidDataException("Missing world snapshots."); reader.End(); return result.Items;
        }
        public async Task<InstanceWorldSnapshot> CaptureAsync(string scope, string worldDirectory, string displayName, CancellationToken token)
        {
            using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.WorldSnapshotCapture); writer.String(scope); writer.String(worldDirectory); writer.String(displayName);
            byte[] bytes = await owner._endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes); var result = reader.InstanceWorldSnapshot() ?? throw new InvalidDataException("Missing world snapshot."); reader.End(); return result;
        }
        public async Task<InstanceWorldSnapshotVerification> VerifyAsync(string scope, string identity, CancellationToken token)
        {
            using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.WorldSnapshotVerify); writer.String(scope); writer.String(identity);
            byte[] bytes = await owner._endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes); var result = reader.InstanceWorldSnapshotVerification() ?? throw new InvalidDataException("Missing world snapshot verification."); reader.End(); return result;
        }
        public async Task RestoreAsync(string scope, string identity, string destination, CancellationToken token)
        {
            using var writer = MinecraftServiceProtocol.Request(MinecraftHostOperations.WorldSnapshotRestore); writer.String(scope); writer.String(identity); writer.String(destination);
            byte[] bytes = await owner._endpoint.InvokeAsync(writer.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes); reader.End();
        }
    }
}
