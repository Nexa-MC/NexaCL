using System.Text;
using Nexa.Core.Xml;
using Nexa.Xsr;

namespace Nexa.Services.Themes;

public sealed partial class ThemePackageService
{
    private sealed record ResourceLease(ThemeResourcePayload Payload, Guid? GrantId, string Directory, long Length);
    private readonly Dictionary<Guid, ResourceLease> _leases = [];

    public ValueTask<XsrResult> GrantAsync(ThemeResourceGrantCommand command, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate)
        {
            if (_disposed || (!_installed.ContainsKey(command.SourceIdentity) && !_candidates.Values.Any(c => c.Installation.Identity == command.SourceIdentity))) return ValueTask.FromResult(Reject("Unknown resource authority."));
            try
            {
                if (!Enum.IsDefined(command.Kind) || !Enum.IsDefined(command.Scope) || command.SelectedPath.Length > 4096 || _grants.Count >= 1024) return ValueTask.FromResult(Reject("Invalid resource grant."));
                string path = ThemePaths.Canonical(command.SelectedPath); ThemePaths.NoLinks(path);
                if (command.Scope == ThemeGrantScope.File ? !File.Exists(path) : !Directory.Exists(path)) return ValueTask.FromResult(Reject("Selected resource path does not exist."));
                if (command.Scope == ThemeGrantScope.File) ContentType(path, command.Kind);
                if (_grants.Values.Any(g => g.SourceIdentity == command.SourceIdentity && g.Kind == command.Kind && g.Scope == command.Scope && SamePath(g.CanonicalPath, path))) return ValueTask.FromResult(XsrResult.Success());
                var grant = new ThemeResourceGrant(Guid.NewGuid(), command.SourceIdentity, command.Kind, command.Scope, path, _revision + 1);
                _grants.Add(grant.GrantId, grant); _revision++;
                try { WriteState(); } catch { _grants.Remove(grant.GrantId); _revision--; throw; }
                return ValueTask.FromResult(XsrResult.Success());
            }
            catch (Exception error) when (Recoverable(error)) { return ValueTask.FromResult(Reject(error.Message)); }
        }
    }
    public ValueTask<XsrResult> RevokeAsync(ThemeResourceRevokeCommand command, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate)
        {
            if (!_grants.Remove(command.GrantId, out var grant)) return ValueTask.FromResult(Reject("Resource grant does not exist."));
            foreach (Guid lease in _leases.Where(l => l.Value.GrantId == command.GrantId).Select(l => l.Key).ToArray()) ReleaseLease(lease);
            _revision++;
            try { WriteState(); return ValueTask.FromResult(XsrResult.Success()); }
            catch (Exception error) when (Recoverable(error)) { _grants.Add(grant.GrantId, grant); _revision--; return ValueTask.FromResult(Reject(error.Message)); }
        }
    }
    public async ValueTask<XsrResult<ThemeResourcePayload>> ReadResourceAsync(ThemeResourceQuery query, CancellationToken token = default)
    {
        await _operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Prepared prepared; string path; Guid? grant;
            lock (_gate)
            {
                CheckLive(); if (!_candidates.TryGetValue(query.CandidateId, out prepared!)) return Reject<ThemeResourcePayload>("Candidate has been retired.");
                var expression = ThemePaths.Expression(query.Expression); if (expression.Function == "folder") return Reject<ThemeResourcePayload>("Use the bounded list route for folder resources.");
                (path, grant) = Resolve(prepared, expression.Function, expression.Argument, query.Kind);
            }
            return await PrepareResourceAsync(prepared, path, grant, query.Kind, token).ConfigureAwait(false);
        }
        catch (Exception error) when (Recoverable(error)) { return Reject<ThemeResourcePayload>(error.Message); }
        finally { _operations.Release(); }
    }
    public async ValueTask<XsrResult<ThemeResourceListPayload>> ListResourcesAsync(ThemeResourceListQuery query, CancellationToken token = default)
    {
        await _operations.WaitAsync(token).ConfigureAwait(false); var leases = new List<ThemeResourcePayload>();
        try
        {
            Prepared prepared; string folder; Guid? grant;
            lock (_gate)
            {
                CheckLive(); if (!_candidates.TryGetValue(query.CandidateId, out prepared!)) return Reject<ThemeResourceListPayload>("Candidate has been retired.");
                var expression = ThemePaths.Expression(query.Expression); if (expression.Function != "folder") return Reject<ThemeResourceListPayload>("The list route requires folder().");
                (folder, grant) = Resolve(prepared, expression.Function, expression.Argument, query.Kind);
            }
            long bytes = 0; int enumerated = 0;
            foreach (string path in Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested(); if (++enumerated > 4096) throw new InvalidDataException("Resource folder enumeration exceeds its budget.");
                ThemePaths.NoLinks(path); if (Directory.Exists(path) || !SupportedExtension(path, query.Kind)) continue;
                if (leases.Count >= 128) throw new InvalidDataException("Resource playlist exceeds 128 entries.");
                bytes = checked(bytes + new FileInfo(path).Length); if (bytes > 256L * 1024 * 1024) throw new InvalidDataException("Resource playlist exceeds 256 MiB.");
                var result = await PrepareResourceAsync(prepared, path, grant, query.Kind, token).ConfigureAwait(false);
                if (!result.IsSuccess) throw new InvalidDataException(result.Error!.Message); leases.Add(result.Value);
            }
            lock (_gate)
            {
                if (!_candidates.ContainsKey(query.CandidateId) || grant is { } id && !_grants.ContainsKey(id)) throw new InvalidDataException("Resource scope retired during playlist preparation.");
                return XsrResult.Success(new ThemeResourceListPayload(prepared.Candidate.SourceIdentity, prepared.Candidate.Generation, leases.AsReadOnly()));
            }
        }
        catch (Exception error) when (Recoverable(error)) { lock (_gate) foreach (var lease in leases) ReleaseLease(lease.LeaseId); return Reject<ThemeResourceListPayload>(error.Message); }
        catch { lock (_gate) foreach (var lease in leases) ReleaseLease(lease.LeaseId); throw; }
        finally { _operations.Release(); }
    }

    private (string Path, Guid? Grant) Resolve(Prepared prepared, string function, string argument, ThemeResourceKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new InvalidDataException("Invalid resource kind.");
        if (function == "asset") return (ThemePaths.Within(prepared.Installation.Root, argument), null);
        if (!HasPriorResourceAuthority(prepared)) throw new InvalidDataException("A theme from a different source requires new external resource grants.");
        string path = ThemePaths.Canonical(argument); ThemePaths.NoLinks(path);
        var grant = _grants.Values.FirstOrDefault(g => g.SourceIdentity == prepared.Candidate.SourceIdentity && g.Kind == kind &&
            (g.Scope == ThemeGrantScope.File && function == "file" && SamePath(path, g.CanonicalPath)
            || g.Scope == ThemeGrantScope.Directory && (SamePath(path, g.CanonicalPath) || path.StartsWith(Path.TrimEndingDirectorySeparator(g.CanonicalPath) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))));
        if (grant is null || function == "folder" && grant.Scope != ThemeGrantScope.Directory) throw new InvalidDataException("External resource has no user-selected grant for this source and kind.");
        if (function == "folder" ? !Directory.Exists(path) : !File.Exists(path)) throw new InvalidDataException("Granted resource is missing.");
        return (path, grant.GrantId);
    }
    private async Task<XsrResult<ThemeResourcePayload>> PrepareResourceAsync(Prepared prepared, string path, Guid? grantId, ThemeResourceKind kind, CancellationToken token)
    {
        ThemePaths.NoLinks(path); string contentType = ContentType(path, kind); long length = new FileInfo(path).Length;
        int limit = kind is ThemeResourceKind.Image or ThemeResourceKind.Icon or ThemeResourceKind.Font ? 16 * 1024 * 1024 : 128 * 1024 * 1024;
        if (length > limit) return Reject<ThemeResourcePayload>("Resource exceeds its kind budget.");
        Guid leaseId = Guid.NewGuid(); string directory = Path.Combine(_root, "leases", leaseId.ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string staged = Path.Combine(directory, "resource" + Path.GetExtension(path).ToLowerInvariant());
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) await ThemeSourceReader.CopyExactAsync(input, staged, length, token).ConfigureAwait(false);
            byte[] bytes = kind is ThemeResourceKind.Audio or ThemeResourceKind.Video ? [] : ThemeSourceReader.ReadBounded(staged, limit);
            ValidateResource(bytes, contentType);
            var payload = new ThemeResourcePayload(leaseId, prepared.Candidate.SourceIdentity, prepared.Candidate.Generation, kind, Path.GetFileName(path), contentType, bytes, staged);
            lock (_gate)
            {
                CheckLive(); token.ThrowIfCancellationRequested();
                if (!_candidates.TryGetValue(prepared.Candidate.CandidateId, out var current) || current.Candidate.Generation != payload.Generation || grantId is { } id && !_grants.ContainsKey(id))
                    throw new InvalidDataException("Resource scope changed while preparing.");
                if (_leases.Count >= 512) throw new InvalidDataException("Resource lease count exceeds its budget.");
                var activationLeases = _leases.Values.Where(lease => lease.Payload.Generation == payload.Generation).ToArray();
                if (activationLeases.Length >= 256 || activationLeases.Sum(lease => lease.Length) + length > 512L * 1024 * 1024) throw new InvalidDataException("Activation resources exceed their 256 leases / 512 MiB budget.");
                if (_leases.Values.Sum(lease => lease.Length) + length > 1024L * 1024 * 1024) throw new InvalidDataException("Active and candidate resource leases exceed their 1 GiB budget.");
                _leases.Add(leaseId, new(payload, grantId, directory, length)); return XsrResult.Success(payload);
            }
        }
        catch { DeleteDirectory(directory); throw; }
    }
    public ValueTask<XsrResult> ReleaseResourceAsync(ThemeResourceReleaseCommand command, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate) { ReleaseLease(command.LeaseId); return ValueTask.FromResult(XsrResult.Success()); }
    }
    private void ReleaseLease(Guid id)
    {
        if (!_leases.Remove(id, out var lease)) return; Array.Clear(lease.Payload.Bytes); DeleteDirectory(lease.Directory);
    }
    private void RetireGeneration(long generation) { foreach (Guid id in _leases.Where(l => l.Value.Payload.Generation == generation).Select(l => l.Key).ToArray()) ReleaseLease(id); }
    private void RetireSourceResources(string identity) { foreach (Guid id in _leases.Where(l => l.Value.Payload.SourceIdentity == identity).Select(l => l.Key).ToArray()) ReleaseLease(id); }

    private static bool SupportedExtension(string path, ThemeResourceKind kind)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return kind switch
        {
            ThemeResourceKind.Image => extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg",
            ThemeResourceKind.Icon => extension is ".png" or ".ico" or ".svg",
            ThemeResourceKind.Font => extension is ".ttf" or ".otf" or ".woff" or ".woff2",
            ThemeResourceKind.Audio => extension is ".mp3" or ".ogg" or ".wav" or ".flac" or ".m4a" or ".aac",
            ThemeResourceKind.Video => extension is ".mp4" or ".webm" or ".mkv" or ".mov",
            _ => false
        };
    }
    internal static string ContentType(string path, ThemeResourceKind kind)
    {
        if (!SupportedExtension(path, kind)) throw new InvalidDataException("Resource format is not admitted for its kind.");
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".mp3" => "audio/mpeg",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            ".aac" => "audio/aac",
            ".m4a" => "audio/mp4",
            ".mp4" => "video/mp4",
            ".webm" or ".mkv" => "video/webm",
            ".mov" => "video/quicktime",
            _ => throw new InvalidDataException("Unknown resource format.")
        };
    }
    private static void ValidateResource(byte[] bytes, string contentType)
    {
        if (contentType == "image/svg+xml")
        {
            if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("SVG exceeds 4 MiB.");
            var document = XmlSnapshotReader.ParseBytes(bytes, new(4 * 1024 * 1024, 8192));
            bool root = false;
            foreach (var element in document.Descendants())
            {
                if (!root) { if (element.Name.LocalName != "svg") throw new InvalidDataException("SVG root is invalid."); root = true; }
                if (element.Name.LocalName is "script" or "style" or "foreignObject" or "iframe" or "image" or "animate" or "animateTransform" or "set") throw new InvalidDataException("SVG active or external content is prohibited.");
                foreach (var attribute in element.Attributes())
                    if (attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                        || attribute.Name.LocalName is "href" && !attribute.Value.StartsWith('#') || ExternalSvgUrl(attribute.Value))
                        throw new InvalidDataException("SVG external references and active attributes are prohibited.");
            }
            if (!root) throw new InvalidDataException("Empty SVG."); return;
        }
        if (bytes.Length == 0 && (contentType.StartsWith("image/", StringComparison.Ordinal) || contentType.StartsWith("font/", StringComparison.Ordinal))) throw new InvalidDataException("Empty visual resource.");
        if (contentType == "image/png" && !bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || contentType == "image/jpeg" && !bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 })
            || contentType == "image/gif" && !bytes.AsSpan().StartsWith("GIF8"u8)
            || contentType == "image/webp" && (bytes.Length < 12 || !bytes.AsSpan().StartsWith("RIFF"u8) || !bytes.AsSpan(8).StartsWith("WEBP"u8))) throw new InvalidDataException("Resource magic does not match its declared format.");
    }
    private static bool ExternalSvgUrl(string value)
    {
        int start = 0;
        while ((start = value.IndexOf("url(", start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            start += 4; int close = value.IndexOf(')', start);
            if (close < 0) return true; string target = value[start..close].Trim().Trim('"', '\'');
            if (!target.StartsWith('#')) return true; start = close + 1;
        }
        return false;
    }
}
