#if NEXA_EXTERNAL_MODULES
using System.Text;
using Nexa.Native.Interop;
#endif

namespace Nexa.Xsr.Renderer.Artwork;

/// <summary>Read-only product image resources, independently owned in native distributions.</summary>
public static class ProductArtwork
{
#if NEXA_EXTERNAL_MODULES
    private static readonly Lazy<NativeResultClient> Client = new(() => new("nexa-artwork", "nexa_artwork", 256, 16 * 1024 * 1024 + 1));
#endif
    public static Stream? Open(string resourceName)
    {
        ArgumentNullException.ThrowIfNull(resourceName);
#if NEXA_EXTERNAL_MODULES
        if (resourceName.Length is < 1 or > 256) return null;
        byte[] request = Encoding.UTF8.GetBytes(resourceName);
        if (request.Length > 256) return null;
        byte[] result = Client.Value.Execute(request);
        if (result.Length == 1 && result[0] == 0) return null;
        if (result.Length < 2 || result[0] != 1) throw new InvalidDataException("Invalid artwork response.");
        return new MemoryStream(result, 1, result.Length - 1, writable: false);
#else
        return Nexa.Native.Artwork.ArtworkCatalog.Open(resourceName);
#endif
    }
}
