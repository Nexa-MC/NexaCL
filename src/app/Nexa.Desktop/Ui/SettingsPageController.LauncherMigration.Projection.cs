using Nexa.Services.Setup;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly Lazy<Dictionary<string, string>> LauncherMigrationTerms = new(() =>
    {
        var terms = DesktopMarkup.Load("SettingsLauncherMigrationTerms.nxml").Root.Children;
        if (terms.Any(node => node.Recipe != NxmlRuntimeRecipe.Text || string.IsNullOrWhiteSpace(node.Key)
            || node.Content is null || node.Bindings.Count != 0 || node.Children.Count != 0))
            throw new NxmlCompileException("Launcher migration terms must be keyed literal text.");
        return terms.ToDictionary(node => node.Key!, node => node.Content!, StringComparer.Ordinal);
    });
    private static string MigrationRaw(string key) => LauncherMigrationTerms.Value[key];
    private string MigrationCopy(string key) => _shell.Renderer.LocalizeText(MigrationRaw(key));
    private string MigrationFormat(string name, params (string Key, string Value)[] values)
        => DesktopMarkup.Dynamic("SettingsLauncherMigration" + name + ".d.nxml", values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal), _shell.Renderer.LocalizeText).Root.Content!;

    private Dictionary<string, string> LauncherMigrationProjection() => new()
    {
        ["enabled"] = Flag(!LauncherMigrationBusy && _launcherMigrationCleanupReceipt is null),
        ["busy"] = Flag(LauncherMigrationBusy),
        ["loading"] = Flag(LauncherMigrationLoading),
        ["sourceKind"] = MigrationCopy("source." + _launcherMigrationKind),
        ["source"] = _launcherMigrationSource ?? MigrationCopy("sourceEmpty"),
        ["destination"] = _launcherMigrationDestination ?? MigrationCopy("destinationEmpty"),
        ["discoverEnabled"] = Flag(!LauncherMigrationBusy && _launcherMigrationCleanupReceipt is null
            && _queries.TryResolve(LauncherMigrationContract.Discover, out _)),
        ["previewEnabled"] = Flag(!LauncherMigrationBusy && _launcherMigrationCleanupReceipt is null && _launcherMigrationSource is not null && _launcherMigrationDestination is not null
            && _queries.TryResolve(LauncherMigrationContract.Preview, out _)),
        ["applyEnabled"] = Flag(!LauncherMigrationBusy && _launcherMigrationCleanupReceipt is null && _launcherMigrationPreview is not null && _launcherMigrationSelected.Count > 0
            && _commands.TryResolve(LauncherMigrationContract.Apply, out _)),
        ["hasReceipt"] = Flag(_launcherMigrationReceipt is not null && _launcherMigrationCleanupReceipt is null),
        ["hasCleanup"] = Flag(_launcherMigrationCleanupReceipt is not null),
        ["cleanupEnabled"] = Flag(!LauncherMigrationBusy && _launcherMigrationCleanupReceipt is not null),
        ["registerEnabled"] = Flag(!LauncherMigrationBusy && _launcherMigrationReceipt is not null && _launcherMigrationLibrary is not null),
        ["undoEnabled"] = Flag(!LauncherMigrationBusy && _launcherMigrationReceipt is not null && _commands.TryResolve(LauncherMigrationContract.Undo, out _)),
        ["recoverEnabled"] = Flag(!LauncherMigrationBusy && _launcherMigrationCleanupReceipt is null && _queries.TryResolve(LauncherMigrationContract.ReadReceipt, out _)),
    };

    private void RefreshLauncherMigrationPresentation()
    {
        if (!_shell.Tree.IsAlive(_launcherMigrationPanel) || _instanceDirectory is not null || _selected != "storage") return;
        _launcherMigrationPresentationDirty = false;
        _dynamic.Reload(_launcherMigrationPanel, DesktopMarkup.Dynamic("SettingsLauncherMigration.d.nxml", LauncherMigrationProjection()));
        _launcherMigrationActions.Clear();
        var nodes = CatalogNodes(_launcherMigrationPanel);
        foreach (string kind in new[] { "kind", "discover", "source", "destination", "preview", "apply", "recover", "register", "undo", "cancel", "cleanup" })
            _launcherMigrationActions[nodes["LauncherMigration." + kind]] = (kind, null);
        RefreshLauncherMigrationSources(nodes["LauncherMigration.Sources"]);
        var instances = _launcherMigrationPreview?.Instances ?? [];
        var live = instances.Select(instance => instance.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string identity in _launcherMigrationRows.Keys.Where(identity => !live.Contains(identity)).ToArray())
        { _dynamic.Remove(_launcherMigrationRows[identity]); _launcherMigrationRows.Remove(identity); }
        foreach (var instance in instances)
        {
            string reason = instance.CanImport ? MigrationCopy("importable") : instance.UnsupportedReason ?? MigrationCopy("unsupported");
            string detail = MigrationFormat("InstanceDetail", ("version", instance.GameVersion),
                ("loader", instance.Loader is null ? MigrationCopy("vanilla") : instance.Loader + " " + instance.LoaderBuild),
                ("files", instance.Files.ToString(System.Globalization.CultureInfo.CurrentCulture)),
                ("size", (instance.Bytes / (1024d * 1024d)).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)), ("status", reason));
            var fragment = DesktopMarkup.Dynamic("SettingsLauncherMigrationInstance.d.nxml", new Dictionary<string, string>
            {
                ["key"] = "LauncherMigration.Instance." + instance.Id,
                ["select"] = "LauncherMigration.Select." + instance.Id,
                ["name"] = instance.Name,
                ["selected"] = Flag(_launcherMigrationSelected.Contains(instance.Id)),
                ["enabled"] = Flag(!LauncherMigrationBusy && instance.CanImport),
                ["detail"] = detail,
                ["warnings"] = string.Join('\n', instance.Warnings),
                ["hasWarnings"] = Flag(instance.Warnings.Count > 0),
            });
            if (_launcherMigrationRows.TryGetValue(instance.Id, out var row)) _dynamic.Reload(row, fragment);
            else _launcherMigrationRows[instance.Id] = row = _dynamic.Append(nodes["LauncherMigration.Instances"], fragment);
            _launcherMigrationActions[CatalogNodes(row)["LauncherMigration.Select." + instance.Id]] = ("select", instance.Id);
        }
        _dynamic.Reorder(nodes["LauncherMigration.Instances"], instances.Select(instance => _launcherMigrationRows[instance.Id]).ToArray());
        string status = LauncherMigrationLoading ? MigrationCopy("working") : _launcherMigrationMessage ?? MigrationCopy("initial");
        if (_launcherMigrationPreview is not null)
            status += "\n" + MigrationFormat("Selection", ("selected", _launcherMigrationSelected.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)),
                ("total", instances.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)),
                ("size", (instances.Where(instance => _launcherMigrationSelected.Contains(instance.Id)).Sum(instance => instance.Bytes) / (1024d * 1024d)).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)));
        _launcherMigrationStatus = ReloadMigrationText(_launcherMigrationStatus, nodes["LauncherMigration.Status"], status);
        string warnings = string.Join('\n', (_launcherMigrationDiscovery?.Warnings ?? []).Concat(_launcherMigrationPreview?.Warnings ?? []).Distinct(StringComparer.Ordinal));
        _launcherMigrationWarnings = ReloadMigrationText(_launcherMigrationWarnings, nodes["LauncherMigration.Warnings"], warnings);
    }

    private void RefreshLauncherMigrationSources(XsrUiEntityId slot)
    {
        var sources = _launcherMigrationDiscovery?.Sources ?? [];
        foreach (int index in _launcherMigrationSourceRows.Keys.Where(index => index >= sources.Count).ToArray())
        { _dynamic.Remove(_launcherMigrationSourceRows[index]); _launcherMigrationSourceRows.Remove(index); }
        for (int index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            string identity = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var fragment = DesktopMarkup.Dynamic("SettingsLauncherMigrationSource.d.nxml", new Dictionary<string, string>
            {
                ["key"] = "LauncherMigration.DetectedSource." + identity,
                ["choose"] = "LauncherMigration.Detected." + identity,
                ["name"] = source.Name,
                ["directory"] = source.Directory,
                ["selected"] = Flag(source.SourceKind == _launcherMigrationKind && Nexa.Core.PathIdentity.Comparer.Equals(source.Directory, _launcherMigrationSource)),
                ["enabled"] = Flag(!LauncherMigrationBusy && _launcherMigrationCleanupReceipt is null),
            });
            if (_launcherMigrationSourceRows.TryGetValue(index, out var row)) _dynamic.Reload(row, fragment);
            else _launcherMigrationSourceRows[index] = row = _dynamic.Append(slot, fragment);
            _launcherMigrationActions[CatalogNodes(row)["LauncherMigration.Detected." + identity]] = ("detected", identity);
        }
        _dynamic.Reorder(slot, Enumerable.Range(0, sources.Count).Select(index => _launcherMigrationSourceRows[index]).ToArray());
    }

    private XsrUiEntityId ReloadMigrationText(XsrUiEntityId entity, XsrUiEntityId slot, string text)
    {
        var fragment = DesktopMarkup.Dynamic("SettingsLauncherMigrationText.d.nxml", new Dictionary<string, string>
        { ["text"] = text, ["visible"] = Flag(text.Length > 0) });
        if (_shell.Tree.IsAlive(entity)) { _dynamic.Reload(entity, fragment); return entity; }
        return _dynamic.Append(slot, fragment);
    }
}
