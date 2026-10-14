using System.Collections.ObjectModel;
using System.Text.Json;
using Nexa.Xsr;

namespace Nexa.Services.Themes;

/// <summary>Owns immutable admitted sources. Presentation compilation and native activation remain host operations.</summary>
public sealed partial class ThemePackageService : IThemePackageService
{
    private sealed record Installation(string Identity, string Root, string Revision, ThemeSourceKind Kind, string? Origin, ThemeManifest Manifest);
    private sealed record Prepared(ThemeCandidate Candidate, Installation Installation, long OptionsRevision);
    private readonly object _gate = new();
    private readonly SemaphoreSlim _operations = new(1);
    private readonly string _root;
    private readonly string? _builtin;
    private readonly HashSet<string> _capabilities;
    private readonly Dictionary<string, Installation> _installed = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Prepared> _candidates = [];
    private readonly Dictionary<string, ThemeOptionState> _options = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, ThemeResourceGrant> _grants = [];
    private readonly HashSet<string> _recoverPendingSources = new(StringComparer.Ordinal);
    private Guid? _previewCandidate;
    private ThemeSelection? _selection;
    private ThemeSelection? _lastKnownGood;
    private long _revision;
    private long _generation;
    private bool _recovered;
    private bool _migrated;
    private bool _disposed;

    public ThemePackageService(string dataRoot, string? builtinThemeRoot, IEnumerable<string> hostCapabilities, Action<ThemeWatchSnapshot>? watchChanged = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot); ArgumentNullException.ThrowIfNull(hostCapabilities);
        _root = Path.Combine(Path.GetFullPath(dataRoot), "themes"); _builtin = builtinThemeRoot is null ? null : Path.GetFullPath(builtinThemeRoot);
        _capabilities = new(hostCapabilities, StringComparer.Ordinal); ThemePaths.NoLinks(_root); Directory.CreateDirectory(_root);
        WatchChanged = watchChanged;
        Directory.CreateDirectory(Path.Combine(_root, "sources")); Directory.CreateDirectory(Path.Combine(_root, "leases"));
        ReadState(); Recover();
    }

    public ValueTask<XsrResult<ThemeCatalogueSnapshot>> CatalogueAsync(ThemeCatalogueQuery query, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate)
        {
            if (_disposed) return ValueTask.FromResult(Reject<ThemeCatalogueSnapshot>("Theme service is retired."));
            try
            {
                EnsureBuiltin();
                var sources = new Dictionary<string, Installation>(_installed, StringComparer.Ordinal);
                foreach (var candidate in _candidates.Values.OrderBy(candidate => candidate.Candidate.Generation)) sources[candidate.Installation.Identity] = candidate.Installation;
                return ValueTask.FromResult(XsrResult.Success(new ThemeCatalogueSnapshot(_revision, sources.Values.OrderBy(i => i.Manifest.Id, StringComparer.Ordinal)
                    .Select(i => new ThemeCatalogueEntry(i.Identity, i.Revision, i.Kind, i.Manifest, i.Kind == ThemeSourceKind.Directory ? i.Origin : null, _installed.ContainsKey(i.Identity), i.Root)).ToArray())));
            }
            catch (Exception error) when (Recoverable(error)) { return ValueTask.FromResult(Reject<ThemeCatalogueSnapshot>(error.Message)); }
        }
    }

    public async ValueTask<XsrResult<ThemeCandidate>> PrepareAsync(ThemePrepareQuery query, CancellationToken token = default)
    {
        await _operations.WaitAsync(token).ConfigureAwait(false); string? staging = null; string? ownedDestination = null;
        try
        {
            string identity, source; ThemeSourceKind kind; Installation? previous;
            lock (_gate)
            {
                CheckLive(); EnsureBuiltin();
                if (query.ExpectedRevision is { } expected && expected != _revision) return Reject<ThemeCandidate>("Theme options changed before candidate preparation.");
                if (query.SourceIdentity is { } existing)
                {
                    previous = query.UseCommittedSource ? _installed.GetValueOrDefault(existing) : FindSource(existing);
                    if (previous is null) return Reject<ThemeCandidate>("Unknown or uncommitted theme source identity.");
                    if (query.UseCommittedSource && !string.IsNullOrEmpty(query.SourcePath)) return Reject<ThemeCandidate>("Committed-source preparation cannot import a source update.");
                    if (query.SourceKind != previous.Kind) return Reject<ThemeCandidate>("Theme source kind does not match its admitted identity.");
                    identity = existing; kind = previous.Kind;
                    if (string.IsNullOrEmpty(query.SourcePath))
                    {
                        var validation = ThemeSourceReader.Validate(previous.Root, _capabilities);
                        if (previous.Kind != ThemeSourceKind.BuiltIn && validation.Revision != previous.Revision) return Reject<ThemeCandidate>("An owned immutable theme snapshot changed; reload its original source or import a new package.");
                        return PublishCandidate(previous with { Manifest = validation.Manifest, Revision = validation.Revision }, validation.Styles, query);
                    }
                    source = Path.GetFullPath(query.SourcePath);
                    if (previous.Origin is null || !SamePath(source, previous.Origin)) return Reject<ThemeCandidate>("A different source cannot inherit an installation's resource authority.");
                }
                else
                {
                    if (_installed.Count >= 256) return Reject<ThemeCandidate>("Installed theme count exceeds its budget.");
                    previous = null; identity = query.SourceKind == ThemeSourceKind.BuiltIn ? "builtin" : "source-" + Guid.NewGuid().ToString("N");
                    kind = query.SourceKind; source = Path.GetFullPath(query.SourcePath);
                    if (kind == ThemeSourceKind.BuiltIn && (_builtin is null || !SamePath(source, _builtin))) return Reject<ThemeCandidate>("Only the host-owned root can be a built-in theme.");
                    if (kind == ThemeSourceKind.BuiltIn && _installed.TryGetValue("builtin", out var builtin))
                    {
                        var validation = ThemeSourceReader.Validate(builtin.Root, _capabilities);
                        return PublishCandidate(builtin with { Manifest = validation.Manifest, Revision = validation.Revision }, validation.Styles, query);
                    }
                }
            }
            staging = Path.Combine(_root, "sources", ".candidate-" + Guid.NewGuid().ToString("N"));
            if (kind != ThemeSourceKind.Package && (SamePath(source, _root) || _root.StartsWith(Path.TrimEndingDirectorySeparator(source) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                return Reject<ThemeCandidate>("Theme source cannot include its own installation workspace.");
            await ThemeSourceReader.CopyAsync(source, kind, staging, token).ConfigureAwait(false);
            var admitted = ThemeSourceReader.Validate(staging, _capabilities);
            if (previous is not null && admitted.Manifest.Id != previous.Manifest.Id) return Reject<ThemeCandidate>("A source update cannot change its theme identity.");
            string destination = Path.Combine(_root, "sources", identity, admitted.Revision);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (Directory.Exists(destination)) DeleteDirectory(staging); else Directory.Move(staging, destination);
            staging = null; ownedDestination = destination;
            lock (_gate)
            {
                CheckLive(); token.ThrowIfCancellationRequested();
                if (previous is not null && (FindSource(identity) is not { } current || current.Revision != previous.Revision)) return Reject<ThemeCandidate>("The source changed or retired during candidate preparation.");
                var installation = new Installation(identity, destination, admitted.Revision, kind, source, admitted.Manifest);
                return PublishCandidate(installation, admitted.Styles, query);
            }
        }
        catch (Exception error) when (Recoverable(error)) { return Reject<ThemeCandidate>(error.Message); }
        finally
        {
            if (staging is not null) DeleteDirectory(staging);
            if (ownedDestination is not null) CleanupOwnedSource(ownedDestination);
            _operations.Release();
        }
    }

    private XsrResult<ThemeCandidate> PublishCandidate(Installation installation, IReadOnlyList<ThemeStyleSource> styles, ThemePrepareQuery query)
    {
        if (query.ExpectedRevision is { } expected && expected != _revision) return Reject<ThemeCandidate>("Theme options changed during candidate preparation.");
        if (_candidates.Count >= 8)
        {
            var oldest = _candidates.Values.Where(c => _selection?.Generation != c.Candidate.Generation && c.Candidate.CandidateId != _previewCandidate).OrderBy(c => c.Candidate.Generation).FirstOrDefault();
            if (oldest is null) return Reject<ThemeCandidate>("Too many retained candidates."); RetireCandidate(oldest.Candidate.CandidateId);
        }
        var values = ValuesFor(installation);
        var overrides = query.OptionsDocument is null ? query.OptionsOverride : ReadOptionsDocument(query.OptionsDocument, installation.Manifest);
        if (query.OptionsDocument is not null && query.OptionsOverride is not null) return Reject<ThemeCandidate>("Only one provisional options source is allowed.");
        if (overrides is not null)
        {
            if (overrides.Count > 256) return Reject<ThemeCandidate>("Provisional option values exceed their budget.");
            foreach (var pair in overrides)
            {
                var definition = installation.Manifest.Options.SingleOrDefault(option => option.Id == pair.Key) ?? throw new InvalidDataException("Unknown provisional option ID.");
                ThemeManifestReader.ValidateValue(definition, pair.Value); values[pair.Key] = pair.Value with { Values = pair.Value.Values?.ToArray() };
            }
        }
        ThemeManifestReader.ValidateAppearance(installation.Manifest.Appearance, values);
        long generation = checked(++_generation);
        var candidate = new ThemeCandidate(Guid.NewGuid(), generation, installation.Identity, installation.Revision, installation.Kind, installation.Manifest, styles,
            new ReadOnlyDictionary<string, ThemeOptionValue>(values), installation.Manifest.OptionalCapabilities.Where(c => !_capabilities.Contains(c.Name)).Select(c => c.Name).ToArray());
        WriteJournal(candidate); _candidates.Add(candidate.CandidateId, new(candidate, installation, _options.TryGetValue(installation.Identity, out var priorOptions) ? priorOptions.Revision : 0)); return XsrResult.Success(candidate);
    }

    public ValueTask<XsrResult<ThemeCandidate>> CandidateAsync(ThemeCandidateQuery query, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate) return ValueTask.FromResult(!_disposed && _candidates.TryGetValue(query.CandidateId, out var candidate)
            ? XsrResult.Success(candidate.Candidate) : Reject<ThemeCandidate>("Candidate has expired or been retired."));
    }
    public ValueTask<XsrResult<ThemeCurrentSnapshot>> CurrentAsync(ThemeCurrentQuery query, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate) return ValueTask.FromResult(_disposed ? Reject<ThemeCurrentSnapshot>("Theme service is retired.")
            : XsrResult.Success(new ThemeCurrentSnapshot(_revision, _selection, _lastKnownGood, _options.Values.ToArray(), _grants.Values.ToArray(), _recovered)));
    }

    public ValueTask<XsrResult> CommitAsync(ThemeCommitCommand command, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate)
        {
            if (_disposed || command.ExpectedRevision != _revision || !_candidates.TryGetValue(command.CandidateId, out var prepared)) return ValueTask.FromResult(Reject("Theme candidate or persistent revision is stale."));
            if (command.Preview) { _previewCandidate = command.CandidateId; return ValueTask.FromResult(XsrResult.Success()); } // Pin transient resource ownership without persisting selection.
            if ((_options.TryGetValue(prepared.Installation.Identity, out var currentOptions) ? currentOptions.Revision : 0) != prepared.OptionsRevision) return ValueTask.FromResult(Reject("Theme options changed after candidate preparation."));
            var oldSelection = _selection; var oldGood = _lastKnownGood; long oldRevision = _revision;
            _installed.TryGetValue(prepared.Installation.Identity, out var priorInstallation); _options.TryGetValue(prepared.Installation.Identity, out var priorOptions);
            try
            {
                _selection = new(prepared.Installation.Identity, prepared.Installation.Revision, prepared.Installation.Manifest.Id, prepared.Installation.Manifest.Version, prepared.Candidate.Generation);
                _lastKnownGood = _selection; _installed[prepared.Installation.Identity] = prepared.Installation;
                _options[prepared.Installation.Identity] = new(prepared.Installation.Identity, prepared.Installation.Manifest.OptionsVersion, (priorOptions?.Revision ?? 0) + 1, prepared.Candidate.Options,
                    priorOptions?.RetainedLegacyValues ?? new Dictionary<string, string>());
                _revision++; WriteState(); TryRemoveJournal(); _previewCandidate = null;
                foreach (Guid id in _candidates.Keys.Where(id => id != command.CandidateId).ToArray()) RetireCandidate(id);
                return ValueTask.FromResult(XsrResult.Success());
            }
            catch (Exception error) when (Recoverable(error))
            {
                _selection = oldSelection; _lastKnownGood = oldGood; _revision = oldRevision;
                if (priorInstallation is null) _installed.Remove(prepared.Installation.Identity); else _installed[prepared.Installation.Identity] = priorInstallation;
                if (priorOptions is null) _options.Remove(prepared.Installation.Identity); else _options[prepared.Installation.Identity] = priorOptions;
                return ValueTask.FromResult(Reject(error.Message));
            }
        }
    }

    public ValueTask<XsrResult> RevertAsync(ThemeRevertCommand command, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate)
        {
            if (!_candidates.TryGetValue(command.CandidateId, out var prepared)) return ValueTask.FromResult(Reject("Candidate has already been retired."));
            if (prepared.Candidate.Generation == _selection?.Generation) return ValueTask.FromResult(Reject("A committed theme must be replaced before retiring its resources."));
            RetireCandidate(command.CandidateId);
            try { _revision++; WriteState(); if (_candidates.Count == 0) TryRemoveJournal(); return ValueTask.FromResult(XsrResult.Success()); }
            catch (Exception error) when (Recoverable(error)) { return ValueTask.FromResult(Reject(error.Message)); }
        }
    }
    public ValueTask<XsrResult> RemoveAsync(ThemeRemoveCommand command, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate)
        {
            if (_disposed || !_installed.TryGetValue(command.SourceIdentity, out var installation) || installation.Kind == ThemeSourceKind.BuiltIn) return ValueTask.FromResult(Reject("This theme cannot be removed."));
            if (_selection?.SourceIdentity == command.SourceIdentity) return ValueTask.FromResult(Reject("Activate another theme before removing the current theme."));
            var previousGrants = _grants.Values.Where(g => g.SourceIdentity == command.SourceIdentity).ToArray(); long previousRevision = _revision;
            try
            {
                _installed.Remove(command.SourceIdentity); foreach (var grant in _grants.Values.Where(g => g.SourceIdentity == command.SourceIdentity).ToArray()) _grants.Remove(grant.GrantId);
                foreach (Guid id in _candidates.Where(c => c.Value.Installation.Identity == command.SourceIdentity).Select(c => c.Key).ToArray()) RetireCandidate(id);
                RetireSourceResources(command.SourceIdentity); StopWatch(command.SourceIdentity); _revision++; WriteState();
                // Option history deliberately remains, while deleted source identity and grants cannot be claimed by a later import.
                DeleteDirectory(Path.Combine(_root, "sources", command.SourceIdentity)); return ValueTask.FromResult(XsrResult.Success());
            }
            catch (Exception error) when (Recoverable(error))
            {
                _installed[command.SourceIdentity] = installation; foreach (var grant in previousGrants) _grants[grant.GrantId] = grant; _revision = previousRevision;
                return ValueTask.FromResult(Reject(error.Message));
            }
        }
    }

    private Dictionary<string, ThemeOptionValue> ValuesFor(Installation installation)
    {
        var values = installation.Manifest.Options.ToDictionary(o => o.Id, o => o.Default, StringComparer.Ordinal);
        if (_options.TryGetValue(installation.Identity, out var stored)) foreach (var option in installation.Manifest.Options)
        {
            if (stored.Values.TryGetValue(option.Id, out var value))
            { try { ThemeManifestReader.ValidateValue(option, value); values[option.Id] = value; } catch (InvalidDataException) { } }
        }
        return values;
    }
    private void EnsureBuiltin()
    {
        if (_builtin is null || _installed.ContainsKey("builtin")) return;
        var admitted = ThemeSourceReader.Validate(_builtin, _capabilities); _installed.Add("builtin", new("builtin", _builtin, admitted.Revision, ThemeSourceKind.BuiltIn, _builtin, admitted.Manifest));
    }
    private void RetireCandidate(Guid id)
    {
        if (!_candidates.Remove(id, out var candidate)) return; RetireGeneration(candidate.Candidate.Generation);
        if (_previewCandidate == id) _previewCandidate = null;
        if (candidate.Installation.Kind != ThemeSourceKind.BuiltIn) DeleteUnreferencedSource(candidate.Installation.Root);
        string identity = candidate.Installation.Identity;
        if (!_installed.ContainsKey(identity) && !_candidates.Values.Any(prepared => prepared.Installation.Identity == identity))
        {
            StopWatch(identity);
            _options.Remove(identity); foreach (var grant in _grants.Values.Where(grant => grant.SourceIdentity == identity).ToArray()) _grants.Remove(grant.GrantId);
        }
    }
    private Installation? FindSource(string identity)
    {
        if (_previewCandidate is { } preview && _candidates.TryGetValue(preview, out var activePreview) && activePreview.Installation.Identity == identity) return activePreview.Installation;
        return _installed.TryGetValue(identity, out var installed) ? installed : _candidates.Values.Where(candidate => candidate.Installation.Identity == identity).OrderByDescending(candidate => candidate.Candidate.Generation).FirstOrDefault()?.Installation;
    }
    private void DeleteUnreferencedSource(string path)
    {
        if (_installed.Values.Any(installation => SamePath(installation.Root, path)) || _candidates.Values.Any(candidate => SamePath(candidate.Installation.Root, path))) return;
        if (path.StartsWith(Path.Combine(_root, "sources") + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) DeleteDirectory(path);
    }
    private void CleanupOwnedSource(string path)
    {
        // A synchronous scope preserves Monitor.Exit when the async finally is fully trimmed.
        lock (_gate) DeleteUnreferencedSource(path);
    }
    private void CheckLive() { ObjectDisposedException.ThrowIf(_disposed, this); }
    private static bool SamePath(string left, string right) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Recoverable(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or System.Xml.XmlException or ArgumentException or InvalidOperationException or OverflowException or KeyNotFoundException;
    private static XsrResult Reject(string message) => XsrResult.Failure(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("themes.rejected"), message));
    private static XsrResult<T> Reject<T>(string message) => XsrResult.Failure<T>(new(XsrErrorKind.Rejected, XsrSemanticId.Parse("themes.rejected"), message));
    private static void DeleteDirectory(string path) { try { if (Directory.Exists(path)) { ThemePaths.NoLinks(path); Directory.Delete(path, true); } } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            foreach (string identity in _watches.Keys.ToArray()) StopWatch(identity);
            foreach (Guid lease in _leases.Keys.ToArray()) ReleaseLease(lease); _candidates.Clear();
        }
        GC.SuppressFinalize(this);
    }
}
