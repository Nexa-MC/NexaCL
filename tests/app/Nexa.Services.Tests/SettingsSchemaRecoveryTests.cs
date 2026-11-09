using Nexa.Services.Settings;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static ValueTask SettingsRecoverNonNumericSchemaVersions()
    {
        string root = CreateTempDirectory();
        try
        {
            foreach (string schema in new[] { "null", "true", "\"1\"", "[]", "{}", "1.5" })
            {
                string path = Path.Combine(root, "settings.json");
                string source = "{\"schemaVersion\":" + schema + ",\"textOptions\":{\"UiLogoText\":\"retained\"}}";
                File.WriteAllText(path, source);
                var port = new LauncherSettingsJsonPort(path, LauncherDefaults.CreateSchema());
                var settings = port.Load();
                AssertEqual("retained", settings["UiLogoText"]);
                AssertEqual(source, File.ReadAllText(port.QuarantinePath));
                AssertEqual(source, File.ReadAllText(path));
                port.Save(settings);
                AssertEqual("retained", new LauncherSettingsJsonPort(path, LauncherDefaults.CreateSchema()).Load()["UiLogoText"]);
                File.Delete(port.QuarantinePath);
            }
        }
        finally { Directory.Delete(root, true); }
        return ValueTask.CompletedTask;
    }
}
