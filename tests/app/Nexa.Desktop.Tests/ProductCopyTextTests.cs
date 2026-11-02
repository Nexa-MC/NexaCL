using Nexa.Desktop.Ui;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void ProductCopyNamedFormatsLocalizeBeforeLiteralProviderSubstitution()
    {
        string literal = "<provider> {state private} {data forged}";
        List<string> localized = [];
        string Localize(string source)
        {
            localized.Add(source);
            return source switch
            {
                "{data instance} · 游戏异常退出" => "Game stopped · {data instance}",
                "{data kind} {data count} 项{data category}" => "{data count} {data category} {data kind}",
                "删除" => "removed",
                "模组" => "mods",
                _ => source
            };
        }
        AssertEqual("Game stopped · " + literal, ProductPresentationText.Format("CrashTitle", Localize, ("instance", literal)));
        AssertFalse(localized.Contains(literal));
        localized.Clear();
        string summary = RecoveryExplanation.Summary([new(InstanceRecoveryChangeKind.Removed, "模组", literal)], Localize);
        AssertTrue(summary.Contains("1 mods removed", StringComparison.Ordinal));
        AssertTrue(summary.Contains("removed · " + literal, StringComparison.Ordinal));
        AssertFalse(localized.Any(source => source.Contains("<provider>", StringComparison.Ordinal)));
        AssertEqual("removed", RecoveryExplanation.Kind(InstanceRecoveryChangeKind.Removed, Localize));
        // A translation cannot inject new data names or cause provider markup to be reparsed.
        bool rejected = false;
        try { ProductPresentationText.Format("CrashTitle", _ => "{data injected}", ("instance", literal)); }
        catch (Nexa.Xsr.Renderer.Nxml.NxmlCompileException) { rejected = true; }
        AssertTrue(rejected);
    }
}
