using Nexa.Services.Resources;
using Nexa.Services.Settings;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class ResourcesPageController
{
    private static readonly string[] SourcePolicyOptions = ["ResourceNetwork.0", "ResourceNetwork.1"];
    private long _sourcePolicyRevision = -1;
    private bool _sourcePolicyForced;
    private string? _sourcePolicyPriority;
    private Task<XsrResult<ResourceNetworkPolicySnapshot>>? _sourcePolicyReading;

    private void SelectSourceFromPage(int index)
    {
        if (_sourcePolicyForced) return;
        _filter = _filter with { MirrorFirst = index == 0 }; Search();
    }

    private void UpdateSourcePolicyNotice()
    {
        if (_disposed || _shell.Stage.Navigation.Current != Page) return;
        long revision = _store.TryResolve(SettingsPolicyContract.RevisionKey, out var key) ? _store.Read<long>(key).Value : 0;
        if (_sourcePolicyReading is null && _sourcePolicyRevision != revision
            && _queries.TryResolve(ResourceCatalogContract.NetworkPolicy, out var route))
        {
            _sourcePolicyRevision = revision;
            _sourcePolicyReading = _queries.QueryAsync<ResourceNetworkPolicyQuery, ResourceNetworkPolicySnapshot>(route, new()).AsTask();
            Wake(_sourcePolicyReading);
        }
        if (_sourcePolicyReading is not { IsCompleted: true } read) return;
        _sourcePolicyReading = null;
        if (!read.IsCompletedSuccessfully || !read.Result.IsSuccess) return;
        string priority = read.Result.Value!.Priority;
        bool changed = _sourcePolicyPriority is not null && _sourcePolicyPriority != priority;
        _sourcePolicyPriority = priority;
        _sourcePolicyForced = priority is "official-first" or "mirrors-first";
        // A global override changes transport policy and presentation, while retaining the page's request preference.
        int selected = (_sourcePolicyForced ? priority == "mirrors-first" : _filter.MirrorFirst) ? 0 : 1;
        for (int index = 0; index < SourcePolicyOptions.Length; index++)
        {
            var option = _entities[SourcePolicyOptions[index]];
            WorkspaceResource((SourcePolicyOptions[index] + "_enabled", Flag(!_sourcePolicyForced)), (SourcePolicyOptions[index] + "_selected", Flag(index == selected)));
            if (index == selected) _shell.Tree.GetComponent<XsrUiSegmentedTrack>(_entities["ResourceNetwork"])!.Selected = option;
            _shell.Tree.MarkDirty(option, XsrUiDirtyKinds.Paint);
        }
        if (changed) Search();
    }
}
