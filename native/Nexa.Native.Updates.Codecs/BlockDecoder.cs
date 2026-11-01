using System.IO.Compression;

namespace Nexa.Native.Updates;

public static class BlockDecoder
{
    public static Stream Open(Stream input, int codec, bool leaveOpen) => codec switch
    {
        1 => new GZipStream(input, CompressionMode.Decompress, leaveOpen),
        2 => new ZstdSharp.DecompressionStream(input, leaveOpen: leaveOpen),
        _ => throw new InvalidDataException("Unsupported block codec.")
    };
}
