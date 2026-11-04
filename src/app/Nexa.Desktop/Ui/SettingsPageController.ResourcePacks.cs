using Nexa.Services.Minecraft.Management;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private XsrUiEntityId _resourcePackTools;
    private void BuildResourcePackControls(XsrUiEntityId slot, InstanceContentEntry entry)
    {
        if (_selected != "resourcepacks" || _management?.ResourcePacks is not { } config) return;
        var pack = config.Packs.FirstOrDefault(p => p.Name == entry.Name);
        var nodes = WorldView(slot, ref _resourcePackTools, "ResourcePack", new()
        {
            ["status"] = config.Problem.Length > 0 ? config.Problem : (pack?.FormatRange ?? "") + " / " + config.TargetFormat + " · " + pack?.Problem,
            ["up"] = Flag(_managementWrite is null && pack is { Enabled: true } && pack.Order < config.Packs.Where(p => p.Enabled).Max(p => p.Order)),
            ["down"] = Flag(_managementWrite is null && pack is { Enabled: true } && pack.Order > config.Packs.Where(p => p.Enabled).Min(p => p.Order)),
        });
        RegisterContentAction(nodes["ResourcePack.Up"], () => { if (pack is not null) SetResourcePack(entry, true, pack.Order + 1); });
        RegisterContentAction(nodes["ResourcePack.Down"], () => { if (pack is not null) SetResourcePack(entry, true, Math.Max(0, pack.Order - 1)); });
    }
    private void SetResourcePack(InstanceContentEntry entry, bool enabled, int? order)
    {
        if (_managementWrite is not null || _management?.ResourcePacks is not { } config || config.Problem.Length > 0
            || !_commands.TryResolve(InstanceResourcePackContract.Set, out var route)) return;
        var pack = config.Packs.FirstOrDefault(p => p.Name == entry.Name); if (pack is null) return;
        var command = new InstanceResourcePackCommand(_management.InstanceDirectory, config.Revision, entry.Name, enabled, order ?? (pack.Order < 0 ? 4096 : pack.Order));
        void Dispatch(bool confirmed)
        {
            if (!confirmed || _managementWrite is not null || _management?.ResourcePacks?.Revision != config.Revision) return;
            _managementWriteInstance = command.InstanceDirectory;
            _managementWrite = _commands.Dispatch(route, command with { AllowIncompatible = pack.Compatible == false }).Completion;
            WakeOnPlatformCompletion(_managementWrite);
        }
        if (enabled && pack.Compatible == false)
            _feedback.ShowDialog("resource-pack.compatibility", WorldTerm("resource-pack.title"), WorldTerm("resource-pack.incompatible"), WorldTerm("resource-pack.enable"), WorldTerm("cancel"), Dispatch);
        else Dispatch(true);
    }
}
