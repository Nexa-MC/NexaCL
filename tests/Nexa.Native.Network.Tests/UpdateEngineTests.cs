using System.IO.Compression;
using Nexa.Services.Updates;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async Task NativeUpdateCodecsWork()
    {
        byte[] raw = new byte[1024 * 1024 + 19];
        new Random(42).NextBytes(raw);
        foreach (string codec in new[] { "gzip", "zstd" })
        {
            using MemoryStream compressed = new();
            using (Stream encoder = codec == "gzip"
                       ? new GZipStream(compressed, CompressionLevel.Fastest, true)
                       : new ZstdSharp.CompressionStream(compressed, leaveOpen: true))
                encoder.Write(raw);
            using (Stream synchronous = UpdateBlockCodec.OpenDecompressor(new MemoryStream(compressed.ToArray()), codec))
            {
                using MemoryStream copy = new();
                synchronous.CopyTo(copy, 4093);
                AssertEqual(true, raw.AsSpan().SequenceEqual(copy.ToArray()));
            }
            using AsyncOnlyInput input = new(compressed.ToArray());
            using Stream decoder = UpdateBlockCodec.OpenDecompressor(input, codec, true);
            using MemoryStream output = new();
            AssertEqual(0, input.BytesRead);
            byte[] first = new byte[997];
            int firstCount = await decoder.ReadAsync(first);
            AssertEqual(true, firstCount > 0 && input.BytesRead < compressed.Length);
            output.Write(first, 0, firstCount);
            await decoder.CopyToAsync(output, 997);
            AssertEqual(true, raw.AsSpan().SequenceEqual(output.ToArray()));
            AssertEqual(0, await decoder.ReadAsync(new byte[3]));
            decoder.Dispose();
            AssertEqual(true, input.CanRead);
        }
        IOException network = new("fixture TLS interruption");
        using (Stream decoder = UpdateBlockCodec.OpenDecompressor(new FailingInput(network), "gzip"))
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try { _ = await decoder.ReadAsync(new byte[10]); throw new InvalidOperationException("Missing input failure."); }
                catch (IOException e) { AssertEqual(true, ReferenceEquals(network, e)); }
            }
        }
        using (var input = new WaitingInput())
        using (Stream decoder = UpdateBlockCodec.OpenDecompressor(input, "gzip", true))
        {
            using var cancellation = new CancellationTokenSource();
            Task<int> read = decoder.ReadAsync(new byte[10], cancellation.Token).AsTask();
            await input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            try { await read; throw new InvalidOperationException("Missing cancellation."); }
            catch (OperationCanceledException) { }
            Task<int> next = decoder.ReadAsync(new byte[10]).AsTask();
            decoder.Dispose();
            try { await next.WaitAsync(TimeSpan.FromSeconds(5)); throw new InvalidOperationException("Missing dispose cancellation."); }
            catch (OperationCanceledException) { }
        }
        WeakReference abandoned = BeginAbandonedDecode();
        for (int attempt = 0; attempt < 20 && abandoned.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(10);
        }
        AssertEqual(false, abandoned.IsAlive);
        byte[] delta = [0xD6, 0xC3, 0xC4, 0, 0, 10, 3, 0, 3, 2, 0, 65, 66, 67, 1, 3];
        AssertEqual("ABC", System.Text.Encoding.ASCII.GetString(UpdateVcdiff.Decode(delta, [])));
        AssertEqual(false, UpdateVcdiff.TryDecode([0, 1], [], out _));
        Console.WriteLine("Native update caller: gzip/zstd, fragmented async input, network exception identity, cancellation, disposal and delta passed.");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference BeginAbandonedDecode()
    {
        Stream decoder = UpdateBlockCodec.OpenDecompressor(new WaitingInput(), "gzip");
        _ = decoder.ReadAsync(new byte[10]).AsTask();
        return new WeakReference(decoder);
    }

    private sealed class AsyncOnlyInput(byte[] data) : MemoryStream
    {
        private int _offset;
        internal int BytesRead => _offset;
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous network read.");
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(Math.Min(buffer.Length, 113), data.Length - _offset);
            data.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }
    }
    private sealed class FailingInput(Exception failure) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(failure);
    }
    private sealed class WaitingInput : MemoryStream
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
