using System.Diagnostics;
using System.Text;

namespace Nexa.Platform;

/// <summary>Unprivileged read-only snapshot leases; never rolls back a live filesystem.</summary>
public sealed partial class PlatformDirectorySnapshot : IAsyncDisposable
{
    private static readonly string[] ToolDirectories = ["/usr/sbin/", "/usr/bin/", "/sbin/", "/bin/"];
    private readonly string _tool;
    private readonly string _marker;
    private readonly string[] _delete;
    private int _disposed;
    public string SourceDirectory { get; }
    private PlatformDirectorySnapshot(string source, string tool, string[] delete, string marker)
    { SourceDirectory = source; _tool = tool; _delete = delete; _marker = marker; }

    public static async Task<PlatformDirectorySnapshot?> TryCaptureAsync(string source, string storage, CancellationToken token = default)
    {
        if (!OperatingSystem.IsLinux()) return null;
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        storage = Path.TrimEndingDirectorySeparator(Path.GetFullPath(storage));
        if (!Directory.Exists(source) || HasLinks(source) || HasLinks(storage)) return null;
        Directory.CreateDirectory(storage);
        if (HasLinks(storage) || !await ReapAsync(storage, token).ConfigureAwait(false)) return null;
        string suffix = "nexacl-" + Guid.NewGuid().ToString("N");
        string marker = Path.Combine(storage, suffix + ".lease");
        string? btrfs = Tool("btrfs");
        if (btrfs is not null && (await RunAsync(btrfs, ["subvolume", "show", "--", source], token).ConfigureAwait(false)).Code == 0)
        {
            var children = await RunAsync(btrfs, ["subvolume", "list", "-o", "--", source], token).ConfigureAwait(false);
            if (children.Code != 0 || children.Text.Trim().Length != 0) return null;
            Directory.CreateDirectory(storage);
            if (HasLinks(storage)) return null;
            string target = Path.Combine(storage, suffix);
            await WriteLeaseAsync(marker, "btrfs", token).ConfigureAwait(false);
            var lease = new PlatformDirectorySnapshot(target, btrfs, ["subvolume", "delete", "--", target], marker);
            try
            {
                var result = await RunAsync(btrfs, ["subvolume", "snapshot", "-r", "--", source, target], token).ConfigureAwait(false);
                if (result.Code == 0 && Directory.Exists(target)) return lease;
            }
            catch { if (Directory.Exists(target)) await lease.DisposeAsync().ConfigureAwait(false); else File.Delete(marker); throw; }
            if (Directory.Exists(target)) await lease.DisposeAsync().ConfigureAwait(false); else File.Delete(marker);
            return null;
        }
        string? zfs = Tool("zfs");
        if (zfs is null) return null;
        var filesystems = await RunAsync(zfs, ["list", "-H", "-o", "name,mountpoint", "-t", "filesystem"], token).ConfigureAwait(false);
        if (filesystems.Code != 0) return null;
        string? dataset = ExactDataset(filesystems.Text, source);
        if (dataset is null) return null;
        var descendants = await RunAsync(zfs, ["list", "-H", "-r", "-o", "name", "-t", "filesystem", dataset], token).ConfigureAwait(false);
        if (descendants.Code != 0 || descendants.Text.Trim() != dataset) return null;
        string identity = dataset + "@" + suffix;
        string snapshotPath = Path.Combine(source, ".zfs", "snapshot", suffix);
        await WriteLeaseAsync(marker, "zfs\n" + dataset, token).ConfigureAwait(false);
        var zfsLease = new PlatformDirectorySnapshot(snapshotPath, zfs, ["destroy", identity], marker);
        try
        {
            var result = await RunAsync(zfs, ["snapshot", identity], token).ConfigureAwait(false);
            if (result.Code == 0)
            {
                if (Directory.Exists(snapshotPath)) return zfsLease;
                await zfsLease.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                var exists = await RunAsync(zfs, ["list", "-H", "-o", "name", "-t", "snapshot", identity], CancellationToken.None).ConfigureAwait(false);
                if (exists.Code == 0 && exists.Text.Trim() == identity) await zfsLease.DisposeAsync().ConfigureAwait(false);
                else if (result.Code != -1) File.Delete(marker);
            }
        }
        catch
        {
            // The snapshot may have committed before the cancelled response was observed.
            var exists = await RunAsync(zfs, ["list", "-H", "-o", "name", "-t", "snapshot", identity], CancellationToken.None).ConfigureAwait(false);
            if (exists.Code == 0 && exists.Text.Trim() == identity) await zfsLease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return null;
    }

    internal static string? ExactDataset(string output, string source)
    {
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('\t');
            if (fields.Length != 2 || fields[1] != source) continue;
            string name = fields[0];
            if (name.Length is < 1 or > 255 || !char.IsAsciiLetterOrDigit(name[0])
                || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('/' or '_' or '-' or '.' or ':'))
                || name.Split('/').Any(segment => segment is "" or "." or "..")) return null;
            return name;
        }
        return null;
    }

    private static string? Tool(string name) => ToolDirectories
        .Select(prefix => prefix + name).FirstOrDefault(File.Exists);
    private static bool HasLinks(string path)
    {
        for (var item = new DirectoryInfo(path); item is not null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }

    internal static async Task<(int Code, string Text)> RunAsync(string tool, IReadOnlyList<string> args, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var process = new Process { StartInfo = new(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.Environment["LC_ALL"] = "C";
        try
        {
            if (!process.Start()) return (-1, "");
            Task<string> output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            Task<string> error = ReadBoundedAsync(process.StandardError, timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                string text = await output.ConfigureAwait(false);
                await error.ConfigureAwait(false);
                return (process.ExitCode, text);
            }
            finally
            {
                if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
                await Task.WhenAll(output, error).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return (-1, ""); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException) { return (-1, ""); }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader input, CancellationToken token)
    {
        var text = new StringBuilder(); char[] buffer = new char[1024]; int count;
        while ((count = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            if (text.Length + count > 65536) throw new IOException("Native snapshot tool output exceeded its budget.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var result = await RunAsync(_tool, _delete, CancellationToken.None).ConfigureAwait(false);
        if (result.Code != 0) throw new IOException("无法清理本次原生备份快照，请检查文件系统权限。");
        File.Delete(_marker);
    }
}
