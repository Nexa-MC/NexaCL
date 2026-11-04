using Nexa.Native.Minecraft.Client;
using Nexa.Services.Accounts;
using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.State;

internal static class LaunchCases
{
    internal static async Task RunAsync(NativeMinecraftClient client, string root, string instanceDirectory,
        XsrStateStore store, string executable, CancellationToken token)
    {
        string metadataDirectory = Path.Combine(instanceDirectory, "Nexa"); Directory.CreateDirectory(metadataDirectory);
        using (var document = new MemoryStream())
        {
            using (var writer = new System.Text.Json.Utf8JsonWriter(document))
            {
                writer.WriteStartObject(); writer.WriteNumber("javaSelectionMode", 2);
                writer.WriteString("selectedJavaPath", executable); writer.WriteEndObject();
            }
            await File.WriteAllBytesAsync(Path.Combine(metadataDirectory, "InstanceMetadata.json"), document.ToArray(), token);
        }
        client.AttachAccounts(new Accounts());
        int preflights = 0, visible = 0, cleanups = 0;
        bool warn = true;
        client.AttachLaunchHost(new VisibleWindow(), (_, _, _, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested(); Interlocked.Increment(ref preflights);
            return Task.FromResult(new CapabilityPreflightReport([], [], warn ? PreflightSeverity.Warning : PreflightSeverity.None));
        }, pid =>
        {
            if (!store.ReadCollection<MinecraftProcessSnapshot>(store.Resolve(MinecraftProcessStateComposition.SessionsKey), token)
                .Items.Any(item => item.ProcessId == pid && item.State == MinecraftProcessState.Running))
                throw new InvalidDataException("Game-window integration ran before its process snapshot arrived.");
            Interlocked.Increment(ref visible);
        }, async cancellation =>
        {
            cancellation.ThrowIfCancellationRequested(); await Task.Yield(); Interlocked.Increment(ref cleanups);
            return XsrResult.Success();
        });
        await client.ConfigureLaunchAsync(root, token: token);
        if (!(await client.SetSafeLaunchAsync(true, token)).IsSuccess || !(await client.ReadSafeLaunchAsync(token)).Required
            || !(await client.SetSafeLaunchAsync(false, token)).IsSuccess || (await client.ReadSafeLaunchAsync(token)).Required)
            throw new InvalidDataException("Native safe launch lost its session setting.");
        if (await client.CancelLaunchAsync(token)) throw new InvalidDataException("Native launch invented an active pipeline.");
        var start = client.StartMinecraftAsync("renamed-game", 0, root, token: token);
        var promptId = store.Resolve(LaunchPreflightContract.StateKey);
        LaunchPreflightPrompt? prompt;
        while ((prompt = store.Read<LaunchPreflightPrompt?>(promptId, token).Value) is null)
        {
            if (start.IsCompleted) throw new InvalidDataException("Native launch stopped before preflight: " + (await start).Error?.Message);
            await Task.Delay(20, token);
        }
        if (preflights != 1 || prompt.Report.OverallSeverity != PreflightSeverity.Warning
            || await client.DecidePreflightAsync(new(Guid.NewGuid(), true), token))
            throw new InvalidDataException("Native preflight lost its report or attempt identity.");
        if (!await client.DecidePreflightAsync(new(prompt.Attempt, false), token) || (await start).IsSuccess)
            throw new InvalidDataException("Native launch bypassed a rejected preflight.");
        var progress = store.Read<MinecraftLaunchProgressSnapshot>(store.Resolve(MinecraftLaunchProgressState.SnapshotKey), token);
        if (!progress.HasValue || progress.Revision <= 0 || visible != 0 || cleanups != 0 || store.Read<LaunchPreflightPrompt?>(promptId, token).Value is not null)
            throw new InvalidDataException("Native launch did not retire replicated progress/preflight state.");
        var cancelled = client.StartMinecraftAsync("renamed-game", 0, root, token: token);
        while (store.Read<LaunchPreflightPrompt?>(promptId, token).Value is null)
        {
            if (cancelled.IsCompleted) throw new InvalidDataException("Native launch stopped before scoped cancellation.");
            await Task.Delay(20, token);
        }
        var attempt = store.Read<MinecraftLaunchProgressSnapshot>(store.Resolve(MinecraftLaunchProgressState.SnapshotKey), token).Value!.Attempts.Single(item => item.Active);
        if (await client.CancelLaunchAsync(Guid.NewGuid(), token) || cancelled.IsCompleted || !await client.CancelLaunchAsync(attempt.Id, token) || (await cancelled).IsSuccess)
            throw new InvalidDataException("Native scoped cancellation lost attempt identity.");
        warn = false;
        var unconfirmed = await client.RemediateAsync(new("remediation.game.repair_files",
            new Dictionary<string, string> { ["instanceDirectory"] = instanceDirectory }), token);
        if (unconfirmed.Succeeded || unconfirmed.Code != "confirmation_required")
            throw new InvalidDataException("Native remediation bypassed confirmation.");
        var repaired = await client.RemediateAsync(new("remediation.java.select", new Dictionary<string, string>
        { ["instanceDirectory"] = instanceDirectory, ["executable"] = executable }), token);
        if (!repaired.Succeeded) throw new InvalidDataException("Native Java remediation failed: " + repaired.Message);
        repaired = await client.RemediateAsync(new("remediation.game.repair_files", new Dictionary<string, string> { ["instanceDirectory"] = instanceDirectory }, Confirmed: true), token);
        if (!repaired.Succeeded) throw new InvalidDataException("Native file remediation failed: " + repaired.Message);
        var launched = await client.StartMinecraftAsync("renamed-game", 0, root, token: token);
        if (!launched.IsSuccess) throw new InvalidDataException("Native full launch failed: " + launched.Error!.Message);
        progress = store.Read<MinecraftLaunchProgressSnapshot>(store.Resolve(MinecraftLaunchProgressState.SnapshotKey), token);
        if (visible != 1 || cleanups != 1 || progress.Value.SessionId is not { } session)
            throw new InvalidDataException("Native launch lost its visible window or session identity.");
        if (!client.HasPendingFinalization) throw new InvalidDataException("Native post-exit finalization was not replicated.");
        if ((await client.DeleteLibraryInstanceAsync(new(root, "renamed-game"), token)).IsSuccess || !Directory.Exists(instanceDirectory))
            throw new InvalidDataException("Native library deleted a running instance.");
        var servers = (await client.QueryInstanceServerListQueryAsync(new(instanceDirectory), token)).Value!;
        if ((await client.DispatchInstanceServerListSaveCommandAsync(new(instanceDirectory, servers.Revision, servers.Entries), token)).IsSuccess)
            throw new InvalidDataException("Native management ignored the launch owner's active process guard.");
        var diagnostic = await client.ReadLaunchDiagnosticsAsync(new(instanceDirectory), token);
        if (diagnostic.CapturedAt is null || diagnostic.MainClass != "fixture.Main")
            throw new InvalidDataException("Native launch diagnostics lost its actual plan.");
        await client.WaitForFinalizationAsync(token);
        if (client.HasPendingFinalization) throw new InvalidDataException("Native finalization completed before its state retired.");
        while ((await client.ProcessesAsync(token)).Single(item => item.SessionId == session).State is MinecraftProcessState.Created or MinecraftProcessState.Running)
            await Task.Delay(20, token);
        while (true)
        {
            var recovery = await client.ReadRecoveryAsync(new(instanceDirectory), token);
            if (recovery.IsSuccess && recovery.Value!.BaselineRevision is not null) break;
            await Task.Delay(20, token);
        }
        if (!store.ReadCollection<JvmHostObservation>(store.Resolve(JvmHostStateContract.ObservationsKey), token).Items.Any(item => item.SessionId == session)
            || !store.ReadCollection<JvmRunContext>(store.Resolve(JvmHostStateContract.ContextsKey), token).Items.Any(item => item.SessionId == session)
            || !store.ReadCollection<JvmRunSample>(store.Resolve(JvmHostStateContract.SamplesKey), token).Items.Any(item => item.SessionId == session))
            throw new InvalidDataException("Native JVM observation, context or sample did not reach host replicas.");
        Console.WriteLine("Native full launch prepared Java/account/files, enforced preflight, confirmed its window and captured a successful-exit baseline.");
    }
    private sealed class VisibleWindow : IMinecraftWindowProbe
    {
        public ValueTask<MinecraftWindowProbeResult> ProbeAsync(int processId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(MinecraftWindowProbeResult.Visible);
    }
    private sealed class Accounts : IAccountLaunchAccess
    {
        public XsrResult<LaunchProfile> GetProfile(int index) => throw new InvalidOperationException("Launch must use asynchronous account access.");
        public async ValueTask<XsrResult<LaunchProfile>> GetProfileAsync(int index, CancellationToken cancellationToken = default)
        { await Task.Yield(); cancellationToken.ThrowIfCancellationRequested(); return XsrResult.Success(new LaunchProfile { Username = "NativePlayer", Kind = LaunchProfileKind.Offline }); }
        public async ValueTask<XsrResult<MinecraftLaunchIdentity>> ResolveAsync(int accountIndex, LaunchProfile profile, CancellationToken cancellationToken = default)
        { await Task.Yield(); cancellationToken.ThrowIfCancellationRequested(); return XsrResult.Success(new MinecraftLaunchIdentity(profile.Username, "0123456789abcdef0123456789abcdef", "0", MinecraftLaunchIdentityMode.Offline)); }
    }
}
