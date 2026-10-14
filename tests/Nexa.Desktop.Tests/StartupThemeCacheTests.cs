using System.Text.Json.Nodes;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void StartupThemeCacheRestoresMatchedBrandAndRespectsSafeMode()
    {
        string root = StartupAppearanceDirectory();
        try
        {
            var (_, policy, _) = StartupAppearanceSettings(root);
            AssertTrue(policy.Set(new("appearance.theme-pack-id", Nexa.Services.Settings.SettingsLayer.Global,
                new(Nexa.Services.Settings.SettingsOverrideMode.Custom, "source-splash-a"))).IsSuccess);
            AssertTrue(policy.Set(new("appearance.theme-mode", Nexa.Services.Settings.SettingsLayer.Global,
                new(Nexa.Services.Settings.SettingsOverrideMode.Custom, "1"))).IsSuccess);
            AssertTrue(policy.Set(new("appearance.reduced-motion", Nexa.Services.Settings.SettingsLayer.Global,
                new(Nexa.Services.Settings.SettingsOverrideMode.Custom, "true"))).IsSuccess);
            var prepared = StartupThemeCacheFixture(7);
            Nexa.Desktop.Program.SaveThemeStartupCacheAsync(root, "source-splash-a", prepared).GetAwaiter().GetResult();
            var appearance = Nexa.Desktop.Program.ReadStartupAppearance(root);
            AssertEqual(XsrUiThemeMode.Dark, appearance.ThemeMode); AssertTrue(appearance.ReducedMotion);
            AssertTrue(appearance.Theme is not null); AssertEqual(7L, appearance.Theme!.Generation);
            AssertTrue(prepared.BrandLightPng.Span.SequenceEqual(appearance.Theme.BrandLightPng.Span));
            AssertTrue(prepared.BrandDarkPng.Span.SequenceEqual(appearance.Theme.BrandDarkPng.Span));
            AssertEqual(prepared.Colors!, appearance.Theme.Colors!); AssertEqual(AvaloniaUiThemeSplashMotion.Breathe, appearance.Theme.Motion);
            var safe = Nexa.Desktop.Program.ReadStartupAppearance(root, safeMode: true);
            AssertTrue(safe.Theme is null); AssertEqual(XsrUiThemeMode.Dark, safe.ThemeMode); AssertTrue(safe.ReducedMotion);
            AssertTrue(policy.Set(new("appearance.theme-pack-id", Nexa.Services.Settings.SettingsLayer.Global,
                new(Nexa.Services.Settings.SettingsOverrideMode.Custom, "source-splash-b"))).IsSuccess);
            AssertTrue(Nexa.Desktop.Program.ReadStartupAppearance(root).Theme is null);
            AssertEqual(XsrUiThemeMode.Dark, Nexa.Desktop.Program.ReadStartupAppearance(root).ThemeMode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void StartupThemeCacheRejectsMalformedBudgetsAndPngWithoutWrites()
    {
        string root = StartupAppearanceDirectory();
        try
        {
            var (_, policy, _) = StartupAppearanceSettings(root);
            AssertTrue(policy.Set(new("appearance.theme-pack-id", Nexa.Services.Settings.SettingsLayer.Global,
                new(Nexa.Services.Settings.SettingsOverrideMode.Custom, "source-splash-a"))).IsSuccess);
            var prepared = StartupThemeCacheFixture(7);
            Nexa.Desktop.Program.SaveThemeStartupCacheAsync(root, "source-splash-a", prepared).GetAwaiter().GetResult();
            string path = Nexa.Desktop.Program.ThemeStartupCachePath(root), valid = File.ReadAllText(path);
            List<string> malformed = ["{broken", new string(' ', 768 * 1024 + 1)];
            foreach (var edit in new Action<JsonObject>[]
            {
                value => value["version"] = 2,
                value => value["generation"] = -1,
                value => value["sourceIdentity"] = "source-another-installation",
                value => value["motion"] = "1",
                value => value["darkPng"] = "",
                value => value["lightPng"] = Convert.ToBase64String(new byte[256 * 1024 + 1]),
                value => value["lightPng"] = Convert.ToBase64String(prepared.BrandLightPng.Span[..33]),
                value => { byte[] bad = prepared.BrandLightPng.ToArray(); bad[32] ^= 1; value["lightPng"] = Convert.ToBase64String(bad); },
                value => value["colors"]!["accent"]![0] = 256,
                value => value["extra"] = JsonNode.Parse("[[[[[[[[[0]]]]]]]]]"),
            })
            {
                var node = JsonNode.Parse(valid)!.AsObject(); edit(node); malformed.Add(node.ToJsonString());
            }
            foreach (string document in malformed)
            {
                File.WriteAllText(path, document);
                byte[] before = File.ReadAllBytes(path); DateTime modified = File.GetLastWriteTimeUtc(path);
                AssertTrue(Nexa.Desktop.Program.ReadStartupAppearance(root).Theme is null);
                AssertTrue(before.AsSpan().SequenceEqual(File.ReadAllBytes(path)));
                AssertEqual(modified, File.GetLastWriteTimeUtc(path));
            }
            File.WriteAllText(path, valid);
            AssertTrue(Nexa.Desktop.Program.ReadStartupAppearance(root).Theme is not null);
            var core = JsonNode.Parse(File.ReadAllText(StartupAppearancePath(root)))!.AsObject();
            core["textOptions"]![Nexa.Services.Settings.SettingsPolicySchema.StorageKey] =
                "{\"version\":1,\"global\":{\"appearance.theme-pack-id\":{\"mode\":\"Custom\",\"value\":\"../source-splash-a\"}}}";
            File.WriteAllText(StartupAppearancePath(root), core.ToJsonString());
            AssertTrue(Nexa.Desktop.Program.ReadStartupAppearance(root).Theme is null);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void StartupThemeCacheInvalidWritePreservesLastKnownGoodFile()
    {
        string root = StartupAppearanceDirectory();
        try
        {
            var prepared = StartupThemeCacheFixture(7);
            Nexa.Desktop.Program.SaveThemeStartupCacheAsync(root, "source-splash-a", prepared).GetAwaiter().GetResult();
            string path = Nexa.Desktop.Program.ThemeStartupCachePath(root); byte[] original = File.ReadAllBytes(path);
            foreach (var invalid in new[]
            {
                prepared with { BrandDarkPng = default },
                prepared with { BrandLightPng = prepared.BrandLightPng.Slice(0, 33) },
                prepared with { BrandLightPng = new byte[256 * 1024 + 1], BrandDarkPng = new byte[256 * 1024 + 1] },
                prepared with { Motion = (AvaloniaUiThemeSplashMotion)99 },
            })
            {
                bool rejected = false;
                try { Nexa.Desktop.Program.SaveThemeStartupCacheAsync(root, "source-splash-a", invalid).GetAwaiter().GetResult(); }
                catch (InvalidDataException) { rejected = true; }
                AssertTrue(rejected); AssertTrue(original.AsSpan().SequenceEqual(File.ReadAllBytes(path)));
            }
            Nexa.Desktop.Program.SaveThemeStartupCacheAsync(root, "source-splash-b", prepared with { Generation = 8 }).GetAwaiter().GetResult();
            AssertTrue(Nexa.Desktop.Program.ReadThemeStartupCache(root, "source-splash-a") is null);
            AssertEqual(8L, Nexa.Desktop.Program.ReadThemeStartupCache(root, "source-splash-b")!.Generation);
            AssertFalse(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static AvaloniaUiThemeStartupSnapshot StartupThemeCacheFixture(long generation)
    {
        byte[] png = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png"));
        return new(generation, png, png, new(new(17, 34, 51), new(221, 238, 255), new(0, 96, 255)), AvaloniaUiThemeSplashMotion.Breathe);
    }
}
