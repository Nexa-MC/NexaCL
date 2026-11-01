using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Native.Window.Wire;

internal enum WindowMediaControlsOperation { Create = 0, Start = 1, Update = 2, Release = 3 }
internal sealed record WindowMediaControlsSnapshot(AvaloniaUiMediaState State, double Volume, bool Enabled)
{
    internal void Write(UiWireWriter writer)
    { writer.Buffer(WindowValueChannel.EncodeMediaState(State)); writer.Double(Volume); writer.Boolean(Enabled); }
    internal static WindowMediaControlsSnapshot Read(UiWireReader reader)
    {
        var state = WindowValueChannel.DecodeMediaState(reader.Buffer()); double volume = reader.Double(); bool enabled = reader.Boolean();
        if (!double.IsFinite(volume) || volume is < 0 or > 1) throw new InvalidDataException("Invalid system media volume.");
        return new(state, volume, enabled);
    }
}
