using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100.Data;

namespace Nexa.Services.Composition.Plugins;

/// <summary>Host grants canonical paths. Guests may only use opaque, load-owned references.</summary>
public sealed class PluginFileResources : IDisposable
{
    public void Dispose() { lock (_gate) _grants.Clear(); MutationGate.Dispose(); }
    internal Func<PluginIdentity, bool>? IsOwnerActive { get; set; }
    private sealed record Grant(PluginIdentity Owner, FileRef Reference, string Path, bool Writable);
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Grant> _grants = [];
    internal readonly SemaphoreSlim MutationGate = new(1, 1);
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public FileRef GrantPath(PluginIdentity owner, string path, bool writable = false)
    {
        path = Canonical(path);
        lock (_gate)
        {
            if (IsOwnerActive is not null && !IsOwnerActive(owner)) throw new AbiHostException(AbiErrors.PLUGIN_STOPPED);
            var previous = _grants.Values.FirstOrDefault(g => g.Owner == owner && g.Path.Equals(path, PathComparison) && g.Writable == writable);
            if (previous is not null) return previous.Reference;
            if (_grants.Count >= 4096) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            var reference = new FileRef(Guid.NewGuid(), owner.LoadInstanceId, writable ? "selected-write" : "selected-read");
            _grants.Add(reference.Token, new(owner, reference, path, writable)); return reference;
        }
    }
    public string Resolve(PluginIdentity owner, FileRef reference, bool write = false)
    {
        lock (_gate)
        {
            if (!_grants.TryGetValue(reference.Token, out var grant) || grant.Owner != owner || grant.Reference != reference) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            if (write && !grant.Writable) throw new AbiHostException(AbiErrors.PERMISSION_DENIED);
            if (!Canonical(grant.Path).Equals(grant.Path, PathComparison)) throw new AbiHostException(AbiErrors.CONFLICT);
            return grant.Path;
        }
    }
    public void Retire(string pluginId)
    {
        lock (_gate) foreach (var key in _grants.Where(p => p.Value.Owner.PluginId == pluginId).Select(p => p.Key).ToArray()) _grants.Remove(key);
    }
    private static string Canonical(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        path = Path.GetFullPath(path); string current = Path.GetPathRoot(path)!;
        foreach (var segment in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null) current = info.ResolveLinkTarget(true)?.FullName ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }
    internal string ObservationStamp(PluginIdentity owner, FileRef reference)
    {
        string path = Resolve(owner, reference);
        if (Directory.Exists(path))
        {
            var entries = new DirectoryInfo(path).EnumerateFileSystemInfos().Take(4097).Select(i => i.Name + ":" + i.LastWriteTimeUtc.Ticks + ":" + i.CreationTimeUtc.Ticks + ":" + i.Attributes).Order(StringComparer.Ordinal).ToArray();
            if (entries.Length > 4096) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            return string.Join('\n', entries);
        }
        var file = new FileInfo(path); return file.Exists ? string.Join(':', file.Length, file.LastWriteTimeUtc.Ticks, file.CreationTimeUtc.Ticks, file.Attributes) : "absent";
    }
    public async ValueTask<FileStat> StatAsync(PluginIdentity owner, FileRef reference, CancellationToken token)
    {
        string path = Resolve(owner, reference);
        if (Directory.Exists(path))
        {
            var info = new DirectoryInfo(path);
            var entries = info.EnumerateFileSystemInfos().Take(4097).Select(x => x.Name + ":" + x.LastWriteTimeUtc.Ticks + ":" + x.Attributes).Order(StringComparer.Ordinal).ToArray();
            if (entries.Length > 4096) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            token.ThrowIfCancellationRequested();
            return new(reference, info.Name, true, 0, info.LastWriteTimeUtc, new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', entries))))));
        }
        if (!File.Exists(path)) return new(reference, Path.GetFileName(path), false, 0, DateTimeOffset.UnixEpoch, new("absent"));
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > 256L * 1024 * 1024) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
        long size = stream.Length; var modified = File.GetLastWriteTimeUtc(path);
        var digest = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
        Resolve(owner, reference);
        return new(reference, Path.GetFileName(path), false, size, modified, new(Convert.ToHexString(digest)));
    }
    public async ValueTask<byte[]> ReadAsync(PluginIdentity owner, FileRead request, CancellationToken token)
    {
        if (request.Offset < 0 || request.Length is < 0 or > 4 * 1024 * 1024) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        string path = Resolve(owner, request.File);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.RandomAccess);
        if (request.Offset > stream.Length || request.Length > stream.Length - request.Offset) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        stream.Position = request.Offset; byte[] bytes = new byte[(int)request.Length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false); Resolve(owner, request.File); return bytes;
    }
    public async ValueTask<ImmutableArray<FileStat>> ListAsync(PluginIdentity owner, FileRef directory, CancellationToken token)
    {
        string path = Resolve(owner, directory); bool write;
        lock (_gate) write = _grants[directory.Token].Writable;
        if (!Directory.Exists(path)) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        var entries = Directory.EnumerateFileSystemEntries(path).Order(StringComparer.Ordinal).Take(4097).ToArray();
        if (entries.Length > 4096) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
        var result = ImmutableArray.CreateBuilder<FileStat>();
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            // Listing a granted directory cannot silently grant the target of a child link.
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) continue;
            if (!Canonical(entry).StartsWith(path + Path.DirectorySeparatorChar, PathComparison)) throw new AbiHostException(AbiErrors.CONFLICT);
            result.Add(await StatAsync(owner, GrantPath(owner, entry, write), token).ConfigureAwait(false));
        }
        Resolve(owner, directory); return result.ToImmutable();
    }
    public async ValueTask<FileStat> WriteAsync(PluginIdentity owner, FileWrite request, byte[] bytes, CancellationToken token)
    {
        await MutationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            string path = Resolve(owner, request.File, true);
            var before = await StatAsync(owner, request.File, token).ConfigureAwait(false);
            if (before.IsDirectory || before.Revision != request.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
            string temporary = Path.Combine(Path.GetDirectoryName(path)!, ".nexa-plugin-" + Guid.NewGuid().ToString("N"));
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false);
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                Resolve(owner, request.File, true);
                if ((await StatAsync(owner, request.File, token).ConfigureAwait(false)).Revision != request.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
                token.ThrowIfCancellationRequested(); File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return await StatAsync(owner, request.File, token).ConfigureAwait(false);
        }
        finally { MutationGate.Release(); }
    }
}
