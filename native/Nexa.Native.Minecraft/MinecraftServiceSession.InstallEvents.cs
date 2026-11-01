using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private readonly System.Threading.Channels.Channel<InstallEvent> _installEvents = System.Threading.Channels.Channel.CreateBounded<InstallEvent>(128);
    private readonly object _installEventGate = new();
    private Task? _installEventPump;
    private long _installEventPosted, _installEventApplied;
    private Exception? _installEventFailure;
    private readonly List<(long Sequence, TaskCompletionSource Completion)> _installEventWaiters = [];
    private sealed record InstallEvent(long Sequence, string Root, string? Previous, string? Current);
    private void PublishInstallEvent(string root, string? previous = null, string? current = null)
    {
        lock (_installEventGate)
        {
            if (_installEventFailure is not null) throw new IOException("Installation notification failed.", _installEventFailure);
            long sequence = checked(_installEventPosted + 1);
            if (!_installEvents.Writer.TryWrite(new(sequence, root, previous, current))) throw new IOException("Installation notification budget exceeded.");
            _installEventPosted = sequence;
        }
    }
    private async Task PumpInstallEventsAsync()
    {
        try
        {
            await foreach (var item in _installEvents.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (!Volatile.Read(ref _closing) && item.Previous is { } previous && item.Current is { } current && _library is { } library)
                {
                    var remembered = await library.RememberRenamedAsync(item.Root, previous, current, CancellationToken.None).ConfigureAwait(false);
                    if (!remembered.IsSuccess) Console.Error.WriteLine("Minecraft library rename reconciliation failed: " + remembered.Error!.Code.Value);
                }
                using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.InstallationEvent);
                request.Int64(item.Sequence); request.String(item.Root); request.String(item.Previous); request.String(item.Current);
                byte[] bytes = await _endpoint.InvokeAsync(request.Finish()).ConfigureAwait(false);
                using var reader = new ServiceWireReader(bytes); reader.End();
                lock (_installEventGate)
                {
                    _installEventApplied = item.Sequence;
                    for (int i = _installEventWaiters.Count - 1; i >= 0; i--)
                        if (_installEventWaiters[i].Sequence <= _installEventApplied)
                        { _installEventWaiters[i].Completion.TrySetResult(); _installEventWaiters.RemoveAt(i); }
                }
            }
        }
        catch (Exception error)
        {
            lock (_installEventGate)
            {
                _installEventFailure = error;
                foreach (var waiter in _installEventWaiters) waiter.Completion.TrySetException(error);
                _installEventWaiters.Clear();
            }
        }
    }
    private Task FlushInstallEventsAsync()
    {
        lock (_installEventGate)
        {
            if (_installEventFailure is not null) return Task.FromException(new IOException("Installation notification failed.", _installEventFailure));
            if (_installEventApplied >= _installEventPosted) return Task.CompletedTask;
            if (_installEventWaiters.Count >= 128) throw new IOException("Installation notification waiter budget exceeded.");
            TaskCompletionSource source = new(TaskCreationOptions.RunContinuationsAsynchronously); _installEventWaiters.Add((_installEventPosted, source)); return source.Task;
        }
    }
}
