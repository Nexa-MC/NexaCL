using System.Text;

namespace Nexa.Platform;

public sealed partial class PlatformDirectorySnapshot
{
    // The owning content-store lock spans recovery, creation and lease disposal. Markers
    // precede native creation, so a terminated launcher never loses ownership information.
    private static async Task WriteLeaseAsync(string marker, string value, CancellationToken token)
    {
        bool created = false;
        try
        {
            await using var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
            created = true;
            await stream.WriteAsync(Encoding.UTF8.GetBytes("NEXA-SNAPSHOT-1\n" + value), token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false); stream.Flush(true);
        }
        catch { if (created) File.Delete(marker); throw; }
    }

    private static async Task<bool> ReapAsync(string storage, CancellationToken token)
    {
        var markers = Directory.EnumerateFiles(storage, "nexacl-*.lease").Take(129).ToArray();
        if (markers.Length > 128) return false;
        foreach (string marker in markers.Take(8))
        {
            token.ThrowIfCancellationRequested();
            string name = Path.GetFileNameWithoutExtension(marker);
            if (name.Length != 39 || !Guid.TryParseExact(name.AsSpan(7), "N", out _) || HasLinks(marker)
                || (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 || new FileInfo(marker).Length > 512) return false;
            string[] parts = (await File.ReadAllTextAsync(marker, token).ConfigureAwait(false)).Split('\n');
            if (parts.Length < 2 || parts[0] != "NEXA-SNAPSHOT-1") return false;
            if (parts.Length == 2 && parts[1] == "btrfs")
            {
                string target = Path.Combine(storage, name);
                if (Directory.Exists(target))
                {
                    string? tool = Tool("btrfs");
                    if (tool is null || HasLinks(target)) return false;
                    var readOnly = await RunAsync(tool, ["property", "get", "-ts", target, "ro"], token).ConfigureAwait(false);
                    if (readOnly.Code != 0 || readOnly.Text.Trim() != "ro=true") return false;
                    if ((await RunAsync(tool, ["subvolume", "delete", "--", target], token).ConfigureAwait(false)).Code != 0) return false;
                }
                File.Delete(marker);
            }
            else if (parts.Length == 3 && parts[1] == "zfs" && ExactDataset(parts[2] + "\t/source", "/source") == parts[2])
            {
                string? tool = Tool("zfs"); if (tool is null) return false;
                string identity = parts[2] + "@" + name;
                var exists = await RunAsync(tool, ["list", "-H", "-o", "name", "-t", "snapshot", identity], token).ConfigureAwait(false);
                // A permission or availability failure is not proof the snapshot is absent.
                if (exists.Code != 0 || exists.Text.Trim() != identity) return false;
                if ((await RunAsync(tool, ["destroy", identity], token).ConfigureAwait(false)).Code != 0) return false;
                File.Delete(marker);
            }
            else return false;
        }
        return markers.Length <= 8;
    }
}
