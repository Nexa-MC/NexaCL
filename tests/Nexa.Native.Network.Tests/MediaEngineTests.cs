using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Nexa.Core.Media;
using Nexa.Services.Accounts;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static unsafe void NativeMediaWorks()
    {
        byte[] webp = new byte[30];
        "RIFF"u8.CopyTo(webp); BinaryPrimitives.WriteUInt32LittleEndian(webp.AsSpan(4), 22);
        "WEBPVP8L"u8.CopyTo(webp.AsSpan(8)); BinaryPrimitives.WriteUInt32LittleEndian(webp.AsSpan(16), 10);
        webp[20] = 0x2f;
        AssertEqual<PngImage?>(null, PngImage.TryCreate(webp));
        AssertEqual(1, PngImage.TryCreateResourceIcon(webp)!.Width);
        webp[21] = 255; webp[22] = 63;
        AssertEqual<PngImage?>(null, PngImage.TryCreateResourceIcon(webp));
        byte[] jpeg = [0xff, 0xd8, 0xff, 0xc0, 0, 11, 8, 0, 1, 0, 1, 1, 1, 0x11, 0, 0xff, 0xda, 0, 2, 0xff, 0xd9];
        AssertEqual(1, PngImage.TryCreateResourceIcon(jpeg)!.Width);
        AssertEqual<PngImage?>(null, PngImage.TryCreate(jpeg));
        jpeg[^1] = 0;
        AssertEqual<PngImage?>(null, PngImage.TryCreateResourceIcon(jpeg));
        byte[] skin = MakeSkin(64, 64);
        var image = WardrobeSkinValidator.Validate(skin, true);
        AssertEqual(64, image.Width);
        AssertEqual(64, image.Height);
        skin[0] = 0;
        AssertEqual((byte)137, image.Bytes.Span[0]);
        AssertEqual<PngImage?>(null, PngImage.TryCreate(skin));
        Reject(() => WardrobeSkinValidator.Validate(skin, false));
        byte[] classic = MakeSkin(64, 32);
        AssertEqual(32, WardrobeSkinValidator.Validate(classic, false).Height);
        Reject(() => WardrobeSkinValidator.Validate(classic, true));
        AssertEqual(32, WardrobeSkinValidator.ValidateTexture(classic, AccountWardrobeTextureKind.Cape).Height);
        byte[] hd = MakeSkin(128, 128);
        AssertEqual(128, WardrobeSkinValidator.ValidateTexture(hd, AccountWardrobeTextureKind.Skin).Width);
        Reject(() => WardrobeSkinValidator.Validate(hd, false));
        byte[] corrupt = MakeSkin(64, 64);
        corrupt[^1] ^= 1;
        Reject(() => WardrobeSkinValidator.Validate(corrupt, false));
        // Metadata factories intentionally only inspect format/dimensions, not full PNG integrity.
        AssertEqual(64, PngImage.TryCreatePreview(corrupt)!.Width);
        AssertEqual(64, PngImage.TryCreateResourceIcon(corrupt)!.Width);
        AssertEqual<PngImage?>(null, PngImage.TryCreatePreview(new byte[16 * 1_048_576 + 1]));
        string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        nint library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "nexa-media" + suffix));
        var inspect = (delegate* unmanaged[Cdecl]<byte*, int, int, int, int*, int*, byte*, int, int>)NativeLibrary.GetExport(library, "nexa_media_inspect_v1");
        int width = 7, height = 7;
        byte* diagnostic = stackalloc byte[8];
        AssertEqual(-1, inspect(null, 1, 0, 0, &width, &height, diagnostic, 8));
        AssertEqual(0, width); AssertEqual(0, height);
        AssertEqual((byte)0, diagnostic[7]);
        AssertEqual(-1, inspect(null, 0, 6, 0, &width, &height, diagnostic, 8));
        AssertEqual(-1, inspect(null, 0, 0, 2, &width, &height, diagnostic, 8));
        Parallel.For(0, 32, _ => AssertEqual(64, WardrobeSkinValidator.Validate(MakeSkin(64, 64), false).Width));
        Console.WriteLine("Native media caller: ownership, skin/cape validation, malformed ABI and concurrent calls passed.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid PNG was accepted.");
    }

    private static byte[] MakeSkin(int width, int height)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6;
        Chunk("IHDR"u8, header);
        using var data = new MemoryStream();
        using (var zlib = new ZLibStream(data, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(new byte[(width * 4 + 1) * height]);
        Chunk("IDAT"u8, data.ToArray()); Chunk("IEND"u8, []);
        return output.ToArray();

        void Chunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> bytes)
        {
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length); output.Write(number);
            output.Write(type); output.Write(bytes);
            uint crc = uint.MaxValue;
            foreach (byte b in type) Update(b);
            foreach (byte b in bytes) Update(b);
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
            void Update(byte b)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
            }
        }
    }
}
