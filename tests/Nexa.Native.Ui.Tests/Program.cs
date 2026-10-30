using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Nexa.Native.Ui;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Wire;

namespace Nexa.Native.Ui.Tests;

internal static class Program
{
    private static void Main(string[] args)
    {
        LocalOwnershipAndFaults();
        ActiveRetirementAndConcurrency();
        AsyncAdmissionAndDeliveryRetry();
        if (args.Length != 1) throw new ArgumentException("Supply the independently published native UI peer library.");
        IndependentRuntime(args[0]);
        Console.WriteLine("Native UI duplex tests passed: independent scenes/input/text, reverse calls, reentrancy, failure isolation and retirement.");
    }

    private static unsafe void LocalOwnershipAndFaults()
    {
        long failures = 0;
        var owner = new NativeUiCallRegistration(request => request.IsEmpty ? throw new IOException("private") : request.ToArray(),
            _ => { Interlocked.Increment(ref failures); throw new IOException("logging fixture"); });
        var table = owner.Table;
        var client = new NativeUiCallClient(table);
        Throws<InvalidOperationException>(() => client.Exchange(ReadOnlyMemory<byte>.Empty));
        Equal(1L, failures);
        Equal((byte)7, client.Exchange(new byte[] { 7 })[0]);
        NativeUiCallTable bad = table; bad.Version++;
        Throws<InvalidDataException>(() => _ = new NativeUiCallClient(bad));
        int length = 123;
        Equal(0L, table.Execute(table.Context, null, -1, &length)); Equal(0, length);
        byte input = 9;
        long id = table.Execute(table.Context, &input, 1, &length);
        True(id > 0); Equal(2, length);
        Task retirement = owner.DisposeAsync().AsTask();
        True(!retirement.IsCompleted);
        byte* output = stackalloc byte[2];
        Equal(-1, table.Copy(table.Context, id, output, 1));
        Equal(0, table.Copy(table.Context, id, output, 2)); Equal((byte)9, output[1]);
        Throws<InvalidOperationException>(() => client.Exchange(new byte[] { 1 }));
        table.Release(table.Context, id);
        retirement.GetAwaiter().GetResult();
        Equal(-1, table.Copy(table.Context, id, output, 2));
        table.Release(table.Context, id);

        NativeUiCallRegistration? reentrant = null;
        Task? drained = null;
        reentrant = new(_ => { drained = reentrant!.DisposeAsync().AsTask(); True(!drained.IsCompleted); return [8]; });
        Equal((byte)8, new NativeUiCallClient(reentrant.Table).Exchange(new byte[] { 0 })[0]);
        True(drained!.IsCompletedSuccessfully);
        int calls = 0;
        var bounded = new NativeUiCallRegistration(_ => { calls++; return []; });
        var boundedTable = bounded.Table;
        long[] results = new long[128];
        for (int i = 0; i < results.Length; i++)
        { results[i] = boundedTable.Execute(boundedTable.Context, null, 0, &length); True(results[i] > 0); }
        Throws<NativeUiCallBusyException>(() => new NativeUiCallClient(boundedTable).Exchange(ReadOnlyMemory<byte>.Empty));
        Equal(128, calls);
        foreach (long result in results) boundedTable.Release(boundedTable.Context, result);
        new NativeUiCallClient(boundedTable).Exchange(ReadOnlyMemory<byte>.Empty);
        Equal(129, calls);
        bounded.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Console.WriteLine("PASS: native call results survive retirement; malformed tables, logging failures and reentrant close are contained");
    }

    private static void ActiveRetirementAndConcurrency()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var owner = new NativeUiCallRegistration(request =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
            return request.ToArray();
        });
        var client = new NativeUiCallClient(owner.Table);
        Task<byte[]> active = Task.Run(() => client.Exchange(new byte[] { 3 }));
        True(entered.Wait(TimeSpan.FromSeconds(30)));
        Task retired = owner.DisposeAsync().AsTask();
        True(!retired.IsCompleted);
        Throws<InvalidOperationException>(() => client.Exchange(new byte[] { 4 }));
        release.Set();
        Equal((byte)3, active.GetAwaiter().GetResult()[0]);
        retired.GetAwaiter().GetResult();

        var concurrent = new NativeUiCallRegistration(request => request.ToArray());
        var parallelClient = new NativeUiCallClient(concurrent.Table);
        Parallel.For(0, 4, index =>
        {
            for (int i = 0; i < 100; i++)
                Equal((byte)index, parallelClient.Exchange(new byte[] { (byte)index })[0]);
        });
        concurrent.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Console.WriteLine("PASS: retirement drains active calls and concurrent results retain their own bytes");
    }

    private static void AsyncAdmissionAndDeliveryRetry()
    {
        NativeUiAsyncClient? client = null;
        int executions = 0, starts = 0, deliveries = 0;
        var server = new NativeUiAsyncServer((request, _) =>
        {
            Interlocked.Increment(ref executions); return Task.FromResult(request.ToArray());
        }, message =>
        {
            if (Interlocked.Increment(ref deliveries) <= 3) throw new NativeUiCallBusyException();
            return client!.Complete(message);
        });
        client = new(message =>
        {
            if (NativeUiAsyncProtocol.Read(message).Kind == NativeUiAsyncProtocol.Start
                && Interlocked.Increment(ref starts) <= 3) throw new NativeUiCallBusyException();
            return server.Exchange(message);
        });
        Equal((byte)41, client.InvokeAsync(new byte[] { 41 }).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()[0]);
        Equal(1, executions);
        Equal(4, starts); Equal(4, deliveries);
        client.Dispose(); server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Console.WriteLine("PASS: async admission and completion backpressure retry without repeating the admitted operation");
    }

    private static unsafe void IndependentRuntime(string path)
    {
        // NativeAOT shared runtimes remain loaded for process lifetime; never unload live code tables.
        nint library = NativeLibrary.Load(Path.GetFullPath(path));
        var open = (delegate* unmanaged[Cdecl]<NativeUiCallTable*, NativeUiCallTable*, int>)NativeLibrary.GetExport(library, "nexa_ui_probe_open_v1");
        var close = (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(library, "nexa_ui_probe_close_v1");
        NativeUiCallClient? peer = null;
        var context = new SynchronizationContext();
        var text = new TextProvider(context);
        NativeUiAsyncClient? operations = null;
        var textEndpoint = new XsrUiTextLayoutEndpoint(text);
        int invalidations = 0;
        var host = new NativeUiCallRegistration(request =>
        {
            if (request.Span[0] == 253) return operations!.Complete(request[1..]);
            if (request.Span[0] == 254)
            { Interlocked.Or(ref invalidations, request.Span[1]); return []; }
            return request.Span[0] == 255 ? peer!.Exchange(new byte[] { 5 }) : textEndpoint.Exchange(request);
        }, callbackContext: context);
        var hostTable = host.Table;
        NativeUiCallTable peerTable = default;
        Equal(0, open(&hostTable, &peerTable));
        peer = new(peerTable);
        operations = new(new InputTransport(peer, 7).Exchange);
        Equal((byte)37, operations.InvokeAsync(new byte[] { 0, 37 }).GetAwaiter().GetResult()[0]);
        Equal((byte)38, operations.InvokeAsync(new byte[] { 1, 38 }).GetAwaiter().GetResult()[0]);
        using (var cancellation = new CancellationTokenSource())
        {
            Task<byte[]> canceled = operations.InvokeAsync(new byte[] { 2 }, cancellation.Token);
            cancellation.Cancel();
            Throws<OperationCanceledException>(() => canceled.GetAwaiter().GetResult());
        }
        Throws<InvalidOperationException>(() => operations.InvokeAsync(new byte[] { 3 }).GetAwaiter().GetResult());
        var receiver = new XsrUiSceneReceiver();
        XsrUiScene scene = receiver.Accept(peer.Exchange(new byte[] { 0 }));
        True(scene.Nodes.Any(node => node.Text == "native runtime text"));
        True(text.Calls > 0);
        True(SynchronizationContext.Current is null);
        byte[] acknowledgement = new byte[9]; acknowledgement[0] = 1;
        BinaryPrimitives.WriteInt64LittleEndian(acknowledgement.AsSpan(1), receiver.Sequence);
        Equal(0, peer.Exchange(acknowledgement).Length);
        var remote = new XsrUiRemoteRenderer(new InputTransport(peer, 2), _ => throw new NotSupportedException());
        remote.SetTransitionOffset(scene[0].Entity, 22);
        Equal(22d, remote.GetTransitionOffset(scene[0].Entity));
        Equal(1L, BinaryPrimitives.ReadInt64LittleEndian(peer.Exchange(new byte[] { 3, 255 })));
        try { peer.Exchange(new byte[] { 4 }); throw new InvalidDataException("Expected remote failure."); }
        catch (InvalidOperationException error) { True(!error.Message.Contains("private", StringComparison.Ordinal)); }
        Equal(2L, BinaryPrimitives.ReadInt64LittleEndian(peer.Exchange(new byte[] { 5 })));
        Equal(22d, remote.GetTransitionOffset(scene[0].Entity));

        remote.SetTransitionOffset(scene[0].Entity, 0);
        var shell = new XsrUiRemoteShell(new InputTransport(peer, 6), remote);
        Equal("NexaCL", shell.Title);
        shell.PublishWindowMetrics(86, false, 8);
        var projected = shell.Render(new(850, 500));
        Equal(834d, projected.Nodes.First(node => node.Role == XsrUiSemanticRole.TitleBar).Rect.Width);
        shell.PublishWindowMetrics(86, true, 8);
        projected = shell.Render(new(1040, 680));
        Equal(1040d, projected.Nodes.First(node => node.Role == XsrUiSemanticRole.TitleBar).Rect.Width);
        True(invalidations != 0);
        shell.NotifyInvalidated((XsrUiShellInvalidation)Interlocked.Exchange(ref invalidations, 0));

        Task<byte[]> pendingAsync = operations.InvokeAsync(new byte[] { 2 });
        operations.Dispose();
        Throws<OperationCanceledException>(() => pendingAsync.GetAwaiter().GetResult());
        int length = 0;
        byte operation = 5;
        long pending = peerTable.Execute(peerTable.Context, &operation, 1, &length);
        True(pending > 0);
        Equal(0, close());
        Throws<InvalidOperationException>(() => peer.Exchange(new byte[] { 5 }));
        byte[] bytes = new byte[length];
        fixed (byte* output = bytes) Equal(0, peerTable.Copy(peerTable.Context, pending, output, length));
        Equal((byte)0, bytes[0]);
        peerTable.Release(peerTable.Context, pending);
        bool closed = false;
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            int state = close(); True(state >= 0);
            if (state == 1) { closed = true; break; }
            Thread.Yield();
        }
        True(closed);
        host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Console.WriteLine("PASS: independent native runtime renders scenes using reverse text calls; generated input and reentrant callbacks preserve ownership");
    }

    private sealed class InputTransport(NativeUiCallClient peer, byte operation) : IXsrUiWireTransport
    {
        public byte[] Exchange(ReadOnlyMemory<byte> request)
        {
            byte[] message = new byte[request.Length + 1]; message[0] = operation;
            request.CopyTo(message.AsMemory(1)); return peer.Exchange(message);
        }
    }
    private sealed class TextProvider(SynchronizationContext expected) : IXsrUiTextLayoutProvider
    {
        internal int Calls;
        public long Revision => 1;
        public XsrUiTextLayoutMetrics Measure(XsrUiTextLayoutRequest request)
        { True(ReferenceEquals(expected, SynchronizationContext.Current)); Calls++; return new(new(request.Text.Length * 8, 20)); }
    }
    private static void True(bool value) { if (!value) throw new InvalidOperationException("Assertion failed."); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, received {actual}."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
