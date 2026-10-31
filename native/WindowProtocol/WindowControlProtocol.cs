namespace Nexa.Native.Window.Wire;

internal enum WindowCommand
{
    MainCompleted = 0, Start = 1, PrepareShell = 2, WarmShell = 3, Present = 4,
    QuiesceUi = 5, ReportStage = 6, ReportFailure = 7, FailureLog = 8,
    CloseWindow = 9, Metrics = 10,
}

internal enum WindowControlCallback { BeginMain = 0, AsyncCompletion = 1, UiStopped = 2, Diagnostic = 3, Localize = 4 }
internal enum WindowUiCallback : byte { Continuation = 0, Shell = 1, Input = 2, InstallText = 3 }
internal enum WindowUiCommand : byte { Continuation = 0, Invalidate = 1, Text = 2 }

internal static class WindowControlProtocol
{
    internal static UiWireWriter Write(int kind, bool callback = false)
    {
        var writer = new UiWireWriter(new Dictionary<string, Nexa.Core.Media.PngImage>());
        writer.Int32(callback ? 0x3148574e : 0x3143574e);
        writer.Int32(1); writer.String(UiWireSchema.Fingerprint); writer.Int32(kind);
        return writer;
    }
    internal static UiWireReader Read(ReadOnlyMemory<byte> bytes, out int kind, bool callback = false)
    {
        var reader = new UiWireReader(bytes.ToArray(), new Dictionary<string, Nexa.Core.Media.PngImage>());
        try
        {
            if (reader.Int32() != (callback ? 0x3148574e : 0x3143574e) || reader.Int32() != 1
                || reader.String() != UiWireSchema.Fingerprint)
                throw new InvalidDataException("Incompatible native window control protocol.");
            kind = reader.Int32(); return reader;
        }
        catch { reader.Dispose(); throw; }
    }
    internal static byte[] Prefix(byte channel, ReadOnlyMemory<byte> body)
    {
        if (body.Length >= UiWireLimits.Frame) throw new InvalidDataException("Window channel budget exceeded.");
        byte[] bytes = new byte[body.Length + 1]; bytes[0] = channel;
        body.CopyTo(bytes.AsMemory(1)); return bytes;
    }
}
