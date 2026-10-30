using System.Buffers.Binary;
using System.Collections.Concurrent;
using Nexa.Native.Ui;

namespace Nexa.Native.Ui.Peer;

/// <summary>Independent runtime thread standing in for the native UI dispatcher.</summary>
internal sealed class DispatcherFixture : IAsyncDisposable
{
    private readonly BlockingCollection<long> _queue = new(4096);
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly NativeUiCallClient _host;
    private readonly Action<Exception> _failure;
    internal DispatcherFixture(NativeUiCallClient host, Action<Exception> failure)
    {
        _host = host; _failure = failure;
        new Thread(Run) { Name = "Native UI contract dispatcher", IsBackground = true }.Start();
    }
    internal void Post(long id)
    {
        if (id <= 0 || !_queue.TryAdd(id)) throw new InvalidOperationException("Dispatcher admission failed.");
    }
    private void Run()
    {
        try
        {
            foreach (long id in _queue.GetConsumingEnumerable())
            {
                byte[] message = new byte[9]; message[0] = 251;
                BinaryPrimitives.WriteInt64LittleEndian(message.AsSpan(1), id);
                try { _host.Exchange(message); }
                catch (Exception error) { _failure(error); }
            }
            _completion.TrySetResult();
        }
        catch (Exception error) { _completion.TrySetException(error); }
    }
    public async ValueTask DisposeAsync()
    {
        _queue.CompleteAdding();
        await _completion.Task.ConfigureAwait(false);
        _queue.Dispose();
    }
}
