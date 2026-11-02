using Nexa.Desktop.Ui;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void ExternalLanguageCatalogRejectsEmptyIncompleteAndOversizedData()
    {
        Reject(() => _ = new UiLocalizationCatalog("[]"));
        Reject(() => _ = new UiLocalizationCatalog("{}"));
        Reject(() => _ = new UiLocalizationCatalog("""[{"source":"","en":"Settings","zh-Hant":"設定"}]"""));
        Reject(() => _ = new UiLocalizationCatalog("""[{"source":"设置","en":"","zh-Hant":"設定"}]"""));
        Reject(() => _ = new UiLocalizationCatalog(new string(' ', 1024 * 1024 + 1)));
        static void Reject(Action action)
        {
            bool rejected = false;
            try { action(); } catch (InvalidDataException) { rejected = true; }
            AssertTrue(rejected);
        }
        var catalog = new UiLocalizationCatalog("""[{"source":"设置","en":"Settings","zh-Hant":"設定"}]""");
        catalog.SetLanguage("en"); AssertEqual("Settings", catalog.Translate("设置"));
        catalog.SetLanguage("zh-Hant"); AssertEqual("設定", catalog.Translate("设置"));
    }
}
