using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Nexa.Native.Ui;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Wire;
using Nexa.Xsr.State;

namespace Nexa.Native.Ui.Peer;

internal static unsafe class Exports
{
    private static NativeUiCallRegistration? _registration;
    private static Task? _retirement;
    private static Peer? _peer;

    [UnmanagedCallersOnly(EntryPoint = "nexa_ui_probe_open_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Open(NativeUiCallTable* host, NativeUiCallTable* output)
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
            _retirement ??= _registration.DisposeAsync().AsTask();
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
        private readonly XsrUiSceneSender _scenes = new();
        private XsrUiScenePacket? _pending;
        private long _failures;
        internal Peer(NativeUiCallClient host)
        {
            _host = host;
            var tree = new XsrUiTree();
            var root = tree.Create("native-ui-probe");
            tree.SetComponent(root, new XsrUiElement { Width = 320, Height = 180 });
            tree.SetComponent(root, new XsrUiText("native runtime text"));
            tree.SetComponent(root, new XsrUiTransition());
            _renderer = new(tree, new XsrStateStoreBuilder().Build())
            {
                TextLayoutProvider = new XsrUiRemoteTextLayoutProvider(new HostTransport(host)),
            };
            _renderer.SetRoot(root); _input = new(_renderer);
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
                default: throw new InvalidDataException("Unknown probe operation.");
            }
        }
        public void Dispose() { _pending?.Dispose(); _scenes.Dispose(); }
    }
    private sealed class HostTransport(NativeUiCallClient client) : IXsrUiWireTransport
    {
        public byte[] Exchange(ReadOnlyMemory<byte> request) => client.Exchange(request);
    }
}
