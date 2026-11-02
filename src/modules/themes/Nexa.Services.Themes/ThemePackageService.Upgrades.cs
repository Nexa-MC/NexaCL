using System.Globalization;
using System.Numerics;

namespace Nexa.Services.Themes;

public sealed partial class ThemePackageService
{
    private Installation? FindInstalledTheme(string themeId) => _installed.Values
        .Where(i => i.Manifest.Id == themeId)
        .OrderByDescending(i => i.Manifest.Version, Comparer<string>.Create(CompareThemeVersions))
        .ThenBy(i => i.Identity, StringComparer.Ordinal).FirstOrDefault();

    // NTP uses dotted numeric versions with an optional .channel.sequence suffix.
    // Numeric components are compared numerically; a release follows its prereleases.
    private static int CompareThemeVersions(string left, string right)
    {
        string[] a = left.Split('.'), b = right.Split('.');
        int ac = Array.FindIndex(a, part => !BigInteger.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _));
        int bc = Array.FindIndex(b, part => !BigInteger.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _));
        if (ac < 0) ac = a.Length;
        if (bc < 0) bc = b.Length;
        for (int i = 0; i < Math.Max(ac, bc); i++)
        {
            int comparison = (i < ac ? BigInteger.Parse(a[i], CultureInfo.InvariantCulture) : BigInteger.Zero)
                .CompareTo(i < bc ? BigInteger.Parse(b[i], CultureInfo.InvariantCulture) : BigInteger.Zero);
            if (comparison != 0) return comparison;
        }
        if (ac == a.Length || bc == b.Length) return (ac == a.Length).CompareTo(bc == b.Length);
        int channel = StringComparer.Ordinal.Compare(a[ac], b[bc]);
        return channel != 0 ? channel : BigInteger.Parse(a[ac + 1], CultureInfo.InvariantCulture).CompareTo(BigInteger.Parse(b[bc + 1], CultureInfo.InvariantCulture));
    }

    private static bool SameOrigin(Installation left, Installation right) => left.Kind == right.Kind
        && left.Origin is not null && right.Origin is not null && SamePath(left.Origin, right.Origin);

    private static bool HasPriorResourceAuthority(Prepared prepared) => prepared.PreviousInstallation is null
        || SameOrigin(prepared.PreviousInstallation, prepared.Installation);
}
