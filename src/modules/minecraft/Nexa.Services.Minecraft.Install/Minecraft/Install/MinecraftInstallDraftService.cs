using System.Collections.Frozen;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Minecraft.Install;

/// <summary>Owns installation choices. Desktop publishes intent and projects this snapshot.</summary>
public sealed class MinecraftInstallDraftService(XsrStateStore store)
{
    private readonly object _gate = new();
    private MinecraftInstallDraftSnapshot _current = MinecraftInstallDraftSnapshot.Empty;
    public XsrResult Replace(InstallDraftReplace change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.PrimaryLoader is { } primary && !Enum.IsDefined(primary)
            || change.Builds.Any(item => !Enum.IsDefined(item.Loader) || string.IsNullOrWhiteSpace(item.Version))
            || change.Builds.Select(item => item.Loader).Distinct().Count() != change.Builds.Count
            || change.Addons.Any(item => !Enum.IsDefined(item)))
            return XsrResult.Failure(new XsrError(XsrErrorKind.Rejected, XsrSemanticId.Parse("minecraft.install.draft.invalid_change"), "安装选择无效。"));
        if (change.Addons.Any(item => item == InstallLoader.Qsl ? change.PrimaryLoader != InstallLoader.Quilt
            : item is not (InstallLoader.FabricApi or InstallLoader.OptiFabric) || change.PrimaryLoader != InstallLoader.Fabric))
            return XsrResult.Failure(new XsrError(XsrErrorKind.Rejected, XsrSemanticId.Parse("minecraft.install.draft.incompatible_addon"), "请先选择兼容的加载器。"));
        lock (_gate)
        {
            var next = new MinecraftInstallDraftSnapshot(change.Chosen, change.GameVersion, change.PrimaryLoader,
                change.Builds.ToFrozenDictionary(item => item.Loader, item => item.Version), Array.AsReadOnly(change.Addons.Distinct().ToArray()));
            store.Publish(store.Resolve(MinecraftInstallDraftContract.StateKey), next); _current = next;
            return XsrResult.Success();
        }
    }

    public XsrResult Apply(InstallDraftChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Loader is { } candidate && !Enum.IsDefined(candidate))
            return XsrResult.Failure(new XsrError(XsrErrorKind.Rejected, XsrSemanticId.Parse("minecraft.install.draft.invalid_loader"), "加载器无效。"));
        lock (_gate)
        {
            var next = _current;
            switch (change.Kind)
            {
                case InstallDraftChangeKind.Reset: next = MinecraftInstallDraftSnapshot.Empty; break;
                case InstallDraftChangeKind.SetChosen: next = next with { Chosen = change.Chosen }; break;
                case InstallDraftChangeKind.SetVersion: next = next with { GameVersion = change.Value ?? "" }; break;
                case InstallDraftChangeKind.SetLoader: next = next with { PrimaryLoader = change.Loader }; break;
                case InstallDraftChangeKind.SelectPrimary:
                    var selectedBuilds = change.Loader is null ? FrozenDictionary<InstallLoader, string>.Empty : next.Builds;
                    next = next with
                    {
                        PrimaryLoader = change.Loader,
                        Builds = selectedBuilds,
                        Addons = Array.AsReadOnly(selectedBuilds.Keys.Where(item => item == InstallLoader.Qsl ? change.Loader == InstallLoader.Quilt
                            : (item is InstallLoader.FabricApi or InstallLoader.OptiFabric) && change.Loader == InstallLoader.Fabric).ToArray())
                    }; break;
                case InstallDraftChangeKind.ClearBuilds: next = next with { Builds = FrozenDictionary<InstallLoader, string>.Empty }; break;
                case InstallDraftChangeKind.SetBuild when change.Loader is { } loader && !string.IsNullOrWhiteSpace(change.Value):
                    var builds = next.Builds.ToDictionary(); builds[loader] = change.Value;
                    next = next with { Builds = builds.ToFrozenDictionary() }; break;
                case InstallDraftChangeKind.AddAddon when change.Loader is { } addon:
                    if ((addon == InstallLoader.FabricApi || addon == InstallLoader.OptiFabric) && next.PrimaryLoader != InstallLoader.Fabric
                        || addon == InstallLoader.Qsl && next.PrimaryLoader != InstallLoader.Quilt)
                        return XsrResult.Failure(new XsrError(XsrErrorKind.Rejected, XsrSemanticId.Parse("minecraft.install.draft.incompatible_addon"), "请先选择兼容的加载器。"));
                    next = next with { Addons = Array.AsReadOnly(next.Addons.Append(addon).Distinct().ToArray()) }; break;
                case InstallDraftChangeKind.ToggleAddon when change.Loader is { } toggled:
                    if (toggled == InstallLoader.Qsl ? next.PrimaryLoader != InstallLoader.Quilt
                        : (toggled is not (InstallLoader.FabricApi or InstallLoader.OptiFabric)) || next.PrimaryLoader != InstallLoader.Fabric)
                        return XsrResult.Failure(new XsrError(XsrErrorKind.Rejected, XsrSemanticId.Parse("minecraft.install.draft.incompatible_addon"), "请先选择兼容的加载器。"));
                    next = next with
                    {
                        Addons = Array.AsReadOnly(next.Addons.Contains(toggled)
                        ? next.Addons.Where(item => item != toggled).ToArray() : next.Addons.Append(toggled).ToArray())
                    }; break;
                case InstallDraftChangeKind.RemoveAddon: next = next with { Addons = Array.AsReadOnly(next.Addons.Where(x => x != change.Loader).ToArray()) }; break;
                case InstallDraftChangeKind.ClearAddons: next = next with { Addons = [] }; break;
                default: return XsrResult.Failure(new XsrError(XsrErrorKind.Rejected, XsrSemanticId.Parse("minecraft.install.draft.invalid_change"), "安装选择无效。"));
            }
            next = next with
            {
                Addons = Array.AsReadOnly(next.Addons.Where(x => x == InstallLoader.Qsl
                ? next.PrimaryLoader == InstallLoader.Quilt : next.PrimaryLoader == InstallLoader.Fabric).ToArray())
            };
            store.Publish(store.Resolve(MinecraftInstallDraftContract.StateKey), next);
            _current = next;
            return XsrResult.Success();
        }
    }
}
