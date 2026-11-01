using Nexa.Native.Ui;

namespace Nexa.Native.Services;

// Both runtimes compile this machinery locally. The only shared representation is a C call
// table and owned, bounded bytes. Admission is synchronous; service work always runs on workers.
internal sealed class ServiceEndpoint : IAsyncDisposable
{
    private NativeUiCallClient? _peer;
    private readonly NativeUiCallRegistration _registration;
    private readonly NativeUiAsyncClient _client;
    private readonly NativeUiAsyncServer _server;
    private readonly Func<ReadOnlyMemory<byte>, byte[]> _publication;

    internal ServiceEndpoint(Func<ReadOnlyMemory<byte>, CancellationToken, Task<byte[]>> execute,
        Func<ReadOnlyMemory<byte>, byte[]> publication, Action<Exception> failure,
        Action<long, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>? rejectedResult = null)
    {
        _publication = publication;
        _client = new(bytes => Send(0, bytes));
        _server = new((bytes, token) => Task.Run(() => execute(bytes, token), token), bytes => Send(1, bytes), failure, rejectedResult);
        _registration = new(Receive, failure);
    }

    internal NativeUiCallTable Table => _registration.Table;
    internal void Connect(NativeUiCallTable peer) => _peer = new(peer);
    internal Task<byte[]> InvokeAsync(ReadOnlyMemory<byte> request, CancellationToken token = default) => _client.InvokeAsync(request, token);
    internal ValueTask StopAsync() => _server.DisposeAsync();
    internal byte[] Publish(ReadOnlyMemory<byte> publication) => Send(2, publication);
    private byte[] Send(byte channel, ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > Wire.ServiceWireLimits.Frame) throw new InvalidDataException("Service envelope budget exceeded.");
        byte[] envelope = new byte[bytes.Length + 1];
        envelope[0] = channel; bytes.CopyTo(envelope.AsMemory(1));
        return (_peer ?? throw new InvalidOperationException("Service peer is not connected.")).Exchange(envelope);
    }
    private byte[] Receive(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 1 or > Wire.ServiceWireLimits.Frame + 1) throw new InvalidDataException("Invalid service envelope.");
        return bytes.Span[0] switch
        {
            0 => _server.Exchange(bytes[1..]),
            1 => _client.Complete(bytes[1..]),
            2 => _publication(bytes[1..]),
            _ => throw new InvalidDataException("Unknown service envelope channel."),
        };
    }
    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync().ConfigureAwait(false);
        await _client.RetireAsync().ConfigureAwait(false);
        await _registration.DisposeAsync().ConfigureAwait(false);
    }
}
