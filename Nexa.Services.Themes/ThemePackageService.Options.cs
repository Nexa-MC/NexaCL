using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Nexa.Xsr;

namespace Nexa.Services.Themes;

public sealed partial class ThemePackageService
{
    private static readonly (string Key, ThemeResourceKind Kind)[] LegacyResourceKeys =
    [
        ("appearance.logo-path", ThemeResourceKind.Image), ("appearance.background-path", ThemeResourceKind.Image),
        ("appearance.video-path", ThemeResourceKind.Video), ("music.path", ThemeResourceKind.Audio)
    ];
    public ValueTask<XsrResult> SaveOptionsAsync(ThemeOptionsSaveCommand command, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate)
        {
            var source = FindSource(command.SourceIdentity);
            if (_disposed || command.ExpectedRevision != _revision || source is null) return ValueTask.FromResult(Reject("Theme option source or revision is stale."));
            try
            {
                if (command.Values.Count > 256) return ValueTask.FromResult(Reject("Too many option values."));
                var values = ValuesFor(source);
                foreach (var pair in command.Values)
                {
                    var definition = source.Manifest.Options.SingleOrDefault(o => o.Id == pair.Key) ?? throw new InvalidDataException("Unknown option ID.");
                    ThemeManifestReader.ValidateValue(definition, pair.Value); values[pair.Key] = pair.Value with { Values = pair.Value.Values?.ToArray() };
                }
                ThemeManifestReader.ValidateAppearance(source.Manifest.Appearance, values);
                _options.TryGetValue(command.SourceIdentity, out var old); long revision = _revision;
                _options[command.SourceIdentity] = new(command.SourceIdentity, source.Manifest.OptionsVersion, (old?.Revision ?? 0) + 1,
                    new ReadOnlyDictionary<string, ThemeOptionValue>(values), old?.RetainedLegacyValues ?? new Dictionary<string, string>());
                _revision++;
                try { WriteState(); } catch { _revision = revision; if (old is null) _options.Remove(command.SourceIdentity); else _options[command.SourceIdentity] = old; throw; }
                return ValueTask.FromResult(XsrResult.Success());
            }
            catch (Exception error) when (Recoverable(error)) { return ValueTask.FromResult(Reject(error.Message)); }
        }
    }
    public ValueTask<XsrResult<ThemeOptionsExport>> ExportOptionsAsync(ThemeOptionsExportQuery query, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate)
        {
            var source = FindSource(query.SourceIdentity);
            if (_disposed || source is null) return ValueTask.FromResult(Reject<ThemeOptionsExport>("Unknown option source."));
            using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject(); writer.WriteString("format", "NTP-options/1"); writer.WriteString("themeId", source.Manifest.Id); writer.WriteNumber("schemaVersion", source.Manifest.OptionsVersion); WriteValues(writer, ValuesFor(source)); writer.WriteEndObject();
            }
            return ValueTask.FromResult(XsrResult.Success(new ThemeOptionsExport(Encoding.UTF8.GetString(stream.ToArray()))));
        }
    }
    public ValueTask<XsrResult> ImportOptionsAsync(ThemeOptionsImportCommand command, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate)
        {
            try
            {
                var source = FindSource(command.SourceIdentity); if (source is null) return ValueTask.FromResult(Reject("Invalid options import source."));
                return SaveOptionsAsync(new(command.SourceIdentity, ReadOptionsDocument(command.Json, source.Manifest), command.ExpectedRevision), token);
            }
            catch (Exception error) when (Recoverable(error)) { return ValueTask.FromResult(Reject(error.Message)); }
        }
    }
    private static Dictionary<string, ThemeOptionValue> ReadOptionsDocument(string json, ThemeManifest manifest)
    {
        if (json.Length > 1024 * 1024) throw new InvalidDataException("Options import exceeds its budget.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 }); var node = document.RootElement;
        ThemeManifestReader.Fields(node, "format", "themeId", "schemaVersion", "values");
        if (ThemeManifestReader.Text(node, "format") != "NTP-options/1" || ThemeManifestReader.Text(node, "themeId") != manifest.Id || node.GetProperty("schemaVersion").GetInt32() != manifest.OptionsVersion)
            throw new InvalidDataException("Options import theme or schema does not match.");
        return ReadValues(node.GetProperty("values"));
    }
    public ValueTask<XsrResult> ResetOptionsAsync(ThemeOptionsResetCommand command, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate)
        {
            var source = FindSource(command.SourceIdentity); if (source is null) return ValueTask.FromResult(Reject("Unknown option source."));
            return SaveOptionsAsync(new(command.SourceIdentity, source.Manifest.Options.ToDictionary(o => o.Id, o => o.Default, StringComparer.Ordinal), command.ExpectedRevision), token);
        }
    }
    public ValueTask<XsrResult> MigrateAsync(ThemeLegacyMigrationCommand command, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate)
        {
            if (_disposed) return ValueTask.FromResult(Reject("Theme service is retired.")); if (_migrated) return ValueTask.FromResult(XsrResult.Success());
            _options.TryGetValue("builtin", out var previousOptions); long previousRevision = _revision; var migratedGrants = new List<Guid>();
            try
            {
                EnsureBuiltin(); if (!_installed.TryGetValue("builtin", out var source)) return ValueTask.FromResult(Reject("Built-in theme must be available before appearance migration."));
                if (command.Values.Count > 256 || command.Values.Any(v => v.Key.Length > 128 || v.Value.Length > 4096)) return ValueTask.FromResult(Reject("Legacy appearance values exceed their budget."));
                var values = ValuesFor(source); var retained = new Dictionary<string, string>(command.Values, StringComparer.Ordinal);
                foreach (var legacyResource in LegacyResourceKeys)
                {
                    if (!command.Values.TryGetValue(legacyResource.Key, out string? raw)) continue;
                    foreach (string path in raw.Split(['\n', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        MigrateResourceGrant(path, legacyResource.Kind, legacyResource.Kind == ThemeResourceKind.Audio, migratedGrants);
                }
                foreach (var option in source.Manifest.Options)
                {
                    if (!command.Values.TryGetValue(option.Id, out string? raw)) continue;
                    var value = option.Kind == ThemeOptionKind.Files ? new ThemeOptionValue(option.Kind, "", raw.Split(['\n', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) : new(option.Kind, raw);
                    if (option.ResourceKind is { } resourceKind && option.Id is "appearance.logo-path" or "appearance.background-path" or "appearance.video-path" or "music.path")
                    {
                        if (option.Kind == ThemeOptionKind.Files)
                        {
                            var valid = (value.Values ?? []).Where(path => MigrateResourceGrant(path, resourceKind, directoryAllowed: resourceKind == ThemeResourceKind.Audio, migratedGrants)).ToArray();
                            value = value with { Values = valid };
                        }
                        else if (!MigrateResourceGrant(value.Value, resourceKind, option.Kind == ThemeOptionKind.Directory, migratedGrants)) continue;
                    }
                    if (option.Kind == ThemeOptionKind.Boolean && bool.TryParse(raw, out bool boolean)) value = value with { Value = boolean ? "true" : "false" };
                    if (option.Kind is ThemeOptionKind.Number or ThemeOptionKind.Range && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) value = value with { Value = number.ToString(CultureInfo.InvariantCulture) };
                    try { ThemeManifestReader.ValidateValue(option, value); values[option.Id] = value; } catch (InvalidDataException) { }
                }
                _options["builtin"] = new("builtin", source.Manifest.OptionsVersion, 1, new ReadOnlyDictionary<string, ThemeOptionValue>(values), new ReadOnlyDictionary<string, string>(retained));
                _migrated = true; _revision++; WriteState(); return ValueTask.FromResult(XsrResult.Success());
            }
            catch (Exception error) when (Recoverable(error))
            {
                if (previousOptions is null) _options.Remove("builtin"); else _options["builtin"] = previousOptions;
                foreach (Guid grant in migratedGrants) _grants.Remove(grant);
                _migrated = false; _revision = previousRevision; return ValueTask.FromResult(Reject(error.Message));
            }
        }
    }
    private bool MigrateResourceGrant(string selected, ThemeResourceKind kind, bool directoryAllowed, List<Guid> migrated)
    {
        if (string.IsNullOrWhiteSpace(selected)) return false;
        try
        {
            string path = Path.GetFullPath(selected); ThemePaths.NoLinks(path); bool directory = Directory.Exists(path);
            if (directory ? !directoryAllowed : !File.Exists(path)) return false;
            if (!directory)
            {
                ContentType(path, kind); long limit = kind is ThemeResourceKind.Audio or ThemeResourceKind.Video ? ThemeSourceReader.EntryLimit : 16 * 1024 * 1024;
                if (new FileInfo(path).Length > limit) return false;
            }
            var scope = directory ? ThemeGrantScope.Directory : ThemeGrantScope.File;
            if (_grants.Values.Any(grant => grant.SourceIdentity == "builtin" && grant.Kind == kind && grant.Scope == scope && SamePath(grant.CanonicalPath, path))) return true;
            if (_grants.Count >= 1024) return false;
            var grant = new ThemeResourceGrant(Guid.NewGuid(), "builtin", kind, scope, path, _revision + 1); _grants.Add(grant.GrantId, grant); migrated.Add(grant.GrantId); return true;
        }
        catch (Exception error) when (Recoverable(error)) { return false; }
    }
}
