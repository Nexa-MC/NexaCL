using Nexa.Services.Accounts;

namespace Nexa.Native.Services.Wire;

internal sealed partial class ServiceWireWriter
{
    internal void IReadOnlyList_WardrobeCatalogSite(IReadOnlyList<WardrobeCatalogSite>? values)
    {
        if (values is null) { Int32(-1); return; }
        Count(values.Count); foreach (var value in values) WardrobeCatalogSite(value);
    }
}
internal sealed partial class ServiceWireReader
{
    internal IReadOnlyList<WardrobeCatalogSite>? IReadOnlyList_WardrobeCatalogSite()
    {
        int count = Count(); if (count < 0) return null;
        var items = new WardrobeCatalogSite[count];
        for (int index = 0; index < count; index++) items[index] = WardrobeCatalogSite() ?? throw new InvalidDataException("Missing wardrobe site.");
        return items;
    }
}
