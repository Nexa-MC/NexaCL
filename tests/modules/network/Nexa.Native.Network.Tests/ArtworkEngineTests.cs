using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Nexa.Xsr.Renderer.Artwork;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static unsafe void NativeArtworkWorks()
    {
        var expected = typeof(Program).Assembly;
        string[] names = expected.GetManifestResourceNames().Where(name => name.StartsWith("Nexa.Desktop.Assets.", StringComparison.Ordinal)).ToArray();
        if (names.Length < 10) throw new InvalidOperationException("Missing artwork fixtures.");
        if (typeof(ProductArtwork).Assembly.GetManifestResourceNames().Any(name => name.StartsWith("Nexa.Desktop.Assets.", StringComparison.Ordinal)))
            throw new InvalidOperationException("Artwork leaked into Host/Core.");
        foreach (string name in names)
        {
            using Stream source = expected.GetManifestResourceStream(name)!;
            using Stream actual = ProductArtwork.Open(name) ?? throw new InvalidOperationException("Missing distributed artwork: " + name);
            AssertEqual(false, actual.CanWrite);
            AssertEqual(Convert.ToHexString(SHA256.HashData(source)), Convert.ToHexString(SHA256.HashData(actual)));
        }
        AssertEqual<Stream?>(null, ProductArtwork.Open("Nexa.Desktop.Assets.api-client.pfx"));
        AssertEqual<Stream?>(null, ProductArtwork.Open("Nexa.Desktop.Assets.Brand.missing.png"));
        AssertEqual<Stream?>(null, ProductArtwork.Open("../icon.png"));
        string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        nint library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "nexa-artwork" + suffix));
        var execute = (delegate* unmanaged[Cdecl]<byte*, int, int*, long>)NativeLibrary.GetExport(library, "nexa_artwork_execute_v1");
        var copy = (delegate* unmanaged[Cdecl]<long, byte*, int, int>)NativeLibrary.GetExport(library, "nexa_artwork_copy_v1");
        var release = (delegate* unmanaged[Cdecl]<long, void>)NativeLibrary.GetExport(library, "nexa_artwork_release_v1");
        int length = 7;
        AssertEqual(0L, execute(null, 1, &length)); AssertEqual(0, length);
        byte invalid = 255;
        AssertEqual(0L, execute(&invalid, 1, &length));
        AssertEqual(0L, execute(&invalid, 257, &length));
        byte[] request = Encoding.UTF8.GetBytes(names[0]);
        fixed (byte* input = request)
        {
            long id = execute(input, request.Length, &length);
            if (id <= 0 || length <= 1) throw new InvalidOperationException("Missing artwork lease.");
            try { AssertEqual(-1, copy(id, &invalid, 1)); }
            finally { release(id); }
            release(id);
            AssertEqual(-1, copy(id, &invalid, 1));
        }
        Parallel.For(0, 16, _ => { using Stream? source = ProductArtwork.Open(names[0]); AssertEqual(true, source is { Length: > 0 }); });
        Console.WriteLine($"Native artwork caller: {names.Length} images match, Host resources excluded, invalid requests and retired leases rejected.");
    }
}
