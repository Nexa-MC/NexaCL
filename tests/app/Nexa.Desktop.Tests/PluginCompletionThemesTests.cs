using System.Collections.Immutable;
using System.Text;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Composition;
using Nexa.Services.Composition.Plugins;
using Nexa.Services.Themes;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void PluginCompletionThemes() => PluginCompletionThemesAsync().GetAwaiter().GetResult();
    private static async Task PluginCompletionThemesAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        string path = Path.Combine(fixture.TemporaryDirectory, "theme"); Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "mimetype"), "application/x-nexa-theme");
        await File.WriteAllTextAsync(Path.Combine(path, "main.nss"), ".card { Background:#123456; }");
        await File.WriteAllTextAsync(Path.Combine(path, "icon.svg"), """<svg xmlns="http://www.w3.org/2000/svg" width="1" height="1"><rect width="1" height="1" fill="red"/></svg>""");
        await File.WriteAllTextAsync(Path.Combine(path, "theme.json"), """
        {"format":"NTP/1","id":"tests.plugin-theme","version":"1.0.0","name":{"en-US":"Plugin Theme"},"styleApi":1,"nss":"NSS/1","styles":["main.nss"],"optionsVersion":2,"options":[{"id":"enabled","kind":"Boolean","default":false,"label":{"en-US":"Enabled"}}],"appearance":{"resources":{"launcherIcon":{"expression":"asset(icon.svg)","kind":"Icon","required":false,"fallback":"none"}}}}
        """);
        using var owner = new ThemePackageService(Path.Combine(fixture.TemporaryDirectory, "theme-data"), null, []);
        var candidate = await owner.PrepareAsync(new(path, ThemeSourceKind.Directory)); AssertTrue(candidate.IsSuccess);
        AssertTrue((await owner.CommitAsync(new(candidate.Value.CandidateId, (await owner.CurrentAsync(new())).Value.Revision))).IsSuccess);
        var commands = new XsrCommandRouterBuilder(); var queries = new XsrQueryRouterBuilder(); ThemesRuntime.Register(queries, commands, owner);
        await using var business = new PluginBusinessRoutes(fixture.Store, fixture.Library.Commands, fixture.Onboarding.Commands,
            foundationCommands: commands.Build(new NoopDispatchObserver()), foundationQueries: queries.Build(new NoopDispatchObserver()));
        business.BindThemes(new((c, t) => owner.SaveOptionsAsync(c, t).AsTask(), (c, t) => owner.ResetOptionsAsync(c, t).AsTask(), (c, t) => owner.ImportOptionsAsync(c, t).AsTask(),
            (_, _, _, _, _) => throw new NotSupportedException(), (_, _) => throw new NotSupportedException()));
        var confirmation = new BackupConfirmation();
        await using var app = new PluginApplication(Path.Combine(fixture.TemporaryDirectory, "completion-themes"), (_, _) => throw new NotSupportedException(), business.Routes, confirmation, (_, _) => { }, (_, _) => { }, _ => { });
        business.BindApplication(app); business.BindConfirmation(confirmation); business.Bind(app.Runtime.Services);
        var entry = new CompletionPluginEntry("example.themes"); var foreign = new CompletionPluginEntry("example.foreign-themes");
        foreach (var report in await app.Runtime.LoadAsync([entry.Source(), foreign.Source()])) AssertEqual(Nexa.Plugin.Bootstrap.V1.LoadState.Active, report.State);
        using var api = entry.Context!.AsAbi100(); var options = CompletionPluginEntry.Options;
        var theme = CompletionValue(await api.Themes.List(new(null, 16), options())).Items.Single();
        var resource = CompletionValue(await api.Themes.ListResources(new(theme.Id), options())).Items.Single();
        AssertEqual("launcherIcon", resource.Name);
        var blob = CompletionValue(await api.Themes.ReadResource(resource, options()));
        var identity = app.Runtime.Active.Single(i => i.PluginId == "example.themes");
        AssertTrue(Encoding.UTF8.GetString(app.CopyBlob(identity, blob)).Contains("<svg", StringComparison.Ordinal));
        using (var other = foreign.Context!.AsAbi100()) AssertFalse((await other.Themes.ReleaseResource(blob, options())).Success);
        CompletionValue(await api.Themes.ReleaseResource(blob, options())); AssertFalse((await api.Themes.ReadResource(resource, options())).Success);
        AssertFalse((await api.Themes.ReleaseResource(blob, options())).Success);
        var initial = CompletionValue(await api.Themes.ReadOptions(new(theme.Id), options()));
        var values = initial.Values.Select(v => v with { Value = Nexa.Plugin.Sdk.Abi100.Data.DataCodec.Map(("kind", new Nexa.Plugin.Bootstrap.V1.AbiValue.Utf8String("Boolean")), ("value", new Nexa.Plugin.Bootstrap.V1.AbiValue.Utf8String("true")), ("values", new Nexa.Plugin.Bootstrap.V1.AbiValue.Array([]))) }).ToImmutableArray();
        var migration = new ThemeOptionChange(initial with { Values = values }, initial.Revision);
        var completed = await CompletionTerminal(api, await api.Themes.MigrateOptions(migration, options()));
        AssertEqual(OperationState.Completed, completed.State);
        AssertEqual(2, (await owner.CurrentAsync(new())).Value.Options.Single().SchemaVersion);
        AssertEqual("true", (await owner.CurrentAsync(new())).Value.Options.Single().Values["enabled"].Value);
        AssertEqual(OperationState.Failed, (await CompletionTerminal(api, await api.Themes.MigrateOptions(migration, options()))).State);
        resource = CompletionValue(await api.Themes.ListResources(new(theme.Id), options())).Items.Single();
        await app.Runtime.StopAsync("example.themes");
        AssertFalse((await api.Themes.ReadResource(resource, options())).Success);
    }
}
