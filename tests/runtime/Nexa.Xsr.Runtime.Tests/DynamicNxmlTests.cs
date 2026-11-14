using Nexa.Sidecar.Protocol;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Runtime.Tests;

internal static partial class Program
{
    private static async ValueTask NxmlRouteReadsDoNotDeadlockSourceReplacement()
    {
        XsrStateStoreBuilder builder = new(); builder.Cell<XsrNxmlModuleSnapshot>(NxmlModuleState, "Test");
        var store = builder.Build(); var state = store.Resolve(NxmlModuleState); var command = XsrSemanticId.Parse("test.form");
        XsrNxmlModuleRuntime runtime = new(store, state, (_, _) => new("page", [command]));
        var (session, peer) = await PatchSession(new(CaptionPoint),
            [new(SidecarRegistrationKind.Command, command.Value, 0, SidecarWireCodecs.GeneratedDto), NxmlItem()], features: SidecarFeatures.All, nxml: runtime);
        await SnapshotAll(session, peer); await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
        using var start = new Barrier(2);
        int finished = 0;
        var writer = Task.Run(async () =>
        {
            start.SignalAndWait();
            try
            {
                for (int i = 0; i < 300; i++) await session.ReplacePresentationAsync(i, [DynamicPage("Page")]);
            }
            finally { Volatile.Write(ref finished, 1); }
        });
        var reader = Task.Run(() =>
        {
            start.SignalAndWait();
            do
            {
                var exposure = store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.Single().Exposure;
                _ = runtime.ReadCommandRoute(exposure, command);
                _ = runtime.UsesFormSubmission(exposure, command);
            } while (Volatile.Read(ref finished) == 0);
        });
        try
        {
            await Task.WhenAll(writer, reader).WaitAsync(TimeSpan.FromSeconds(10));
            AssertEqual(300L, session.PresentationRevision);
        }
        finally
        {
            // A regression must report its timeout instead of deadlocking the test runner's cleanup.
            if (writer.IsCompleted && reader.IsCompleted) { session.Dispose(); peer.Dispose(); }
        }
    }

    private static async ValueTask NxmlFormsRequireNegotiatedFeatures()
    {
        foreach (var features in new[] { SidecarFeatures.All & ~SidecarFeatures.UiForms, SidecarFeatures.All & ~SidecarFeatures.LargeGeneratedDtos })
        {
            XsrStateStoreBuilder builder = new(); builder.Cell<XsrNxmlModuleSnapshot>(NxmlModuleState, "Test");
            var store = builder.Build(); var state = store.Resolve(NxmlModuleState); var command = XsrSemanticId.Parse("test.form");
            XsrNxmlModuleRuntime runtime = new(store, state, (_, _) => new("form", [command]));
            var (session, peer) = await PatchSession(new(CaptionPoint),
                [new(SidecarRegistrationKind.Command, command.Value, 0, SidecarWireCodecs.GeneratedDto), NxmlItem()], features: features, nxml: runtime);
            using (session) using (peer)
            {
                await SnapshotAll(session, peer); await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
                var exposure = store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.Single().Exposure;
                AssertEqual(features.HasFlag(SidecarFeatures.UiForms), runtime.UsesFormSubmission(exposure, command));
                AssertFalse((await runtime.DispatchFormAsync(exposure, command, "save", new Dictionary<string, string> { ["draft"] = new('x', 70000) })).IsSuccess);
                AssertFalse((await runtime.DispatchFormAsync(exposure, command, "save", new Dictionary<string, string> { ["bad"] = "\ud800" })).IsSuccess);
            }
        }
    }

    private static async ValueTask DynamicNxmlModelsRetainExposures()
    {
        XsrStateStoreBuilder builder = new(); builder.Cell<XsrNxmlModuleSnapshot>(NxmlModuleState, "Test");
        var store = builder.Build(); var state = store.Resolve(NxmlModuleState);
        XsrNxmlModuleRuntime runtime = new(store, state, (_, data) =>
            data.GetValueOrDefault("text") == "invalid" ? throw new InvalidDataException("Bad model") : new(data.GetValueOrDefault("text", "empty"), []));
        var (session, peer) = await PatchSession(new(CaptionPoint), [], features: SidecarFeatures.All, nxml: runtime);
        using (session) using (peer)
        {
            await SnapshotAll(session, peer); await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            Dictionary<string, string> callerModel = new() { ["text"] = "initial" };
            await session.ReplacePresentationWithModelsAsync(0, [DynamicPage("First"), DynamicPage("Second")], [callerModel, new Dictionary<string, string>()]);
            callerModel["text"] = "mutated caller memory";
            var before = store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.ToArray();
            AssertEqual("initial", (string)before[0].Presentation.Artifact);
            await session.UpdatePresentationModelsAsync(1, new Dictionary<int, IReadOnlyDictionary<string, string>> { [0] = new Dictionary<string, string> { ["text"] = "updated" } });
            var after = store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules;
            AssertEqual(before[0].Exposure, after[0].Exposure); AssertEqual(before[1].Exposure, after[1].Exposure);
            AssertEqual("updated", (string)after[0].Presentation.Artifact); AssertEqual("empty", (string)after[1].Presentation.Artifact);
            await AssertThrowsAsync<InvalidDataException>(async () => await session.UpdatePresentationModelsAsync(2,
                new Dictionary<int, IReadOnlyDictionary<string, string>> { [0] = new Dictionary<string, string> { ["text"] = "invalid" } }));
            await AssertThrowsAsync<InvalidOperationException>(async () => await session.UpdatePresentationModelsAsync(1,
                new Dictionary<int, IReadOnlyDictionary<string, string>> { [0] = new Dictionary<string, string>() }));
            await AssertThrowsAsync<SidecarProtocolException>(async () => await session.UpdatePresentationModelsAsync(2,
                new Dictionary<int, IReadOnlyDictionary<string, string>> { [2] = new Dictionary<string, string>() }));
            await AssertThrowsAsync<SidecarProtocolException>(async () => await session.UpdatePresentationModelsAsync(2,
                new Dictionary<int, IReadOnlyDictionary<string, string>> { [0] = new Dictionary<string, string> { ["text"] = new string('a', 512 * 1024 + 1) } }));
            AssertEqual(2L, session.PresentationRevision);
            await session.DeactivateAsync(); await DataPlaneReceiveAsync(peer);
            await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            var restored = store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules;
            AssertEqual("updated", (string)restored[0].Presentation.Artifact);
            AssertFalse(before[0].Exposure == restored[0].Exposure);
            await session.ReplacePresentationAsync(2, [DynamicPage("Reset")]);
            AssertEqual("empty", (string)store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.Single().Presentation.Artifact);
        }
    }

    private static SidecarNxmlDocument DynamicPage(string target, string source = "<Page />") =>
        new("#" + target, SidecarNxmlOperation.Page, "main.nxml", new Dictionary<string, string> { ["main.nxml"] = source });

    private static async ValueTask DynamicNxmlReplacementIsAtomicAndSessionOwned()
    {
        XsrStateStoreBuilder builder = new(); builder.Cell<XsrNxmlModuleSnapshot>(NxmlModuleState, "Test");
        var store = builder.Build(); var state = store.Resolve(NxmlModuleState);
        XsrNxmlModuleRuntime runtime = new(store, state, (document, _) =>
            document.Files[document.EntryPoint] == "invalid" ? throw new InvalidDataException("Bad source") : new(document.Target, []));
        var (first, peer) = await PatchSession(new(CaptionPoint), [], features: SidecarFeatures.All, nxml: runtime);
        var (other, otherPeer) = await PatchSession(new(CaptionPoint), [], features: SidecarFeatures.All, nxml: runtime);
        using (first) using (peer) using (other) using (otherPeer)
        {
            await SnapshotAll(first, peer); await first.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            await SnapshotAll(other, otherPeer); await other.ActivateAsync(); await DataPlaneReceiveAsync(otherPeer);
            await first.ReplacePresentationAsync(0, [DynamicPage("First")]);
            await other.ReplacePresentationAsync(0, [DynamicPage("Other")]);
            AssertEqual(1L, first.PresentationRevision);
            var exposure = first.CapturePresentation(1).Documents.Single().Exposure;
            var otherExposure = other.CapturePresentation(1).Documents.Single().Exposure;
            await WaitUntil(() => store.Read<XsrNxmlModuleSnapshot>(state).Value is { } snapshot
                && snapshot.Modules.Count == 2 && snapshot.Modules.Any(module => module.Exposure == exposure)
                && snapshot.Modules.Any(module => module.Exposure == otherExposure));
            AssertFalse(runtime.TryResolvePage(exposure, "#Other", out _));
            await AssertThrowsAsync<InvalidDataException>(async () => await first.ReplacePresentationAsync(1, [DynamicPage("Bad", "invalid")]));
            await AssertThrowsAsync<InvalidOperationException>(async () => await first.ReplacePresentationAsync(0, []));
            await AssertThrowsAsync<SidecarProtocolException>(async () => await first.ReplacePresentationAsync(1, [DynamicPage("Same"), DynamicPage("Same")]));
            AssertEqual(1L, first.PresentationRevision);
            AssertTrue(runtime.TryResolvePage(exposure, "#First", out _));
            runtime.RejectPresentation(exposure);
            // A renderer rejection retires exposures, but the active owner can submit a corrected set.
            await first.ReplacePresentationAsync(1, [DynamicPage("Second")]);
            AssertFalse(runtime.TryResolvePage(exposure, "#First", out _));
            var second = first.CapturePresentation(2).Documents.Single().Exposure;
            await WaitUntil(() => store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.Any(module => module.Exposure == second));
            await first.DeactivateAsync(); await DataPlaneReceiveAsync(peer);
            AssertFalse(runtime.TryResolvePage(second, "#Second", out _));
            await WaitUntil(() => store.Read<XsrNxmlModuleSnapshot>(state).Value is { } snapshot
                && snapshot.Modules.Count == 1 && snapshot.Modules[0].Exposure == otherExposure);
            await first.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            AssertEqual(2L, first.PresentationRevision);
            var restored = first.CapturePresentation(2).Documents.Single().Exposure;
            AssertFalse(second == restored);
            await WaitUntil(() => store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.Any(module => module.Exposure == restored));
            await first.ReplacePresentationAsync(2, []);
            AssertEqual(3L, first.PresentationRevision);
            AssertEqual(0, first.CapturePresentation(3).Documents.Count);
            AssertFalse(runtime.TryResolvePage(restored, "#Second", out _));
            await WaitUntil(() => store.Read<XsrNxmlModuleSnapshot>(state).Value is { } snapshot
                && snapshot.Modules.Count == 1 && snapshot.Modules[0].Source.Target == "#Other");
            other.Dispose();
            AssertFalse(runtime.TryResolvePage(otherExposure, "#Other", out _));
            await WaitUntil(() => store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.Count == 0);
        }
    }

    private static async ValueTask NxmlCommitDoesNotWaitForBlockedProjectionObserver()
    {
        XsrStateStoreBuilder builder = new(); builder.Cell<XsrNxmlModuleSnapshot>(NxmlModuleState, "Test");
        var store = builder.Build(); var state = store.Resolve(NxmlModuleState);
        XsrNxmlModuleRuntime runtime = new(store, state, (document, _) => new(document.Target, []));
        var (session, peer) = await PatchSession(new(CaptionPoint), [], features: SidecarFeatures.All, nxml: runtime);
        using (session) using (peer)
        using (var release = new ManualResetEventSlim())
        {
            await SnapshotAll(session, peer); await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            var entered = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
            int blocked = 0;
            void Observe(XsrStateChange change)
            {
                var snapshot = store.Read<XsrNxmlModuleSnapshot>(state).Value;
                if (snapshot is null || snapshot.Modules.Count != 1 || snapshot.Modules[0].Source.Target != "#Blocked"
                    || Interlocked.CompareExchange(ref blocked, 1, 0) != 0) return;
                entered.TrySetResult(snapshot.Modules[0].Exposure);
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Projection observer was not released.");
            }
            store.Changed += Observe;
            var replacing = Task.Run(async () => await session.ReplacePresentationAsync(0, [DynamicPage("Blocked")]));
            try
            {
                Guid exposure = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await session.ReplacePresentationAsync(1, []).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                AssertEqual(2L, session.PresentationRevision);
                AssertEqual(0, session.CapturePresentation(2).Documents.Count);
                AssertFalse(runtime.TryResolvePage(exposure, "#Blocked", out _));
                AssertEqual(exposure, store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.Single().Exposure);
                release.Set();
                await replacing.WaitAsync(TimeSpan.FromSeconds(5));
                await WaitUntil(() => store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.Count == 0);
            }
            finally
            {
                release.Set(); store.Changed -= Observe;
                await replacing.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private static async ValueTask DynamicNxmlDisconnectDuringCompilationCannotPublish()
    {
        XsrStateStoreBuilder builder = new(); builder.Cell<XsrNxmlModuleSnapshot>(NxmlModuleState, "Test");
        var store = builder.Build(); var state = store.Resolve(NxmlModuleState);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var proceed = new ManualResetEventSlim();
        XsrNxmlModuleRuntime runtime = new(store, state, (document, _) =>
        {
            entered.TrySetResult();
            if (!proceed.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return new(document.Target, []);
        });
        var (session, peer) = await PatchSession(new(CaptionPoint), [], features: SidecarFeatures.All, nxml: runtime);
        using (session) using (peer)
        {
            await SnapshotAll(session, peer); await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            var changing = Task.Run(async () => await session.ReplacePresentationAsync(0, [DynamicPage("Late")]));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                session.Dispose();
            }
            finally { proceed.Set(); }
            await AssertThrowsAsync<InvalidOperationException>(async () => await changing);
            AssertEqual(0L, session.PresentationRevision);
            AssertFalse(store.Read<XsrNxmlModuleSnapshot>(state).Value?.Modules.Count > 0);
        }
    }

    private static async ValueTask DynamicNxmlBudgetsAndCancellationPreservePredecessor()
    {
        XsrStateStoreBuilder builder = new(); builder.Cell<XsrNxmlModuleSnapshot>(NxmlModuleState, "Test");
        var store = builder.Build(); var state = store.Resolve(NxmlModuleState);
        XsrNxmlModuleRuntime runtime = new(store, state, (document, _) => new(document.Target, []));
        List<(SidecarHostSession Session, Nexa.Sidecar.Transport.SidecarConnection Plugin)> sessions = [];
        try
        {
            for (int i = 0; i < 4; i++)
            {
                var pair = await PatchSession(new(CaptionPoint), [], features: SidecarFeatures.All, nxml: runtime);
                sessions.Add(pair);
                await SnapshotAll(pair.Session, pair.Plugin); await pair.Session.ActivateAsync(); await DataPlaneReceiveAsync(pair.Plugin);
                await pair.Session.ReplacePresentationAsync(0, Enumerable.Range(0, 32).Select(n => DynamicPage("Page" + n)).ToArray());
            }
            var first = sessions[0].Session;
            // Replacement at the global limit charges the candidate instead of both old and new sets.
            // A concurrent mirror refresh can own the publication drain. The source commit
            // is immediate, but the serialized state projection completes on that publisher.
            var reduced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void ObserveReduced(XsrStateChange _)
            {
                if (store.Read<XsrNxmlModuleSnapshot>(state).Value?.Modules.Count == 97) reduced.TrySetResult();
            }
            store.Changed += ObserveReduced;
            try
            {
                await first.ReplacePresentationAsync(1, [DynamicPage("Reduced")]);
                ObserveReduced(default);
                await reduced.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally { store.Changed -= ObserveReduced; }
            AssertEqual(1, first.CapturePresentation(2).Documents.Count);
            AssertEqual(97, store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.Count);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await AssertThrowsAsync<OperationCanceledException>(async () => await first.ReplacePresentationAsync(2, [], cancelled.Token));
            await AssertThrowsAsync<SidecarProtocolException>(async () => await first.ReplacePresentationAsync(2,
                Enumerable.Range(0, 33).Select(n => DynamicPage("Page" + n)).ToArray()));
            AssertEqual(2L, first.PresentationRevision);
            AssertEqual(97, store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.Count);
        }
        finally { foreach (var pair in sessions) { pair.Session.Dispose(); pair.Plugin.Dispose(); } }
    }
}
