using Nexa.Services.Minecraft.Management;

namespace Nexa.Desktop.Ui;

/// <summary>Explains observed changes without claiming that correlation proves a crash cause.</summary>
internal static class RecoveryExplanation
{
    private static readonly string[] CategoryTermKeys =
        ["dependencies", "mods", "config", "resourcepacks", "shaderpacks", "version-settings", "options", "version-files", "launch-settings"];
    internal static string Summary(IReadOnlyList<InstanceRecoveryChange> changes, Func<string, string>? localize = null)
    {
        if (changes.Count == 0) return ProductPresentationText.Term("recovery.empty", localize);
        string[] groups = changes.GroupBy(change => (change.Category, change.Kind))
            .OrderBy(group => group.Key.Category, StringComparer.Ordinal).ThenBy(group => group.Key.Kind)
            .Take(12).Select(group => ProductPresentationText.Format("RecoveryGroup", localize,
                ("kind", Kind(group.Key.Kind, localize)), ("count", group.Count().ToString(System.Globalization.CultureInfo.CurrentCulture)),
                ("category", Category(group.Key.Category, localize)))).ToArray();
        string summary = ProductPresentationText.Format("RecoverySummary", localize, ("groups", string.Join("\n", groups)));
        var clues = changes.Where(change => change.Kind is InstanceRecoveryChangeKind.Removed or InstanceRecoveryChangeKind.Disabled
            && (change.Category == ProductPresentationText.Raw("category.mods") || change.Category == ProductPresentationText.Raw("category.dependencies"))).Take(3).ToArray();
        if (clues.Length > 0)
            summary = ProductPresentationText.Format("RecoveryClues", localize, ("summary", summary),
                ("clues", string.Join("\n", clues.Select(change => ProductPresentationText.Format("RecoveryClue", localize,
                    ("kind", Kind(change.Kind, localize)), ("path", Display(change.Path)))))));
        else if (changes.Any(change => change.SettingKey?.Contains("java", StringComparison.OrdinalIgnoreCase) == true))
            summary = ProductPresentationText.Format("RecoveryJavaChanged", localize, ("summary", summary));
        return summary;
    }

    internal static string Kind(InstanceRecoveryChangeKind kind, Func<string, string>? localize = null)
        => ProductPresentationText.Term(kind switch
        {
            InstanceRecoveryChangeKind.Added => "recovery.added",
            InstanceRecoveryChangeKind.Removed => "recovery.removed",
            InstanceRecoveryChangeKind.Enabled => "recovery.enabled",
            InstanceRecoveryChangeKind.Disabled => "recovery.disabled",
            _ => "recovery.modified"
        }, localize);

    internal static string Category(string category, Func<string, string>? localize = null) =>
        CategoryTermKeys.Any(key => ProductPresentationText.Raw("category." + key) == category)
                ? localize?.Invoke(category) ?? category : Display(category);

    internal static string Display(string value) => new(value.Take(300).Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}
