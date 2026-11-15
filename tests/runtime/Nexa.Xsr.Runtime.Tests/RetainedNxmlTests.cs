using Nexa.Sidecar.Protocol;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Runtime.Tests;

internal static partial class Program
{
    private static async ValueTask NxmlRegisteredCallbacksKeepDocumentBoundaries()
    {
        XsrStateStoreBuilder builder = new(); builder.Cell<XsrNxmlModuleSnapshot>(NxmlModuleState, "Test");
        var store = builder.Build(); var command = XsrSemanticId.Parse("test.callback.registered");
        var scalar = XsrSemanticId.Parse("test.callback.scalar");
        XsrNxmlModuleRuntime runtime = new(store, store.Resolve(NxmlModuleState), (source, _) => new(source.Target, []));
        var (session, peer) = await PatchSession(new(CaptionPoint),
            [new(SidecarRegistrationKind.Command, command.Value, 0, SidecarWireCodecs.GeneratedDto),
             new(SidecarRegistrationKind.Command, scalar.Value, 0, SidecarWireCodecs.Utf8String)], features: SidecarFeatures.All, nxml: runtime);
        using (session) using (peer)
        {
            await SnapshotAll(session, peer); await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            var loop = session.RunReceiveLoopAsync().AsTask();
            await session.ReplacePresentationAsync(0, [DynamicPage("Callback")]);
            var exposure = session.CapturePresentation(1).Documents[0].Exposure;
            AssertTrue(runtime.CanDispatchCallback(exposure, command));
            AssertFalse(runtime.CanDispatchCallback(exposure, scalar));
            AssertFalse(runtime.CanDispatchCallback(Guid.NewGuid(), command));
            AssertFalse((await runtime.DispatchFormAsync(exposure, command, "not-in-markup", new Dictionary<string, string>())).IsSuccess);
            AssertFalse((await runtime.DispatchCallbackAsync(exposure, scalar, "not-a-form")).IsSuccess);
            AssertFalse((await runtime.DispatchCallbackAsync(exposure, XsrSemanticId.Parse("host.only.command"), "not-own-command")).IsSuccess);
            var observation = new Dictionary<string, string> { ["kind"] = "Pointer", ["text"] = "initial" };
            AssertFalse((await runtime.DispatchCallbackAsync(exposure, scalar, "observation", observation)).IsSuccess);
            AssertFalse((await runtime.DispatchCallbackAsync(Guid.NewGuid(), command, "observation", observation)).IsSuccess);
            AssertFalse((await runtime.DispatchCallbackAsync(exposure, command, "oversized", new Dictionary<string, string>
            { ["text"] = new('x', SidecarUiForms.MaximumValueLength + 1) })).IsSuccess);
            var observed = runtime.DispatchCallbackAsync(exposure, command, "surface", observation).AsTask();
            observation["text"] = "changed after dispatch";
            var observedFrame = await DataPlaneReceiveAsync(peer);
            var observedForm = SidecarUiForms.Decode(SidecarDataMessages.DecodeBinaryRequest(observedFrame.Payload.Span).Value.ToArray());
            AssertEqual("surface", observedForm.Argument); AssertEqual(2, observedForm.Fields.Length);
            AssertEqual("initial", System.Text.Encoding.UTF8.GetString(observedForm.Fields.Single(field => field.Key == "text").Utf8Value));
            await peer.SendAsync(new(SidecarProtocol.Version, SidecarMessageType.CommandResult, SidecarFrameTraits.Final,
                observedFrame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(true, new(0, []), null)));
            AssertTrue((await observed).IsSuccess);
            var action = runtime.DispatchCallbackAsync(exposure, command, "shortcut").AsTask();
            var frame = await DataPlaneReceiveAsync(peer);
            var form = SidecarUiForms.Decode(SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span).Value.ToArray());
            AssertEqual("shortcut", form.Argument); AssertEqual(0, form.Fields.Length);
            await session.ReconcilePresentationAsync(1, [new(0, null, new Dictionary<string, string>())]);
            AssertTrue(runtime.CanDispatchCallback(exposure, command)); AssertFalse(action.IsCompleted);
            await session.ReplacePresentationAsync(2, []);
            var cancel = await DataPlaneReceiveAsync(peer);
            AssertEqual(SidecarMessageType.Cancel, cancel.MessageType); AssertEqual(frame.CorrelationId, cancel.CorrelationId);
            AssertFalse((await action).IsSuccess); AssertFalse(runtime.CanDispatchCallback(exposure, command));
            session.Dispose(); await loop.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var (legacy, legacyPeer) = await PatchSession(new(CaptionPoint),
            [new(SidecarRegistrationKind.Command, command.Value, 0, SidecarWireCodecs.GeneratedDto)],
            features: SidecarFeatures.All & ~SidecarFeatures.UiForms, nxml: runtime);
        using (legacy) using (legacyPeer)
        {
            await SnapshotAll(legacy, legacyPeer); await legacy.ActivateAsync(); await DataPlaneReceiveAsync(legacyPeer);
            await legacy.ReplacePresentationAsync(0, [DynamicPage("LegacyCallback")]);
            AssertFalse(runtime.CanDispatchCallback(legacy.CapturePresentation(1).Documents[0].Exposure, command));
        }
    }

    private static async ValueTask NxmlReconcileRetainsOnlySelectedActionLifetimes()
    {
        XsrStateStoreBuilder builder = new(); builder.Cell<XsrNxmlModuleSnapshot>(NxmlModuleState, "Test");
        var store = builder.Build(); var command = XsrSemanticId.Parse("test.retained.action");
        XsrNxmlModuleRuntime runtime = new(store, store.Resolve(NxmlModuleState), (source, _) => new(source.Target, [command]));
        var (session, peer) = await PatchSession(new(CaptionPoint),
            [new(SidecarRegistrationKind.Command, command.Value, 0, SidecarWireCodecs.GeneratedDto)], features: SidecarFeatures.All, nxml: runtime);
        using (session) using (peer)
        {
            await SnapshotAll(session, peer); await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            var loop = session.RunReceiveLoopAsync().AsTask();
            await session.ReplacePresentationAsync(0, [DynamicPage("Keep"), DynamicPage("Drop")]);
            var before = session.CapturePresentation(1);
            var keep = runtime.DispatchFormAsync(before.Documents[0].Exposure, command, "keep", new Dictionary<string, string>()).AsTask();
            var keepFrame = await DataPlaneReceiveAsync(peer);
            var drop = runtime.DispatchFormAsync(before.Documents[1].Exposure, command, "drop", new Dictionary<string, string>()).AsTask();
            var dropFrame = await DataPlaneReceiveAsync(peer);
            await session.ReconcilePresentationAsync(1,
                [new(0, null, new Dictionary<string, string>()), new(-1, DynamicPage("Added"), new Dictionary<string, string>())]);
            var after = session.CapturePresentation(2);
            AssertTrue(before.Retirement.IsCancellationRequested);
            AssertFalse(after.Retirement.IsCancellationRequested);
            AssertEqual(before.Documents[0].Exposure, after.Documents[0].Exposure);
            AssertFalse(before.Documents[1].Exposure == after.Documents[1].Exposure);
            var cancelled = await DataPlaneReceiveAsync(peer);
            AssertEqual(SidecarMessageType.Cancel, cancelled.MessageType);
            AssertEqual(dropFrame.CorrelationId, cancelled.CorrelationId);
            AssertFalse((await drop).IsSuccess); AssertFalse(keep.IsCompleted);
            await peer.SendAsync(new(SidecarProtocol.Version, SidecarMessageType.CommandResult, SidecarFrameTraits.Final,
                keepFrame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(true, new(0, []), null)));
            AssertTrue((await keep).IsSuccess);

            var moving = runtime.DispatchFormAsync(after.Documents[0].Exposure, command, "move", new Dictionary<string, string>()).AsTask();
            var movingFrame = await DataPlaneReceiveAsync(peer);
            await session.ReconcilePresentationAsync(2,
                [new(1, null, new Dictionary<string, string>()), new(0, null, new Dictionary<string, string>())]);
            var reordered = session.CapturePresentation(3);
            AssertEqual(after.Documents[0].Exposure, reordered.Documents[1].Exposure);
            AssertEqual(after.Documents[1].Exposure, reordered.Documents[0].Exposure);
            AssertFalse(moving.IsCompleted);
            // The original whole-set replacement remains a retirement boundary after any transfers.
            await session.ReplacePresentationAsync(3, [DynamicPage("WholeReplacement")]);
            cancelled = await DataPlaneReceiveAsync(peer);
            AssertEqual(SidecarMessageType.Cancel, cancelled.MessageType);
            AssertEqual(movingFrame.CorrelationId, cancelled.CorrelationId);
            AssertFalse((await moving).IsSuccess);
            AssertFalse((await runtime.DispatchFormAsync(reordered.Documents[1].Exposure, command, "stale", new Dictionary<string, string>())).IsSuccess);
            session.Dispose(); await loop.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async ValueTask NxmlReconcileRejectsInvalidCandidatesAtomically()
    {
        XsrStateStoreBuilder builder = new(); builder.Cell<XsrNxmlModuleSnapshot>(NxmlModuleState, "Test");
        var store = builder.Build(); var state = store.Resolve(NxmlModuleState);
        XsrNxmlModuleRuntime runtime = new(store, state, (source, model) =>
            model.GetValueOrDefault("text") == "invalid" ? throw new InvalidDataException("Bad model") : new(source.Target, []));
        var (session, peer) = await PatchSession(new(CaptionPoint), [], features: SidecarFeatures.All, nxml: runtime);
        using (session) using (peer)
        {
            await SnapshotAll(session, peer); await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            await session.ReplacePresentationAsync(0, [DynamicPage("Owned")]);
            var before = session.CapturePresentation(1);
            IReadOnlyDictionary<string, string> empty = new Dictionary<string, string>();
            foreach (var candidates in new XsrNxmlSourceCandidate[][]
            {
                [new(0, null, empty), new(0, null, empty)], [new(1, null, empty)], [new(-2, null, empty)],
                [new(-1, null, empty)], [new(0, DynamicPage("UnexpectedSource"), empty)],
                [new(0, null, empty), new(-1, DynamicPage("Owned"), empty)],
                Enumerable.Range(0, 33).Select(index => new XsrNxmlSourceCandidate(-1, DynamicPage("Many" + index), empty)).ToArray(),
                [new(0, null, new Dictionary<string, string> { ["text"] = new('x', 512 * 1024 + 1) })]
            }) await AssertThrowsAsync<SidecarProtocolException>(async () => await session.ReconcilePresentationAsync(1, candidates));
            await AssertThrowsAsync<InvalidOperationException>(async () => await session.ReconcilePresentationAsync(0, [new(0, null, empty)]));
            await AssertThrowsAsync<InvalidDataException>(async () => await session.ReconcilePresentationAsync(1,
                [new(0, null, new Dictionary<string, string> { ["text"] = "invalid" })]));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await AssertThrowsAsync<OperationCanceledException>(async () => await session.ReconcilePresentationAsync(1, [new(0, null, empty)], cancelled.Token));
            AssertEqual(1L, session.PresentationRevision);
            AssertFalse(before.Retirement.IsCancellationRequested);
            AssertEqual(before.Documents[0].Exposure, session.CapturePresentation(1).Documents[0].Exposure);
            runtime.RejectPresentation(before.Documents[0].Exposure);
            AssertTrue(before.Retirement.IsCancellationRequested);
            await AssertThrowsAsync<SidecarProtocolException>(async () => await session.ReconcilePresentationAsync(1, [new(0, null, empty)]));
            // Recovery may install fresh documents, but cannot resurrect a retired identity.
            await session.ReconcilePresentationAsync(1, [new(-1, DynamicPage("Recovered"), empty)]);
            var recovered = session.CapturePresentation(2);
            AssertFalse(before.Documents[0].Exposure == recovered.Documents[0].Exposure);
            await session.ReconcilePresentationAsync(2, []);
            AssertTrue(recovered.Retirement.IsCancellationRequested);
            AssertEqual(0, session.CapturePresentation(3).Documents.Count);
        }
        var (legacy, legacyPeer) = await PatchSession(new(CaptionPoint), [], features: SidecarFeatures.All & ~SidecarFeatures.RetainedDocuments, nxml: runtime);
        using (legacy) using (legacyPeer)
        {
            await SnapshotAll(legacy, legacyPeer); await legacy.ActivateAsync(); await DataPlaneReceiveAsync(legacyPeer);
            await AssertThrowsAsync<InvalidOperationException>(async () => await legacy.ReconcilePresentationAsync(0, []));
            AssertEqual(0L, legacy.PresentationRevision);
        }
    }
    private static async ValueTask NxmlReconcileOwnsDataAndRejectsLateCandidates()
    {
        XsrStateStoreBuilder builder = new(); builder.Cell<XsrNxmlModuleSnapshot>(NxmlModuleState, "Test");
        var store = builder.Build(); var state = store.Resolve(NxmlModuleState);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int block = 0;
        XsrNxmlModuleRuntime runtime = new(store, state, (source, model) =>
        {
            if (model.GetValueOrDefault("text") == "blocked" && Interlocked.Exchange(ref block, 0) == 1)
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Candidate compilation did not resume.");
            }
            return new(source.Target + ":" + model.GetValueOrDefault("text"), []);
        });
        var (session, peer) = await PatchSession(new(CaptionPoint), [], features: SidecarFeatures.All, nxml: runtime);
        using (session) using (peer)
        {
            await SnapshotAll(session, peer); await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            await session.ReplacePresentationAsync(0, [DynamicPage("Owned")]);
            var initial = session.CapturePresentation(1);
            IReadOnlyDictionary<string, string> blocked = new Dictionary<string, string> { ["text"] = "blocked" };
            Volatile.Write(ref block, 1);
            var first = Task.Run(async () => await session.ReconcilePresentationAsync(1, [new(0, null, blocked)]));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var mutable = new Dictionary<string, string> { ["text"] = "owned model" };
            XsrNxmlSourceCandidate[] candidates = [new(0, null, mutable), new(-1, DynamicPage("OwnedNew"), mutable)];
            var waiting = session.ReconcilePresentationAsync(2, candidates).AsTask();
            AssertFalse(waiting.IsCompleted);
            mutable["text"] = "caller changed model";
            candidates[0] = new(-1, DynamicPage("CallerChangedSource"), mutable);
            release.Set(); await first; await waiting;
            var current = session.CapturePresentation(3);
            AssertEqual(initial.Documents[0].Exposure, current.Documents[0].Exposure);
            AssertEqual("#OwnedNew", current.Documents[1].Source.Target);
            AssertTrue(store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.All(module =>
                ((string)module.Presentation.Artifact).EndsWith(":owned model", StringComparison.Ordinal)));

            release.Reset(); entered = new(TaskCreationOptions.RunContinuationsAsynchronously); Volatile.Write(ref block, 1);
            using var cancelled = new CancellationTokenSource();
            var lateCancel = Task.Run(async () => await session.ReconcilePresentationAsync(3, [new(0, null, blocked)], cancelled.Token));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancelled.Cancel(); release.Set();
            await AssertThrowsAsync<OperationCanceledException>(async () => await lateCancel);
            AssertEqual(3L, session.PresentationRevision); AssertFalse(current.Retirement.IsCancellationRequested);
            AssertEqual(2, session.CapturePresentation(3).Documents.Count);

            release.Reset(); entered = new(TaskCreationOptions.RunContinuationsAsynchronously); Volatile.Write(ref block, 1);
            var lateRetirement = Task.Run(async () => await session.ReconcilePresentationAsync(3, [new(0, null, blocked)]));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); session.Dispose(); release.Set();
            await AssertThrowsAsync<InvalidOperationException>(async () => await lateRetirement);
            AssertEqual(3L, session.PresentationRevision); AssertTrue(current.Retirement.IsCancellationRequested);
            AssertEqual(0, store.Read<XsrNxmlModuleSnapshot>(state).Value!.Modules.Count);
        }
    }

}
