using System.Security.Cryptography;
using System.Text;

namespace Nexa.Services.Resources;

/// <summary>Stable identity of the exact ordered files resolved by the resource owner.</summary>
public static class ResourceInstallPlanIdentity
{
    public static string Fingerprint(IReadOnlyList<ResourceVersion> versions)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
        {
            writer.Write(1); writer.Write(versions.Count);
            foreach (var version in versions)
            {
                writer.Write((int)version.Provider); writer.Write(version.ProjectId); writer.Write(version.Id);
                writer.Write(version.File is not null);
                if (version.File is { } file)
                {
                    writer.Write(file.Name); writer.Write(file.Url); writer.Write(file.Size);
                    writer.Write(file.Sha1 ?? ""); writer.Write(file.Sha512 ?? "");
                }
            }
        }
        return Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length))));
    }
}
