using System.Buffers;
using System.Text.Json;

namespace Nexa.Services.Themes;

public sealed partial class ThemePackageService
{
    private string StatePath => Path.Combine(_root, "state.json");
    private string JournalPath => Path.Combine(_root, "candidate.json");
    private static readonly SearchValues<char> IdentityCharacters = SearchValues.Create("0123456789abcdef");

    private void WriteState() => AtomicWrite(StatePath, writer =>
    {
        writer.WriteStartObject(); writer.WriteNumber("version", 1); writer.WriteNumber("revision", _revision); writer.WriteNumber("generation", _generation); writer.WriteBoolean("migrated", _migrated);
        Selection(writer, "selection", _selection); Selection(writer, "lastKnownGood", _lastKnownGood);
        writer.WriteStartArray("installations");
        foreach (var item in _installed.Values)
        {
            writer.WriteStartObject(); writer.WriteString("identity", item.Identity); writer.WriteString("root", item.Root); writer.WriteString("revision", item.Revision); writer.WriteString("kind", item.Kind.ToString()); writer.WriteString("origin", item.Origin); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteStartArray("pendingSources");
        foreach (string identity in _candidates.Values.Select(candidate => candidate.Installation.Identity).Where(identity => !_installed.ContainsKey(identity)).Distinct(StringComparer.Ordinal)) writer.WriteStringValue(identity);
        writer.WriteEndArray(); writer.WriteStartArray("options");
        foreach (var state in _options.Values)
        {
            writer.WriteStartObject(); writer.WriteString("identity", state.SourceIdentity); writer.WriteNumber("schema", state.SchemaVersion); writer.WriteNumber("revision", state.Revision); WriteValues(writer, state.Values);
            writer.WriteStartObject("legacy"); foreach (var legacy in state.RetainedLegacyValues) writer.WriteString(legacy.Key, legacy.Value); writer.WriteEndObject(); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteStartArray("grants");
        foreach (var grant in _grants.Values)
        {
            writer.WriteStartObject(); writer.WriteString("id", grant.GrantId); writer.WriteString("identity", grant.SourceIdentity); writer.WriteString("kind", grant.Kind.ToString()); writer.WriteString("scope", grant.Scope.ToString()); writer.WriteString("path", grant.CanonicalPath); writer.WriteNumber("revision", grant.Revision); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteEndObject();
    });

    private void ReadState()
    {
        if (!File.Exists(StatePath)) return;
        try
        {
            using var document = JsonDocument.Parse(ThemeSourceReader.ReadBounded(StatePath, 4 * 1024 * 1024), new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement; if (root.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("Unsupported theme state version.");
            _revision = root.GetProperty("revision").GetInt64(); _generation = root.GetProperty("generation").GetInt64(); _migrated = root.GetProperty("migrated").GetBoolean();
            if (_revision < 0 || _generation < 0) throw new InvalidDataException("Invalid theme state revision.");
            var installs = root.GetProperty("installations"); ThemeManifestReader.Array(installs, 256);
            foreach (var item in installs.EnumerateArray())
            {
                string identity = ThemeManifestReader.Text(item, "identity"), path = ThemePaths.Canonical(ThemeManifestReader.Text(item, "root")), revision = ThemeManifestReader.Text(item, "revision");
                if (!ValidIdentity(identity) || !Enum.TryParse<ThemeSourceKind>(ThemeManifestReader.Text(item, "kind"), out var kind) || !Enum.IsDefined(kind)) throw new InvalidDataException("Invalid stored theme source.");
                string? origin = item.GetProperty("origin").ValueKind == JsonValueKind.Null ? null : ThemePaths.Canonical(ThemeManifestReader.Text(item, "origin"));
                if (kind == ThemeSourceKind.BuiltIn) { if (_builtin is null || !SamePath(path, _builtin)) continue; }
                else if (!SamePath(path, Path.Combine(_root, "sources", identity, revision))) throw new InvalidDataException("Stored theme root escapes owned sources.");
                if (revision.Length != 64 || !revision.All(char.IsAsciiHexDigit)) throw new InvalidDataException("Invalid source revision.");
                if (!Directory.Exists(path)) continue; ThemePaths.NoLinks(path);
                var manifest = ThemeManifestReader.Read(ThemeSourceReader.ReadBounded(ThemePaths.Within(path, "theme.json"), ThemeManifestReader.ManifestLimit));
                _installed.Add(identity, new(identity, path, revision, kind, origin, manifest));
            }
            var options = root.GetProperty("options"); ThemeManifestReader.Array(options, 512);
            foreach (var item in options.EnumerateArray())
            {
                string identity = ThemeManifestReader.Text(item, "identity"); if (!ValidIdentity(identity)) throw new InvalidDataException("Invalid option source identity.");
                var values = ReadValues(item.GetProperty("values")); var legacy = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in item.GetProperty("legacy").EnumerateObject()) { if (legacy.Count >= 256) throw new InvalidDataException("Legacy option budget exceeded."); legacy.Add(property.Name, ThemeManifestReader.String(property.Value)); }
                _options.Add(identity, new(identity, item.GetProperty("schema").GetInt32(), item.GetProperty("revision").GetInt64(), new System.Collections.ObjectModel.ReadOnlyDictionary<string, ThemeOptionValue>(values), new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(legacy)));
            }
            var grants = root.GetProperty("grants"); ThemeManifestReader.Array(grants, 1024);
            foreach (var item in grants.EnumerateArray())
            {
                string identity = ThemeManifestReader.Text(item, "identity"); if (!_installed.ContainsKey(identity)) continue;
                string path = ThemePaths.Canonical(ThemeManifestReader.Text(item, "path"));
                if (!Enum.TryParse<ThemeResourceKind>(ThemeManifestReader.Text(item, "kind"), out var kind) || !Enum.IsDefined(kind)
                    || !Enum.TryParse<ThemeGrantScope>(ThemeManifestReader.Text(item, "scope"), out var scope) || !Enum.IsDefined(scope)) throw new InvalidDataException("Invalid stored grant.");
                Guid id = item.GetProperty("id").GetGuid(); _grants.Add(id, new(id, identity, kind, scope, path, item.GetProperty("revision").GetInt64()));
            }
            _selection = ReadSelection(root.GetProperty("selection")); _lastKnownGood = ReadSelection(root.GetProperty("lastKnownGood"));
            if (root.TryGetProperty("pendingSources", out var pending))
            {
                foreach (string identity in ThemeManifestReader.Strings(pending, 8)) { if (!ValidIdentity(identity)) throw new InvalidDataException("Invalid pending source identity."); _recoverPendingSources.Add(identity); }
            }
        }
        catch (Exception error) when (Recoverable(error))
        {
            _installed.Clear(); _options.Clear(); _grants.Clear(); _recoverPendingSources.Clear(); _selection = null; _lastKnownGood = null; _revision = 0; _generation = 0; _migrated = false; _recovered = true;
            // Keep a corrupt state for diagnosis instead of deleting user options.
            string quarantine = StatePath + ".invalid-" + Guid.NewGuid().ToString("N"); File.Move(StatePath, quarantine);
        }
    }

    private void Recover()
    {
        bool pending = false;
        foreach (string identity in _recoverPendingSources)
        {
            if (_installed.ContainsKey(identity)) continue; _options.Remove(identity);
            foreach (var grant in _grants.Values.Where(grant => grant.SourceIdentity == identity).ToArray()) _grants.Remove(grant.GrantId); pending = true;
        }
        _recoverPendingSources.Clear();
        if (File.Exists(JournalPath) || pending) { _recovered = true; _selection = _lastKnownGood; _revision++; WriteState(); TryRemoveJournal(); }
        foreach (string path in Directory.EnumerateDirectories(Path.Combine(_root, "sources"), ".candidate-*")) DeleteDirectory(path);
        foreach (string path in Directory.EnumerateDirectories(Path.Combine(_root, "leases"))) DeleteDirectory(path);
        var admittedRoots = _installed.Values.Select(i => ThemePaths.Canonical(i.Root)).ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string identityRoot in Directory.EnumerateDirectories(Path.Combine(_root, "sources")))
        {
            if (Path.GetFileName(identityRoot).StartsWith('.')) continue;
            ThemePaths.NoLinks(identityRoot);
            foreach (string revisionRoot in Directory.EnumerateDirectories(identityRoot)) if (!admittedRoots.Contains(ThemePaths.Canonical(revisionRoot))) DeleteDirectory(revisionRoot);
        }
    }
    private void WriteJournal(ThemeCandidate candidate) => AtomicWrite(JournalPath, writer =>
    {
        writer.WriteStartObject(); writer.WriteNumber("version", 1); writer.WriteString("candidate", candidate.CandidateId); writer.WriteNumber("generation", candidate.Generation); writer.WriteString("identity", candidate.SourceIdentity); writer.WriteString("revision", candidate.SourceRevision); writer.WriteEndObject();
    });
    private void RemoveJournal() { if (File.Exists(JournalPath)) File.Delete(JournalPath); }
    private void TryRemoveJournal() { try { RemoveJournal(); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private static void AtomicWrite(string path, Action<Utf8JsonWriter> write)
    {
        ThemePaths.NoLinks(path); string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            { using var writer = new Utf8JsonWriter(stream); write(writer); writer.Flush(); if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Theme state exceeds its persistence budget."); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Selection(Utf8JsonWriter writer, string name, ThemeSelection? selection)
    {
        if (selection is null) { writer.WriteNull(name); return; }
        writer.WriteStartObject(name); writer.WriteString("identity", selection.SourceIdentity); writer.WriteString("revision", selection.SourceRevision); writer.WriteString("id", selection.ThemeId); writer.WriteString("version", selection.Version); writer.WriteNumber("generation", selection.Generation); writer.WriteEndObject();
    }
    private ThemeSelection? ReadSelection(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Null) return null;
        string identity = ThemeManifestReader.Text(node, "identity"), revision = ThemeManifestReader.Text(node, "revision");
        if (!_installed.TryGetValue(identity, out var source) || source.Revision != revision) { _recovered = true; return null; }
        return new(identity, revision, ThemeManifestReader.Text(node, "id"), ThemeManifestReader.Text(node, "version"), node.GetProperty("generation").GetInt64());
    }
    private static void WriteValues(Utf8JsonWriter writer, IReadOnlyDictionary<string, ThemeOptionValue> values)
    {
        writer.WriteStartObject("values");
        foreach (var pair in values)
        {
            writer.WriteStartObject(pair.Key); writer.WriteString("kind", pair.Value.Kind.ToString()); writer.WriteString("value", pair.Value.Value);
            if (pair.Value.Values is { } files) { writer.WriteStartArray("files"); foreach (string file in files) writer.WriteStringValue(file); writer.WriteEndArray(); }
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }
    private static Dictionary<string, ThemeOptionValue> ReadValues(JsonElement node)
    {
        ThemeManifestReader.Object(node); var result = new Dictionary<string, ThemeOptionValue>(StringComparer.Ordinal);
        foreach (var property in node.EnumerateObject())
        {
            if (result.Count >= 256 || property.Name.Length is < 1 or > 128) throw new InvalidDataException("Option value budget exceeded.");
            ThemeManifestReader.Fields(property.Value, "kind", "value", "files");
            if (!Enum.TryParse<ThemeOptionKind>(ThemeManifestReader.Text(property.Value, "kind"), out var kind) || !Enum.IsDefined(kind)) throw new InvalidDataException("Invalid stored option kind.");
            var files = property.Value.TryGetProperty("files", out var f) ? ThemeManifestReader.Strings(f, 256).AsReadOnly() : null;
            result.Add(property.Name, new(kind, ThemeManifestReader.Text(property.Value, "value"), files));
        }
        return result;
    }
    private static bool ValidIdentity(string identity) => identity == "builtin" || identity.Length == 39 && identity.StartsWith("source-", StringComparison.Ordinal) && identity.AsSpan(7).IndexOfAnyExcept(IdentityCharacters) < 0;
}
