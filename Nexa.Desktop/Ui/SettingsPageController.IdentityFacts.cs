using Nexa.Services.Minecraft.Process;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private void BuildObservedInstanceHistory(XsrUiEntityId slot)
    {
        if (!_store.TryResolve(MinecraftProcessStateComposition.SessionsKey, out var state)) return;
        var items = _store.ReadCollection<MinecraftProcessSnapshot>(state).Items;
        DateTimeOffset? latest = null; double seconds = 0; int count = 0;
        for (int index = Math.Max(0, items.Count - 256); index < items.Count; index++)
        {
            var item = items[index];
            if (!Nexa.Core.PathIdentity.Comparer.Equals(item.InstanceDirectory, _instance)) continue;
            latest = latest is null || item.StartedAt > latest.Value ? item.StartedAt : latest;
            seconds += Math.Max(0, ((item.EndedAt ?? DateTimeOffset.UtcNow) - item.StartedAt).TotalSeconds);
            count++;
        }
        var memory = _values?.Values.FirstOrDefault(value => value.Key == "game.memory");
        var data = new Dictionary<string, string>
        {
            ["latest"] = latest is { } at ? at.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture) : CustomCopy("identity.notLaunched"),
            ["minutes"] = (seconds / 60).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            ["count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["health"] = CustomCopy(_offlineReport is { } report ? report.Ready ? "identity.healthy" : report.Complete ? "identity.unhealthy" : "identity.incomplete" : "identity.unchecked"),
            ["hasMemory"] = Flag(memory is not null),
            ["memory"] = memory is null ? "" : memory.Value.Mode == Nexa.Services.Settings.SettingsOverrideMode.Auto ? CustomCopy("identity.autoMemory")
                : CustomFormat("Memory", new() { ["value"] = memory.Value.Value ?? CustomCopy("identity.unknown"), ["source"] = memory.Source.ToString() }),
        };
        var fragment = DesktopMarkup.Dynamic("SettingsCustomIdentityHistory.d.nxml", data, _shell.Renderer.LocalizeText);
        if (_shell.Tree.IsAlive(_identityHistory)) _dynamic.Reload(_identityHistory, fragment);
        else _identityHistory = _dynamic.Append(slot, fragment);
    }
}
