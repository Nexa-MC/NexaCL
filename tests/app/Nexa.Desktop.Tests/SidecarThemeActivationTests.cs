using System.Collections.Concurrent;
using Nexa.Services.Composition.Sidecars;
using Nexa.Services.Themes;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Activation = Nexa.Sidecar.Host.ThemeActivation.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarThemeActivationAbi()
    {
        using var fixture = new ThemeLifecycleFixture();
        string first = fixture.MakeTheme("sidecar-first", "Text#TitleMain{Foreground:#102030;}");
        string second = fixture.MakeTheme("sidecar-second", "Text#TitleMain{Foreground:#304050;}");
        AssertTrue(fixture.Run(fixture.Session.ImportAsync(first, ThemeSourceKind.Directory, false, default)).IsSuccess);
        AssertTrue(fixture.Run(fixture.Session.ImportAsync(second, ThemeSourceKind.Directory, false, default)).IsSuccess);
        var a = fixture.Session.Snapshot.Catalogue.Themes.Single(value => value.Manifest.Id == "fixture.sidecar-first");
        var b = fixture.Session.Snapshot.Catalogue.Themes.Single(value => value.Manifest.Id == "fixture.sidecar-second");
        AssertTrue(fixture.Run(fixture.Session.ActivateAsync(a.SourceIdentity, false, default)).IsSuccess);
        var nxml = new DesktopSidecarNxml(fixture.Fixture.Store);
        var (session, peer) = ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All, nxml: nxml).GetAwaiter().GetResult();
        var (other, otherPeer) = ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All).GetAwaiter().GetResult();
        using (session) using (peer) using (other) using (otherPeer)
        {
            ConcurrentQueue<XsrError> failures = [];
            var api = new SidecarThemeActivationApi(caller => caller.SessionId == session.SessionId ? session : other,
                new NoopDispatchObserver(), failures.Enqueue);
            using var wire = new ThemeActivationWire(api, session);
            using var foreign = new ThemeActivationWire(api, other);
            IDisposable? binding = null;
            Activation.HostThemeActivation Input(ThemeCatalogueEntry entry) => new(entry.SourceIdentity, entry.SourceRevision, fixture.Current().Revision);
            IDisposable Bind() => api.Bind(new((input, preview, token) => fixture.Session.ActivateExternalAsync(
                input.SourceIdentity, input.SourceRevision, input.ExpectedRevision, preview, token), fixture.Session.EndExternalPreviewAsync));
            void Drain(Task task) { fixture.Pump(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
            try
            {
                AssertFalse(fixture.Run(wire.Preview(Input(b), 1)).IsSuccess);
                AssertTrue(fixture.Run(wire.Client.DiscoverAsync().AsTask()).IsSuccess);
                AssertTrue(fixture.Run(foreign.Client.DiscoverAsync().AsTask()).IsSuccess);
                AssertEqual("xsr.unavailable", fixture.Run(wire.Preview(Input(b), 1)).Error!.Code.Value);
                binding = Bind();
                var lifetime = session.CaptureActivationLifetime();
                AssertTrue(fixture.Run(wire.Preview(Input(b), 1)).IsSuccess);
                AssertTrue(fixture.Session.Snapshot.IsPreview); AssertEqual(a.SourceIdentity, fixture.Current().Selection!.SourceIdentity);
                Drain(session.ReplacePresentationAsync(0, []).AsTask());
                AssertEqual(lifetime, session.CaptureActivationLifetime()); AssertFalse(lifetime.IsCancellationRequested);
                AssertTrue(fixture.Session.Snapshot.IsPreview);
                AssertTrue(fixture.Run(foreign.End(1)).IsSuccess); AssertTrue(fixture.Session.Snapshot.IsPreview);
                AssertTrue(fixture.Run(fixture.Session.ActivateAsync(a.SourceIdentity, true, default)).IsSuccess);
                AssertTrue(fixture.Run(wire.End(1)).IsSuccess); AssertTrue(fixture.Run(wire.End(1)).IsSuccess);
                AssertEqual(a.SourceIdentity, fixture.Session.Snapshot.PresentedSourceIdentity);
                AssertTrue(fixture.Session.Snapshot.IsPreview); // An older lease cannot end the user's newer manual preview.
                AssertTrue(fixture.Run(fixture.Session.ActivateAsync(a.SourceIdentity, false, default)).IsSuccess);
                AssertTrue(fixture.Run(wire.End(3)).IsSuccess);
                AssertFalse(fixture.Run(wire.Preview(Input(b), 2)).IsSuccess);
                AssertFalse(fixture.Run(wire.Preview(Input(b), 3)).IsSuccess);
                AssertTrue(fixture.Run(wire.Preview(Input(b), 4)).IsSuccess);
                AssertFalse(fixture.Run(wire.Preview(Input(b) with { SourceRevision = "stale" }, 5)).IsSuccess);
                AssertTrue(fixture.Session.Snapshot.IsPreview);
                long blocked = fixture.Appearance.Current!.Generation + 1;
                var restoring = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                fixture.Session.WaitForAppearanceConsumers = (generation, token) =>
                {
                    if (generation != blocked) return Task.CompletedTask;
                    restoring.TrySetResult(); return Task.Delay(Timeout.Infinite, token);
                };
                var ending = wire.End(4);
                fixture.Pump(() => restoring.Task.IsCompleted);
                fixture.Session.WaitForAppearanceConsumers = null;
                var failedUserChange = fixture.Session.ImportAsync(Path.Combine(fixture.Fixture.TemporaryDirectory, "missing-theme"),
                    ThemeSourceKind.Directory, false, default);
                AssertFalse(fixture.Run(failedUserChange).IsSuccess);
                AssertTrue(fixture.Run(ending).IsSuccess); AssertFalse(fixture.Session.Snapshot.IsPreview);
                AssertEqual(a.SourceIdentity, fixture.Session.Snapshot.PresentedSourceIdentity);
                AssertTrue(fixture.Run(wire.Preview(Input(b), 6)).IsSuccess);
                Drain(session.DeactivateAsync().AsTask());
                AssertEqual(SidecarMessageType.Deactivate, peer.ReceiveAsync().AsTask().GetAwaiter().GetResult().MessageType);
                fixture.Pump(() => !fixture.Session.Snapshot.IsPreview);
                AssertTrue(lifetime.IsCancellationRequested); AssertEqual(a.SourceIdentity, fixture.Session.Snapshot.PresentedSourceIdentity);
                bool denied = false;
                try { _ = session.CaptureActivationLifetime(); }
                catch (InvalidOperationException) { denied = true; }
                AssertTrue(denied);
                Drain(session.ActivateAsync().AsTask());
                AssertEqual(SidecarMessageType.Activate, peer.ReceiveAsync().AsTask().GetAwaiter().GetResult().MessageType);
                AssertFalse(lifetime == session.CaptureActivationLifetime()); AssertFalse(session.CaptureActivationLifetime().IsCancellationRequested);
                AssertTrue(fixture.Run(wire.Preview(Input(b), 1)).IsSuccess);
                binding.Dispose(); binding = null;
                fixture.Pump(() => !fixture.Session.Snapshot.IsPreview);
                binding = Bind();
                AssertFalse(fixture.Run(wire.Preview(Input(b), 1)).IsSuccess); // Rebinding cannot forget the activation's watermark.
                AssertTrue(fixture.Run(wire.Preview(Input(b), 2)).IsSuccess);
                AssertTrue(fixture.Run(wire.End(2)).IsSuccess);
                AssertTrue(fixture.Run(wire.Commit(Input(b))).IsSuccess);
                session.Dispose();
                AssertEqual(b.SourceIdentity, fixture.Current().Selection!.SourceIdentity); AssertFalse(fixture.Session.Snapshot.IsPreview);
                AssertTrue(fixture.Run(foreign.Preview(Input(a), 2)).IsSuccess);
                File.WriteAllText(Path.Combine(b.InstalledDirectory!, "main.nss"), "invalid committed source");
                AssertTrue(fixture.Run(foreign.End(2)).IsSuccess);
                AssertEqual("builtin", fixture.Session.Snapshot.PresentedSourceIdentity);
                AssertTrue(fixture.Log.Any(value => value.Contains("could not be restored after preview", StringComparison.Ordinal)));
                AssertTrue(failures.IsEmpty);
            }
            finally { binding?.Dispose(); Drain(api.DisposeAsync().AsTask()); }
        }
    }

    private static void SidecarThemeActivationRaces() => SidecarThemeActivationRacesAsync().GetAwaiter().GetResult();
    private static async Task SidecarThemeActivationRacesAsync()
    {
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All);
        using (session) using (peer)
        {
            Activation.HostThemeActivation input = new("source", "revision", 1);
            ConcurrentQueue<long> ended = [];
            var entered = new SemaphoreSlim(0);
            var release = new TaskCompletionSource<XsrResult<long>>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (var api = new SidecarThemeActivationApi(_ => session, new NoopDispatchObserver()))
            using (var wire = new ThemeActivationWire(api, session))
            {
                int activations = 0;
                var binding = api.Bind(new((_, _, _) => { Interlocked.Increment(ref activations); entered.Release(); return release.Task; },
                    (generation, _) => { ended.Enqueue(generation); return Task.FromResult(XsrResult.Success()); }));
                AssertTrue((await wire.Client.DiscoverAsync()).IsSuccess);
                var pending = wire.Preview(input, 1);
                AssertTrue(await entered.WaitAsync(TimeSpan.FromSeconds(5)));
                var ending = wire.End(1); AssertFalse(ending.IsCompleted);
                release.TrySetResult(XsrResult.Success(77L));
                AssertFalse((await pending.WaitAsync(TimeSpan.FromSeconds(5))).IsSuccess);
                AssertTrue((await ending.WaitAsync(TimeSpan.FromSeconds(5))).IsSuccess);
                AssertEqual(77L, ended.Single());
                AssertTrue((await wire.End(100)).IsSuccess);
                AssertFalse((await wire.Preview(input, 100)).IsSuccess); AssertEqual(1, activations);
                release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                pending = wire.Preview(input, 101);
                AssertTrue(await entered.WaitAsync(TimeSpan.FromSeconds(5)));
                binding.Dispose();
                AssertFalse((await pending.WaitAsync(TimeSpan.FromSeconds(5))).IsSuccess);
                int replacementEnds = 0;
                using var replacement = api.Bind(new((_, _, _) => Task.FromResult(XsrResult.Success(99L)),
                    (_, _) => { replacementEnds++; return Task.FromResult(XsrResult.Success()); }));
                AssertTrue((await wire.Commit(input)).IsSuccess);
                release.TrySetResult(XsrResult.Success(88L));
                AssertTrue((await wire.End(101)).IsSuccess);
                AssertTrue(ended.SequenceEqual(new long[] { 77, 88 })); AssertEqual(0, replacementEnds);
            }
            entered.Dispose();
            // Cancelled requests cannot free slots still occupied by uncooperative native callbacks.
            using var allEntered = new SemaphoreSlim(0);
            var allRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ConcurrentQueue<long> retired = [];
            await using (var api = new SidecarThemeActivationApi(_ => session, new NoopDispatchObserver()))
            using (var wire = new ThemeActivationWire(api, session))
            {
                int count = 0;
                using var binding = api.Bind(new(async (_, _, _) =>
                {
                    int generation = Interlocked.Increment(ref count); allEntered.Release();
                    await allRelease.Task; return XsrResult.Success((long)generation);
                }, (generation, _) => { retired.Enqueue(generation); return Task.FromResult(XsrResult.Success()); }));
                AssertTrue((await wire.Client.DiscoverAsync()).IsSuccess);
                try
                {
                    for (int id = 1; id <= 64; id++)
                    {
                        using var stop = new CancellationTokenSource();
                        var pending = wire.Preview(input, id, stop.Token);
                        AssertTrue(await allEntered.WaitAsync(TimeSpan.FromSeconds(5)));
                        stop.Cancel();
                        AssertFalse((await pending.WaitAsync(TimeSpan.FromSeconds(5))).IsSuccess);
                        await wire.DrainRequestsAsync();
                    }
                    AssertEqual("xsr.backpressure", (await wire.Preview(input, 65)).Error!.Code.Value); AssertEqual(64, count);
                }
                finally { allRelease.TrySetResult(); }
                await api.DisposeAsync();
                AssertEqual(64, retired.Count); AssertEqual(64, retired.Distinct().Count());
            }
        }
    }

    private sealed class ThemeActivationWire : IDisposable
    {
        private readonly SidecarHostRequestClient _requests;
        private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _pending = [];
        internal SidecarThemeActivationClient Client { get; }
        internal ThemeActivationWire(SidecarThemeActivationApi api, SidecarHostSession session)
        {
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[2304].Authorize(caller with { PackageName = "foreign" }));
            SidecarHostRequestClient? wire = null;
            _requests = wire = new(async (frame, token) =>
            {
                if (frame.MessageType == SidecarMessageType.Cancel)
                {
                    var cancel = SidecarStateSnapshot.DecodeCancel(frame.Payload.Span);
                    if (_pending.TryGetValue(cancel.CorrelationId, out var active))
                        try { active.Cancel(); } catch (ObjectDisposedException) { }
                    return;
                }
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
                AssertTrue(_pending.TryAdd(frame.CorrelationId.Value, lifetime));
                try
                {
                    var request = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span); var route = routes[request.ContractId];
                    var result = route.Authorize(caller) ? await route.Invoke(caller, request.Value, lifetime.Token)
                        : XsrResult.Failure<SidecarBinaryValue>(XsrRuntimeErrors.RouteNotFound());
                    bool accepted = wire!.TryAcceptReply(new(SidecarProtocol.Version, route.IsQuery ? SidecarMessageType.QueryResult : SidecarMessageType.CommandResult,
                        SidecarFrameTraits.None, frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(result.IsSuccess,
                            result.IsSuccess ? result.Value : new(SidecarWireCodecs.GeneratedDto, []), result.Error?.Code.Value)));
                    AssertTrue(accepted || lifetime.IsCancellationRequested);
                }
                finally { _pending.TryRemove(frame.CorrelationId.Value, out _); }
            });
            _requests.Activate(SidecarFeatures.All); Client = new(_requests, new NoopDispatchObserver());
        }
        internal Task<XsrResult> Preview(Activation.HostThemeActivation theme, long id, CancellationToken token = default)
        {
            AssertTrue(Client.Commands.TryResolve(Activation.SidecarThemeActivationRoutes.Preview, out var route));
            return Client.Commands.Dispatch(route, new Activation.HostThemePreview(theme, id), cancellationToken: token).Completion;
        }
        internal Task<XsrResult> Commit(Activation.HostThemeActivation theme)
        {
            AssertTrue(Client.Commands.TryResolve(Activation.SidecarThemeActivationRoutes.Activate, out var route));
            return Client.Commands.Dispatch(route, theme).Completion;
        }
        internal Task<XsrResult> End(long id)
        {
            AssertTrue(Client.Commands.TryResolve(Activation.SidecarThemeActivationRoutes.EndPreview, out var route));
            return Client.Commands.Dispatch(route, new Activation.HostThemePreviewEnd(id)).Completion;
        }
        internal async Task DrainRequestsAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!_pending.IsEmpty) await Task.Delay(1, deadline.Token);
        }
        public void Dispose() => _requests.Dispose();
    }
}
