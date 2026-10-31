using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Native.Window.Wire;

internal sealed record WindowStartupFacts(AvaloniaUiStartupAppearance Appearance,
    bool HardwareAccelerationDisabled, string Stage, long? FirstRenderTicks, long? ShellReadyTicks)
{
    internal static byte[] Encode(WindowStartupFacts value)
    {
        using var writer = WindowControlProtocol.Write((int)WindowCommand.StartupFacts);
        writer.Buffer(WindowValueChannel.EncodeStartup(value.Appearance));
        writer.Boolean(value.HardwareAccelerationDisabled); writer.String(value.Stage);
        WriteTicks(writer, value.FirstRenderTicks); WriteTicks(writer, value.ShellReadyTicks);
        return writer.Finish();
    }
    internal static WindowStartupFacts Decode(ReadOnlyMemory<byte> bytes)
    {
        using var reader = WindowControlProtocol.Read(bytes, out int command);
        if (command != (int)WindowCommand.StartupFacts) throw new InvalidDataException("Invalid startup snapshot.");
        var result = new WindowStartupFacts(WindowValueChannel.DecodeStartup(reader.Buffer()), reader.Boolean(),
            reader.String() ?? throw new InvalidDataException("Missing startup stage."), ReadTicks(reader), ReadTicks(reader));
        reader.End(); return result;
    }
    private static void WriteTicks(UiWireWriter writer, long? ticks)
    {
        if (ticks < 0) throw new InvalidDataException("Invalid startup duration.");
        writer.Boolean(ticks.HasValue); if (ticks.HasValue) writer.Int64(ticks.Value);
    }
    private static long? ReadTicks(UiWireReader reader)
    {
        if (!reader.Boolean()) return null;
        long ticks = reader.Int64();
        return ticks >= 0 ? ticks : throw new InvalidDataException("Invalid startup duration.");
    }
}
