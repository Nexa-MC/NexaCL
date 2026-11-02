namespace Nexa.Native.Artwork;

internal static class ArtworkCatalog
{
    internal static Stream? Open(string name)
    {
        if (name.Length is < 1 or > 256 || !(name is "Nexa.Desktop.Assets.icon.png" or "Nexa.Desktop.Assets.icon.ico"
            || name.StartsWith("Nexa.Desktop.Assets.Brand.", StringComparison.Ordinal)
            || name.StartsWith("Nexa.Desktop.Assets.Gamepads.", StringComparison.Ordinal))) return null;
        return typeof(ArtworkCatalog).Assembly.GetManifestResourceStream(name);
    }
}
