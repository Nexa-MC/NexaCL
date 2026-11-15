using System.Security.Cryptography;
using System.Text;
using Nexa.Services.Composition.Sidecars;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;
using SkiaSharp;
using Rasters = Nexa.Sidecar.Host.RasterControl.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarRasterAbi() => SidecarRasterAbiAsync().GetAwaiter().GetResult();
    private static async Task SidecarRasterAbiAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store); List<string> warnings = [];
        var view = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add);
        const string command = "test.rasters.input";
        var (session, peer) = await ActivateCaptionPatch(new(), "", declarations:
        [
            new(SidecarRegistrationKind.Command, command, 0, SidecarWireCodecs.GeneratedDto),
            new(SidecarRegistrationKind.Command, "test.rasters.scalar", 0, SidecarWireCodecs.Utf8String),
            PageDeclaration("test.rasters.page", "#RasterPage", SidecarNxmlOperation.Page,
                """<Page Key="RasterPage"><StackPanel><TextInput Key="RasterSecret" Label="Draft" Height="36"/><Dynamic Key="RasterSlot" Height="96" /></StackPanel></Page>"""),
            PageDeclaration("test.rasters.modal", "#RasterModal", SidecarNxmlOperation.Dialog,
                """<Page Key="RasterModal"><Dynamic Key="RasterModalSlot" Height="96" /></Page>""")
        ], features: SidecarFeatures.All, nxml: adapter);
        var (other, otherPeer) = await ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All & ~SidecarFeatures.UiForms, nxml: adapter);
        using (session) using (peer) using (other) using (otherPeer)
        {
            var receive = session.RunReceiveLoopAsync().AsTask();
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var api = new SidecarRasterControlApi(_ => session, new NoopDispatchObserver(), adapter.RasterActions);
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[4352].Authorize(caller with { SessionId = other.SessionId }));
            AssertFalse(routes[4351].Authorize(caller with { PackageName = "foreign" }));
            var unsupported = new SidecarRasterControlApi(_ => other, new NoopDispatchObserver(), adapter.RasterActions);
            AssertFalse(unsupported.Routes.Any(route => route.Authorize(new(other.PluginName, other.SessionId))));
            SidecarHostRequestClient? wire = null;
            using var requests = wire = new(async (frame, token) =>
            {
                var request = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span); var route = routes[request.ContractId];
                var result = route.Authorize(caller) ? await route.Invoke(caller, request.Value, token)
                    : XsrResult.Failure<SidecarBinaryValue>(XsrRuntimeErrors.RouteNotFound());
                AssertTrue(wire!.TryAcceptReply(new(SidecarProtocol.Version, route.IsQuery ? SidecarMessageType.QueryResult : SidecarMessageType.CommandResult,
                    SidecarFrameTraits.None, frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(result.IsSuccess,
                        result.IsSuccess ? result.Value : new(SidecarWireCodecs.GeneratedDto, []), result.Error?.Code.Value))));
            });
            requests.Activate(SidecarFeatures.All);
            var client = new SidecarRasterControlClient(requests, new NoopDispatchObserver());
            Task<XsrResult> Call<T>(XsrSemanticId semantic, T input) where T : notnull
            { AssertTrue(client.Commands.TryResolve(semantic, out var route)); return client.Commands.Dispatch(route, input).Completion; }
            AssertFalse((await Call(Rasters.SidecarRasterControlRoutes.Replace, new Rasters.HostRasterSet(0, []))).IsSuccess);
            AssertTrue((await client.DiscoverAsync()).IsSuccess);
            byte[] png = RasterPng(64, 64);
            async Task Upload(long id, byte[] bytes)
            {
                AssertTrue((await Call(Rasters.SidecarRasterControlRoutes.Begin, new Rasters.HostRasterBegin(id, bytes.Length, SHA256.HashData(bytes)))).IsSuccess);
                for (int offset = 0; offset < bytes.Length; offset += 65536)
                    AssertTrue((await Call(Rasters.SidecarRasterControlRoutes.Append, new Rasters.HostRasterAppend(id, offset, bytes.AsSpan(offset, Math.Min(65536, bytes.Length - offset)).ToArray()))).IsSuccess);
                AssertTrue((await Call(Rasters.SidecarRasterControlRoutes.Seal, new Rasters.HostRasterRef(id))).IsSuccess);
            }
            fixture.Shell.Renderer.ReducedMotion = true;
            XsrUiScene Frame() => fixture.Shell.Render(new(1000, 750));
            async Task<XsrResult> Set(long revision, params Rasters.HostRasterBinding[] bindings)
            {
                var operation = Call(Rasters.SidecarRasterControlRoutes.Replace, new Rasters.HostRasterSet(revision, bindings));
                await Until(() => operation.IsCompleted || adapter.PendingRasterControls > 0); Frame(); return await operation;
            }
            async Task<SidecarFrame> Read(SidecarMessageType type)
            { var frame = await peer.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); AssertEqual(type, frame.MessageType); return frame; }
            async Task Reply(SidecarFrame frame) => await peer.SendAsync(new(SidecarProtocol.Version, SidecarMessageType.CommandResult, SidecarFrameTraits.Final,
                frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(true, new(0, []), null)));
            Dictionary<string, string> Fields(SidecarFrame frame) => SidecarUiForms.Decode(SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span).Value.ToArray()).Fields
                .ToDictionary(field => field.Key, field => Encoding.UTF8.GetString(field.Utf8Value));
            Frame(); await Upload(1, png);
            var ordinary = new Rasters.HostRasterBinding(0, "RasterSlot", 1, 64, 64, command, "ordinary");
            AssertTrue((await Set(0, ordinary)).IsSuccess);
            AssertFalse((await Call(Rasters.SidecarRasterControlRoutes.Release, new Rasters.HostRasterRef(1))).IsSuccess);
            AssertEqual(0, adapter.ActiveRasterCallbacks);
            var open = adapter.ChangePageAsync(session, 0, 0, true, default).AsTask(); Frame(); await open;
            var scene = Frame();
            var node = scene.Nodes.Single(node => fixture.Shell.Tree.Name(node.Entity).StartsWith("SidecarRaster_", StringComparison.Ordinal));
            AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiRasterImage>(node.Entity) is not null);
            var point = new XsrUiPoint(node.Rect.X + 8, node.Rect.Y + 8);
            fixture.Shell.Renderer.PointerMoved(point);
            var input = await Read(SidecarMessageType.CommandRequest);
            var fields = Fields(input); AssertEqual("Pointer", fields["kind"]); AssertEqual("moved", fields["phase"]); AssertEqual(6, fields.Count);
            AssertFalse(fields.ContainsKey("RasterSecret"));
            for (int i = 0; i < 20; i++) fixture.Shell.Renderer.PointerMoved(new(point.X + i % 4, point.Y));
            AssertEqual(1, adapter.ActiveRasterCallbacks);
            await Reply(input); var latest = await Read(SidecarMessageType.CommandRequest);
            AssertEqual("true", Fields(latest)["gap"]); await Reply(latest); await Until(() => adapter.ActiveRasterCallbacks == 0);
            foreach (var invalid in new Rasters.HostRasterBinding[][]
            {
                [ordinary with { Document = 2 }], [ordinary with { Slot = "RasterModalSlot" }],
                [ordinary with { Slot = "RasterSecret" }], [ordinary with { Command = "host.only.command" }],
                [ordinary with { Command = "test.rasters.scalar" }], [ordinary with { Width = 63 }],
                [ordinary with { Width = double.NaN }], [ordinary with { ImageId = 99 }], [ordinary, ordinary]
            }) AssertFalse((await Set(0, invalid)).IsSuccess);
            AssertTrue(fixture.Shell.Tree.IsAlive(node.Entity));
            // Model updates retain the installed raster and its node identity.
            await session.UpdatePresentationModelsAsync(0, new Dictionary<int, IReadOnlyDictionary<string, string>> { [0] = new Dictionary<string, string>() });
            Frame(); AssertTrue(fixture.Shell.Tree.IsAlive(node.Entity));
            AssertFalse((await Set(0, ordinary)).IsSuccess);
            fixture.Shell.Renderer.PointerMoved(point); input = await Read(SidecarMessageType.CommandRequest);
            AssertTrue((await Set(1, ordinary)).IsSuccess); AssertTrue(fixture.Shell.Tree.IsAlive(node.Entity));
            await Reply(input); await Until(() => adapter.ActiveRasterCallbacks == 0);
            await Upload(2, png);
            var modal = new Rasters.HostRasterBinding(1, "RasterModalSlot", 2, 64, 64, command, "modal");
            AssertTrue((await Set(1, ordinary, modal)).IsSuccess);
            var opening = adapter.ChangeDialogAsync(session, 1, 1, true, default).AsTask(); Frame(); await opening; scene = Frame();
            var modalNode = scene.Nodes.Single(item => fixture.Shell.Tree.Name(item.Entity).StartsWith("SidecarRaster_", StringComparison.Ordinal) && item.Entity != node.Entity);
            fixture.Shell.Renderer.PointerMoved(new(modalNode.Rect.X + 8, modalNode.Rect.Y + 8)); input = await Read(SidecarMessageType.CommandRequest);
            AssertEqual("modal", SidecarUiForms.Decode(SidecarDataMessages.DecodeBinaryRequest(input.Payload.Span).Value.ToArray()).Argument);
            var closing = adapter.ChangeDialogAsync(session, 1, 1, false, default).AsTask(); Frame(); await closing;
            var cancel = await Read(SidecarMessageType.Cancel); AssertEqual(input.CorrelationId, cancel.CorrelationId); await Until(() => adapter.ActiveRasterCallbacks == 0);
            Frame(); fixture.Shell.Renderer.PointerMoved(point); input = await Read(SidecarMessageType.CommandRequest);
            await session.ReplacePresentationAsync(1, []); cancel = await Read(SidecarMessageType.Cancel); AssertEqual(input.CorrelationId, cancel.CorrelationId);
            Frame(); await Until(() => adapter.ActiveRasterCallbacks == 0); AssertFalse(fixture.Shell.Tree.IsAlive(node.Entity));
            AssertTrue((await Call(Rasters.SidecarRasterControlRoutes.Release, new Rasters.HostRasterRef(1))).IsSuccess);
            AssertTrue((await Call(Rasters.SidecarRasterControlRoutes.Release, new Rasters.HostRasterRef(2))).IsSuccess);

            // Uploads larger than a transport frame are accepted only as bounded chunks.
            byte[] large = RasterPng(1600, 1600, noise: true); AssertTrue(large.Length > 8 * 1024 * 1024);
            await Upload(3, large);
            AssertTrue((await Call(Rasters.SidecarRasterControlRoutes.Release, new Rasters.HostRasterRef(3))).IsSuccess);
            await RasterPoolChecks(session, other, png);
            var pending = adapter.RasterActions.Replace(session, new(2, []), default).AsTask(); view.Dispose();
            try { await pending; } catch (OperationCanceledException) { }
            AssertTrue(pending.IsCanceled); AssertEqual(0, adapter.PendingRasterControls); AssertEqual(0, warnings.Count);
            session.Dispose(); await receive.WaitAsync(TimeSpan.FromSeconds(5));
            Console.WriteLine("PASS: generic raster discovery, bounded chunk uploads, owned slots, atomic rejection, retained nodes, coalesced input, modal/source cancellation and resource expiry.");
        }
        static async Task Until(Func<bool> done)
        { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (!done()) await Task.Delay(1, deadline.Token); }
    }

    private static async Task RasterPoolChecks(SidecarHostSession owner, SidecarHostSession foreign, byte[] png)
    {
        long clock = 0;
        using var pool = new DesktopSidecarRasterPool(() => Interlocked.Read(ref clock));
        async Task Rejected(Func<Task> action)
        { bool rejected = false; try { await action(); } catch (Exception error) when (error is InvalidOperationException or ArgumentException or InvalidDataException) { rejected = true; } AssertTrue(rejected); }
        pool.Begin(owner, new(1, png.Length, SHA256.HashData(png)), default);
        await Rejected(() => { pool.Append(owner, new(1, 1, png), default); return Task.CompletedTask; });
        await Rejected(() => pool.SealAsync(owner, 1, default).AsTask());
        await Rejected(() => { pool.Append(foreign, new(1, 0, png), default); return Task.CompletedTask; });
        pool.Append(owner, new(1, 0, png), default); await pool.SealAsync(owner, 1, default);
        var held = pool.Acquire(owner, [1], default).Single();
        Interlocked.Add(ref clock, 31000); pool.Sweep(); AssertEqual(1, pool.Count);
        pool.Unpin(held); Interlocked.Add(ref clock, 31000); pool.Sweep(); AssertEqual(0, pool.Count);
        for (int i = 1; i <= 4; i++) pool.Begin(owner, new(i, 16 * 1024 * 1024, new byte[32]), default);
        await Rejected(() => { pool.Begin(owner, new(5, 1, new byte[32]), default); return Task.CompletedTask; });
        Interlocked.Add(ref clock, 31000); pool.Sweep(); AssertEqual(0L, pool.Bytes);
        pool.Begin(owner, new(8, png.Length, new byte[32]), default); pool.Append(owner, new(8, 0, png), default);
        await Rejected(() => pool.SealAsync(owner, 8, default).AsTask()); AssertEqual(0, pool.Count);
        byte[] invalid = png[..33];
        pool.Begin(owner, new(9, invalid.Length, SHA256.HashData(invalid)), default); pool.Append(owner, new(9, 0, invalid), default);
        await Rejected(() => pool.SealAsync(owner, 9, default).AsTask()); AssertEqual(0, pool.Count);
    }

    private static byte[] RasterPng(int width, int height, bool noise = false)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        if (noise) { byte[] bytes = RandomNumberGenerator.GetBytes(width * height * 4); System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length); }
        else { using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Blue); }
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
}
