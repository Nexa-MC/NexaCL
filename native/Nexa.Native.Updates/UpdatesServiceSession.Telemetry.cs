using Nexa.Native.Services.Wire;
using Nexa.Native.Updates.Services;

namespace Nexa.Native.Updates;

internal sealed partial class UpdatesServiceSession
{
    private readonly System.Threading.Channels.Channel<(long Sequence, string Name, string Outcome)> _telemetry = System.Threading.Channels.Channel.CreateBounded<(long, string, string)>(128);
    private readonly object _telemetryGate = new();
    private readonly Task _telemetryPump;
    private long _telemetryPosted, _telemetryApplied;
    private Exception? _telemetryFailure;
    private readonly List<(long Sequence, TaskCompletionSource Completion)> _telemetryWaiters = [];
    private void Record(string name, string outcome)
    {
        lock (_telemetryGate)
        {
            if (_telemetryFailure is not null) throw new IOException("Update telemetry failed.", _telemetryFailure);
            long sequence = checked(_telemetryPosted + 1);
            if (!_telemetry.Writer.TryWrite((sequence, name, outcome))) throw new IOException("Update telemetry budget exceeded.");
            _telemetryPosted = sequence;
        }
    }
    private async Task PumpTelemetryAsync()
    {
        try
        {
            await foreach (var item in _telemetry.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                using var writer = UpdatesServiceProtocol.Request(UpdatesServiceProtocol.Telemetry); writer.Int64(item.Sequence); writer.String(item.Name); writer.String(item.Outcome);
                byte[] bytes = await _endpoint.InvokeAsync(writer.Finish()).ConfigureAwait(false); using var reader = new ServiceWireReader(bytes); reader.End();
                lock (_telemetryGate)
                {
                    _telemetryApplied = item.Sequence;
                    for (int i = _telemetryWaiters.Count - 1; i >= 0; i--)
                        if (_telemetryWaiters[i].Sequence <= _telemetryApplied) { _telemetryWaiters[i].Completion.TrySetResult(); _telemetryWaiters.RemoveAt(i); }
                }
            }
        }
        catch (Exception error)
        {
            lock (_telemetryGate) { _telemetryFailure = error; foreach (var waiter in _telemetryWaiters) waiter.Completion.TrySetException(error); _telemetryWaiters.Clear(); }
        }
    }
    private Task FlushTelemetryAsync()
    {
        lock (_telemetryGate)
        {
            if (_telemetryFailure is { } error) return Task.FromException(new IOException("Update telemetry failed.", error));
            if (_telemetryApplied >= _telemetryPosted) return Task.CompletedTask;
            if (_telemetryWaiters.Count >= 128) throw new IOException("Update telemetry waiter budget exceeded.");
            TaskCompletionSource source = new(TaskCreationOptions.RunContinuationsAsynchronously); _telemetryWaiters.Add((_telemetryPosted, source)); return source.Task;
        }
    }
}
