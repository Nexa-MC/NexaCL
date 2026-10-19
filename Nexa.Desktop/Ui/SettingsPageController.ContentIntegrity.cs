using System.Globalization;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private InstanceContentIntegrityQuery? _contentIntegrityQuery;
    private InstanceContentIntegrity? _contentIntegrity;
    private Task<XsrResult<InstanceContentIntegrity>>? _contentIntegrityRead;
    private CancellationTokenSource? _contentIntegrityStop;
    private string? _contentIntegrityError, _contentIntegritySourceCaption;
    private XsrUiEntityId _contentIntegritySection, _contentIntegritySource;

    private void BuildContentTrustStates()
    {
        if (_instanceDirectory is not null || _selected != "privacy") return;
        AppendContent(_sections, "SettingsContentTrust.d.nxml", new());
    }

    private void CancelContentIntegrity()
    {
        ObserveRetiredInstanceToolRead(_contentIntegrityRead);
        if (_shell.Tree.IsAlive(_contentIntegritySection)) RemoveContentView(_contentIntegritySection);
        _contentIntegrityStop?.Cancel(); _contentIntegrityStop?.Dispose(); _contentIntegrityStop = null;
        _contentIntegrityRead = null; _contentIntegrityQuery = null; _contentIntegrity = null;
        _contentIntegrityError = _contentIntegritySourceCaption = null; _contentIntegritySection = _contentIntegritySource = default;
    }

    private InstanceContentIntegrityQuery? ContentIntegrityFile(InstanceContentEntry item) => _instance is not null && !item.IsDirectory && item.Size is { } size
        && _selected is "mods" or "resourcepacks" or "shaderpacks" or "screenshots" or "schematics"
        ? new(_instance, _selected, item.Name, size, item.ModifiedUtcTicks) : null;

    private void BuildContentIntegrity(XsrUiEntityId parent, InstanceContentEntry item)
    {
        if (ContentIntegrityFile(item) is not { } query) { CancelContentIntegrity(); return; }
        if (query != _contentIntegrityQuery)
        {
            CancelContentIntegrity(); _contentIntegrityQuery = query;
            if (_queries.TryResolve(InstanceContentIntegrityContract.Read, out var route))
            {
                _contentIntegrityStop = new();
                _contentIntegrityRead = _queries.QueryAsync<InstanceContentIntegrityQuery, InstanceContentIntegrity>(route, query, cancellationToken: _contentIntegrityStop.Token).AsTask();
                WakeOnPlatformCompletion(_contentIntegrityRead);
            }
            else _contentIntegrityError = ContentTerm("copy.578d8df0b4f5");
        }
        if (_shell.Tree.IsAlive(_contentIntegritySection) && _shell.Tree.Parent(_contentIntegritySection) == parent) RenderContentIntegrity();
        else _contentIntegritySection = AppendContent(parent, "SettingsContentIntegrity.d.nxml", ContentIntegrityProjection());
        var queryIdentity = query; BindContentAction(ContentNodes(_contentIntegritySection), "ContentIntegrity.Refresh", () => { if (queryIdentity != _contentIntegrityQuery) return; CancelContentIntegrity(); BuildSections(true); });
        _contentIntegritySource = ContentNodes(_contentIntegritySection)["ContentIntegrity.Source.Value"];
    }

    private void UpdateContentIntegrity()
    {
        if (_contentIntegrityQuery is not { } query) return;
        if (_contentDetail is not { } item || ContentIntegrityFile(item) != query) { CancelContentIntegrity(); return; }
        if (_contentIntegrityRead is { IsCompleted: true } read)
        {
            _contentIntegrityRead = null; _contentIntegrityStop?.Dispose(); _contentIntegrityStop = null;
            if (PendingQuery.Succeeded(read) && read.Result.Value!.File == query) _contentIntegrity = read.Result.Value;
            else _contentIntegrityError = ContentTerm("copy.3f2a49f6ac40");
            if (_contentIntegritySection.IsAssigned && _shell.Tree.IsAlive(_contentIntegritySection)) RenderContentIntegrity();
        }
        UpdateContentIntegritySource();
    }

    private void RenderContentIntegrity() => ReloadContent(_contentIntegritySection, ContentIntegrityProjection().Select(pair => (pair.Key, pair.Value)).ToArray());

    private Dictionary<string, string> ContentIntegrityProjection()
    {
        var fact = _contentIntegrity; _contentIntegritySourceCaption = ContentIntegritySourceCaption();
        return new()
        {
            ["enabled"] = ContentFlag(_managementWrite is null),
            ["message"] = _contentIntegrityError ?? ContentTerm("integrityLoading"),
            ["loading"] = ContentFlag(_contentIntegrityRead is not null),
            ["failed"] = ContentFlag(_contentIntegrityError is not null),
            ["pending"] = ContentFlag(fact is null),
            ["ready"] = ContentFlag(fact is not null),
            ["Sha256"] = fact?.Sha256 ?? "",
            ["Captured"] = fact?.CapturedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.CurrentCulture) ?? "",
            ["Modified"] = fact is null ? "" : ContentTerm(fact.Modified switch { false => "baselineSame", true => "baselineDifferent", _ => fact.BaselineState == InstanceContentBaselineState.Incomplete ? "baselineIncomplete" : "baselineUnknown" }),
            ["Baseline"] = fact?.BaselineSha256 ?? "",
            ["hasBaseline"] = ContentFlag(fact?.BaselineSha256 is not null),
            ["Transaction"] = fact?.BaselineTransactionId?.ToString("N") ?? "",
            ["hasTransaction"] = ContentFlag(fact?.BaselineTransactionId is not null),
            ["Source"] = _contentIntegritySourceCaption
        };
    }

    private string ContentIntegritySourceCaption()
    {
        if (_contentIntegrity is not { } fact || _contentIntegrityQuery is not { } query) return "";
        var online = _onlineQuery is { } current && current.InstanceDirectory == query.InstanceDirectory && current.PageId == query.PageId
            && current.Name == query.Name && current.ExpectedSize == query.ExpectedSize && current.ExpectedModifiedUtcTicks == query.ExpectedModifiedUtcTicks ? _onlineContent : null;
        if (online is null && _contentDetail is { } item && OnlineFile(item) is { } file) _onlineList.TryGetValue(file, out online);
        var matches = online?.InstalledFiles.Where(match => match.FileName == query.Name && match.Sha512.Equals(fact.Sha512, StringComparison.OrdinalIgnoreCase))
            .Select(match => match.Source.Provider + ":" + match.Source.ProjectId + "@" + match.VersionId).Distinct().Take(8).ToArray();
        return matches is { Length: > 0 } ? string.Join(ContentTerm("sourceSeparator"), matches) : ContentTerm("copy.9ec8f0f1cb3c");
    }

    private void UpdateContentIntegritySource()
    {
        if (_contentIntegrity is null || !_contentIntegritySource.IsAssigned || !_shell.Tree.IsAlive(_contentIntegritySource)) return;
        string caption = ContentIntegritySourceCaption(); if (_contentIntegritySourceCaption == caption) return;
        _contentIntegritySourceCaption = caption; ReloadContent(_contentIntegritySection, ("Source", caption));
    }
}
