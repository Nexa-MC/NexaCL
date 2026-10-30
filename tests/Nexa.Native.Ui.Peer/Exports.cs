using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Nexa.Native.Ui;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Wire;
using Nexa.Xsr.State;

namespace Nexa.Native.Ui.Peer;

internal static class Exports
{
    private static NativeUiCallRegistration? _registration;
    private static Task? _retirement;
    private static Peer? _peer;

    [UnmanagedCallersOnly(EntryPoint = "nexa_ui_probe_open_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int Open(NativeUiCallTable* host, NativeUiCallTable* output)
    {
        try
        {
            if (host == null || output == null || _registration is not null) return -1;
            _peer = new Peer(new NativeUiCallClient(*host));
            _registration = new(_peer.Exchange, _peer.Failed);
            *output = _registration.Table;
            return 0;
        }
        catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_ui_probe_close_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Close()
    {
        try
        {
            if (_registration is null) return -1;
            _retirement ??= Task.WhenAll(_registration.DisposeAsync().AsTask(), _peer!.StopAsync());
            if (!_retirement.IsCompletedSuccessfully) return 0;
            _peer?.Dispose();
            return 1;
        }
        catch { return -1; }
    }

    private sealed class Peer : IDisposable
    {
        private readonly NativeUiCallClient _host;
        private readonly XsrUiRenderer _renderer;
        private readonly XsrUiRendererEndpoint _input;
        private readonly XsrUiShellEndpoint _shell;
        private readonly NativeUiAsyncServer _async;
        private readonly NativeUiResources<MemoryStream> _resources = new(8, 1024);
        private readonly XsrUiSceneSender _scenes = new();
        private XsrUiScenePacket? _pending;
        private long _failures;
        internal Peer(NativeUiCallClient host)
        {
            _host = host;
            var builder = new XsrStateStoreBuilder();
            XsrUiShellWindowState.Declare(builder);
            var shell = new XsrUiShell(builder.Build());
            shell.Tree.SetComponent(shell.Content, new XsrUiText("native runtime text"));
            shell.Tree.SetComponent(shell.Root, new XsrUiTransition());
            _renderer = shell.Renderer;
            _renderer.TextLayoutProvider = new XsrUiRemoteTextLayoutProvider(new HostTransport(host));
            _input = new(_renderer);
            _shell = new(shell);
            _shell.Invalidated += kind => _host.Exchange(new byte[] { 254, (byte)kind });
            _async = new(ExecuteAsync, completion =>
            {
                byte[] bytes = new byte[completion.Length + 1]; bytes[0] = 253;
                completion.CopyTo(bytes.AsMemory(1)); return _host.Exchange(bytes);
            }, Failed, (_, request, result) =>
            {
                if (request.Span[0] == 4) _resources.RejectProvisional(BinaryPrimitives.ReadInt64LittleEndian(result.Span));
            });
        }
        internal void Failed(Exception _) => Interlocked.Increment(ref _failures);
        internal byte[] Exchange(ReadOnlyMemory<byte> request)
        {
            if (request.IsEmpty) throw new InvalidDataException("Missing probe operation.");
            switch (request.Span[0])
            {
                case 0:
                    if (request.Length != 1) throw new InvalidDataException("Unexpected scene arguments.");
                    _pending?.Dispose();
                    _pending = _scenes.Prepare(_renderer.Render());
                    return _pending.Bytes.ToArray();
                case 1:
                    if (request.Length != 9) throw new InvalidDataException("Invalid scene acknowledgement.");
                    (_pending ?? throw new InvalidDataException("No pending scene.")).Acknowledge(BinaryPrimitives.ReadInt64LittleEndian(request.Span[1..]));
                    _pending.Dispose(); _pending = null;
                    return [];
                case 2: return _input.Exchange(request[1..]);
                case 3: return _host.Exchange(request[1..]);
                case 4: throw new InvalidOperationException("private fixture text must not cross the ABI");
                case 5: return BitConverter.GetBytes(Interlocked.Read(ref _failures));
                case 6: return _shell.Exchange(request[1..]);
                case 7: return _async.Exchange(request[1..]);
                case 8:
                    if (request.Length != 10) throw new InvalidDataException("Invalid resource operation.");
                    long resource = BinaryPrimitives.ReadInt64LittleEndian(request.Span[2..]);
                    switch (request.Span[1])
                    {
                        case 0: return [_resources.Claim(resource) ? (byte)1 : (byte)0];
                        case 1:
                            using (var borrow = _resources.Acquire(resource)) return borrow.Resource.ToArray();
                        case 2: _resources.Release(resource); return [];
                        default: throw new InvalidDataException("Unknown resource operation.");
                    }
                default: throw new InvalidDataException("Unknown probe operation.");
            }
        }
        internal async Task StopAsync()
        {
            await _async.DisposeAsync().ConfigureAwait(false);
            await _resources.DisposeAsync().ConfigureAwait(false);
        }
        private Task<byte[]> ExecuteAsync(ReadOnlyMemory<byte> request, CancellationToken token)
        {
            if (request.IsEmpty) throw new InvalidDataException("Missing async probe operation.");
            return request.Span[0] switch
            {
                0 => Task.FromResult(request[1..].ToArray()),
                1 => DelayedAsync(request[1..], token),
                2 => CanceledAsync(token),
                4 => Task.FromResult(CreateResource(request[1..])),
                _ => throw new InvalidOperationException("private async fixture error"),
            };
        }
        private byte[] CreateResource(ReadOnlyMemory<byte> request)
        {
            var resource = new MemoryStream(request.ToArray(), writable: false);
            try { return BitConverter.GetBytes(_resources.Register(resource, request.Length)); }
            catch { resource.Dispose(); throw; }
        }
        private static async Task<byte[]> DelayedAsync(ReadOnlyMemory<byte> request, CancellationToken token)
        {
            await Task.Delay(20, token).ConfigureAwait(false); return request.ToArray();
        }
        private static async Task<byte[]> CanceledAsync(CancellationToken token)
        {
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); return [];
        }
        public void Dispose() { _shell.Dispose(); _pending?.Dispose(); _scenes.Dispose(); }
    }
    private sealed class HostTransport(NativeUiCallClient client) : IXsrUiWireTransport
    {
        public byte[] Exchange(ReadOnlyMemory<byte> request) => client.Exchange(request);
    }
}
