using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Launch;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    public static void DeclareLaunchState(XsrStateStoreBuilder builder)
    {
        MinecraftLaunchProgressState.DeclareState(builder);
        DeclareLaunchReplicas(builder);
    }
    public static void DeclareLaunchReplicas(XsrStateStoreBuilder builder)
    {
        foreach (var key in new[] { MinecraftLaunchProgressState.SnapshotKey, MinecraftLaunchProgressState.AcquirePendingKey,
            MinecraftLaunchProgressState.AcquireComponentKey, MinecraftLaunchProgressState.AcquireMajorKey,
            MinecraftLaunchProgressState.JavaChoicesKey, LaunchPreflightContract.StateKey, MinecraftLaunchProgressState.FinalizationKey }) builder.Replica(key);
    }
    private byte[] ApplyLaunchState(int field, ServiceWireReader reader)
    {
        long revision = reader.Int64(); var availability = (XsrStateAvailability)reader.Int32(); bool hasValue = reader.Boolean();
        if (revision <= 0 || availability is not (XsrStateAvailability.Available or XsrStateAvailability.Unavailable or XsrStateAvailability.Stale))
            throw new InvalidDataException("Invalid Minecraft launch state.");
        switch (field)
        {
            case 0: Apply(MinecraftLaunchProgressState.SnapshotKey, hasValue ? reader.MinecraftLaunchProgressSnapshot() ?? throw new InvalidDataException("Missing launch state.") : default!); break;
            case 1: Apply(MinecraftLaunchProgressState.AcquirePendingKey, hasValue && reader.Boolean()); break;
            case 2: Apply(MinecraftLaunchProgressState.AcquireComponentKey, hasValue ? reader.String() ?? throw new InvalidDataException("Missing Java component.") : default!); break;
            case 3: Apply(MinecraftLaunchProgressState.AcquireMajorKey, hasValue ? reader.Int32() : 0); break;
            case 4:
                IReadOnlyList<int> choices = [];
                if (hasValue)
                {
                    int count = reader.Count(); if (count < 0) throw new InvalidDataException("Missing Java choices.");
                    int[] values = new int[count]; for (int i = 0; i < count; i++) values[i] = reader.Int32(); choices = Array.AsReadOnly(values);
                }
                Apply(MinecraftLaunchProgressState.JavaChoicesKey, choices); break;
            case 5: Apply<LaunchPreflightPrompt?>(LaunchPreflightContract.StateKey, hasValue ? reader.LaunchPreflightPrompt() : null); break;
            case 6: Apply(MinecraftLaunchProgressState.FinalizationKey, hasValue && reader.Boolean()); break;
            default: throw new InvalidDataException("Unknown launch state.");
        }
        reader.End(); return [];
        void Apply<T>(XsrSemanticId key, T value)
        {
            if (_replicas is { } store) store.Replicate(store.Resolve(key), revision, availability, hasValue, value);
        }
    }
    public async Task ConfigureLaunchAsync(string root, string launcherVersion = "2.0.0", CancellationToken token = default)
    {
        await _javaConfiguration.WaitAsync(token).ConfigureAwait(false);
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.LaunchConfigure, writer => { writer.String(root); writer.String(launcherVersion); }, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); reader.End();
    }
    public bool HasPendingFinalization => _replicas is { } store && store.Read<bool>(store.Resolve(MinecraftLaunchProgressState.FinalizationKey)).Value;
    public async Task WaitForFinalizationAsync(CancellationToken cancellationToken = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.LaunchFinalize, _ => { }, cancellationToken).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); reader.End();
    }
    public async Task<MinecraftSafeLaunchSessionSnapshot> ReadSafeLaunchAsync(CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.LaunchSafeRead, _ => { }, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var value = reader.MinecraftSafeLaunchSessionSnapshot() ?? throw new InvalidDataException("Missing safe launch state."); reader.End(); return value;
    }
    public async Task<MinecraftLaunchPlanDiagnosticSnapshot> ReadLaunchDiagnosticsAsync(MinecraftLaunchPlanDiagnosticQuery query, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.LaunchDiagnostics, writer => writer.String(query.InstanceDirectory), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var value = reader.MinecraftLaunchPlanDiagnosticSnapshot() ?? throw new InvalidDataException("Missing launch diagnostics."); reader.End(); return value;
    }
    public Task<XsrResult> StartMinecraftAsync(string instance, int account, string root, string? server = null, CancellationToken token = default)
        => LaunchResultAsync(MinecraftServiceProtocol.LaunchStart, writer => { writer.String(instance); writer.Int32(account); writer.String(root); writer.String(server); }, token);
    public Task<bool> CancelLaunchAsync(CancellationToken token = default) => LaunchBooleanAsync(MinecraftServiceProtocol.LaunchCancel, _ => { }, token);
    public Task<bool> DecideJavaAsync(bool approve, CancellationToken token = default) => LaunchBooleanAsync(MinecraftServiceProtocol.LaunchJavaApprove, writer => writer.Boolean(approve), token);
    public Task<XsrResult> SelectJavaVersionAsync(int major, CancellationToken token = default) => LaunchResultAsync(MinecraftServiceProtocol.LaunchJavaMajor, writer => writer.Int32(major), token);
    public Task<XsrResult> SelectJavaPathAsync(string path, CancellationToken token = default) => LaunchResultAsync(MinecraftServiceProtocol.LaunchJavaPath, writer => writer.String(path), token);
    public Task<bool> DecidePreflightAsync(LaunchPreflightDecision decision, CancellationToken token = default) => LaunchBooleanAsync(MinecraftServiceProtocol.LaunchPreflightDecide, writer => writer.LaunchPreflightDecision(decision), token);
    public Task<XsrResult> SetSafeLaunchAsync(bool required, CancellationToken token = default) => LaunchResultAsync(MinecraftServiceProtocol.LaunchSafe, writer => writer.Boolean(required), token);
    private async Task<XsrResult> LaunchResultAsync(int operation, Action<ServiceWireWriter> encode, CancellationToken token)
    {
        byte[] bytes = await InvokeAsync(operation, encode, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    private async Task<bool> LaunchBooleanAsync(int operation, Action<ServiceWireWriter> encode, CancellationToken token)
    {
        byte[] bytes = await InvokeAsync(operation, encode, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); bool result = reader.Boolean(); reader.End(); return result;
    }
}
