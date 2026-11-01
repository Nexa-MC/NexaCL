using System.Threading.Channels;
using Nexa.Services.Logging;

namespace Nexa.Native.Network.Services;

internal readonly record struct NativeLogMessage(LogLevel Level, string Module, string Message,
    string? Exception, bool Operation = false, TimeSpan Duration = default, bool Succeeded = false);

internal sealed class NativeLogPipe : IAsyncDisposable
{
    private readonly Channel<NativeLogMessage> _messages;
    private readonly Task _worker;
    private long _dropped;
    internal NativeLogPipe(Func<NativeLogMessage, Task> send)
    {
        _messages = Channel.CreateBounded<NativeLogMessage>(new BoundedChannelOptions(512)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest }, _ => Interlocked.Increment(ref _dropped));
        _worker = Task.Run(async () =>
        {
            await foreach (var message in _messages.Reader.ReadAllAsync().ConfigureAwait(false)) await send(message).ConfigureAwait(false);
            long dropped = Interlocked.Read(ref _dropped);
            if (dropped != 0) await send(new(LogLevel.Warn, "Download", $"Native log queue dropped {dropped} older records.", null)).ConfigureAwait(false);
        });
    }
    internal void Post(NativeLogMessage message) => _messages.Writer.TryWrite(message);
    public async ValueTask DisposeAsync() { _messages.Writer.TryComplete(); await _worker.ConfigureAwait(false); }
}
