using Nexa.Native.Ui;
using Nexa.Native.Window.Wire;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Native.Window;

internal sealed partial class WindowSession
{
    private readonly NativeUiResources<IDisposable> _effects = new(16, 0);

    private byte[] ExecuteEffect(UiWireReader reader, CancellationToken token)
    {
        var operation = (WindowEffectOperation)reader.Int32();
        switch (operation)
        {
            case WindowEffectOperation.CropScreenshot:
                byte[] input = reader.Buffer();
                int x = reader.Int32(), y = reader.Int32(), width = reader.Int32(), height = reader.Int32(); reader.End();
                token.ThrowIfCancellationRequested();
                byte[] cropped = AvaloniaUiScreenshotCodec.Crop(input, x, y, width, height);
                token.ThrowIfCancellationRequested();
                using (var writer = WindowControlProtocol.Write((int)WindowCommand.Effect))
                { writer.Buffer(cropped); return writer.Finish(); }
            case WindowEffectOperation.AcquireWorldLock:
                string path = Required(reader.String()); reader.End(); token.ThrowIfCancellationRequested();
                var lease = AvaloniaUiWorldEditLease.Acquire(path);
                long handle = 0;
                try { handle = _effects.Register(lease, 0); return WindowEffectResult.Encode(handle); }
                catch { if (handle == 0) lease.Dispose(); else _effects.RejectProvisional(handle); throw; }
            default: throw new InvalidDataException("Unknown native worker effect.");
        }
    }
}
