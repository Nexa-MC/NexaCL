namespace Nexa.Services.Updates;

/// <summary>RFC 3284 block decoder; failed deltas fall back to full blocks.</summary>
public static class UpdateVcdiff
{
    public const string Algorithm = "vcdiff-rfc3284";
    public static bool TryDecode(ReadOnlySpan<byte> delta, ReadOnlySpan<byte> source, out byte[] target)
    {
        try { target = Decode(delta, source); return true; }
        catch (Exception failure) when (failure is InvalidDataException or ArgumentOutOfRangeException or OverflowException)
        { target = []; return false; }
    }

    public static byte[] Decode(ReadOnlySpan<byte> delta, ReadOnlySpan<byte> source)
    {
#if NEXA_EXTERNAL_MODULES
        return NativeUpdateEngine.Decode(delta, source);
#else
        return Nexa.Native.Updates.VcdiffDecoder.Decode(delta, source);
#endif
    }
}
