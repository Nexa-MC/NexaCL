using System.Security.Cryptography;
using System.Text;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Runtime.Tests;

internal static partial class Program
{
    private static readonly XsrSemanticId NxmlModuleState = XsrSemanticId.Parse("ui.test.nxml.modules");
    private static SidecarRegistrationItem NxmlItem()
    {
        var document = new SidecarNxmlDocument("#AnyNewPage/#AnyNewControl", SidecarNxmlOperation.Replace,
            "main.nxml", new Dictionary<string, string> { ["main.nxml"] = "<Text Content=\"{data plugin.title}\" />" });
        byte[] payload = SidecarNxmlDocument.Encode(document);
        return new(SidecarRegistrationKind.UiModule, "plugin.nxml.main", 0, 0, payload, SHA256.HashData(payload));
    }
    private static async ValueTask NxmlModulesCompileBeforeReadyAndRetire()
    {
        XsrStateStoreBuilder builder = new(); builder.Cell<XsrNxmlModuleSnapshot>(NxmlModuleState, "Test");
        var store = builder.Build(); var state = store.Resolve(NxmlModuleState);
        int compilations = 0;
        XsrNxmlModuleRuntime runtime = new(store, state, (_, data) =>
        { Interlocked.Increment(ref compilations); return new(data["plugin.title"], []); });
        var (session, peer) = await PatchSession(new(CaptionPoint),
            [new(SidecarRegistrationKind.State, "plugin.title", 0, 0), NxmlItem()], features: SidecarFeatures.All, nxml: runtime);
        using (session) using (peer)
        {
            AssertEqual(0, compilations);
            AssertFalse(store.Read<XsrNxmlModuleSnapshot>(state).HasValue);
            await SnapshotAll(session, peer, (1, Encoding.UTF8.GetBytes("before READY")));
            AssertEqual(1, compilations); AssertFalse(store.Read<XsrNxmlModuleSnapshot>(state).HasValue);
            await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            var snapshot = store.Read<XsrNxmlModuleSnapshot>(state).Value!;
            AssertEqual("before READY", (string)snapshot.Modules.Single().Presentation.Artifact);
            var exposure = snapshot.Modules.Single().Exposure;
            var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            store.Changed += change =>
            {
                var modules = store.Read<XsrNxmlModuleSnapshot>(state).Value?.Modules;
                if (change.Id == state && modules is { Count: > 0 } && modules[0].Presentation.Artifact is "after READY") refreshed.TrySetResult();
            };
            session.Mirror!.Store.Publish(session.Mirror.Store.Resolve(XsrSemanticId.Parse("plugin.title")), "after READY");
            await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            AssertEqual(exposure, store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.Single().Exposure);
            await session.DeactivateAsync(); await DataPlaneReceiveAsync(peer);
            AssertEqual(0, store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.Count);
            await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            var restored = store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.Single();
            AssertEqual("after READY", (string)restored.Presentation.Artifact);
            AssertFalse(exposure == restored.Exposure);
            session.Dispose(); AssertEqual(0, store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.Count);
            AssertFalse((await runtime.DispatchAsync(exposure, XsrSemanticId.Parse("plugin.action"), "")).IsSuccess);
        }
    }

    private static async ValueTask NxmlModulesRejectUnsupportedAndInvalidDocuments()
    {
        await AssertThrowsAsync<SidecarProtocolException>(async () =>
        {
            var (session, peer) = await PatchSession(new(CaptionPoint), [NxmlItem()]);
            session.Dispose(); peer.Dispose();
        });
        XsrStateStoreBuilder builder = new(); builder.Cell<XsrNxmlModuleSnapshot>(NxmlModuleState, "Test");
        var store = builder.Build(); var state = store.Resolve(NxmlModuleState);
        XsrNxmlModuleRuntime runtime = new(store, state, (_, _) => throw new InvalidDataException("Invalid shared NXML"));
        var (invalid, plugin) = await PatchSession(new(CaptionPoint), [NxmlItem()], features: SidecarFeatures.All, nxml: runtime);
        using (invalid) using (plugin)
        {
            await AssertThrowsAsync<SidecarProtocolException>(async () => await SnapshotAll(invalid, plugin));
            AssertFalse(store.Read<XsrNxmlModuleSnapshot>(state).HasValue);
            AssertEqual(SidecarSessionState.Failed, invalid.State);
        }
    }
}
