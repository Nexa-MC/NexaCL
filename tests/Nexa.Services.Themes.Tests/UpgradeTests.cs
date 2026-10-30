using Nexa.Services.Themes;

namespace Nexa.Services.Themes.Tests;

internal static partial class Program
{
    private static string WithVersion(string version) => Manifest.Replace("\"version\":\"1.0.0\"", "\"version\":\"" + version + "\"", StringComparison.Ordinal);

    private static async Task SameIdUpgrade()
    {
        using var workspace = new Workspace(); string identity;
        using (var service = workspace.Service())
        {
            var first = await Prepare(service, workspace.Theme("first", WithVersion("1.9.0")));
            await Commit(service, first); identity = first.SourceIdentity;
            Require((await service.SaveOptionsAsync(new(identity, new Dictionary<string, ThemeOptionValue>
            { ["volume"] = new(ThemeOptionKind.Range, "0.8") }, 1))).IsSuccess);
            var next = await Prepare(service, workspace.Theme("next", WithVersion("1.10.0")));
            Require(next.SourceIdentity == identity && next.PreviousVersion == "1.9.0" && !next.IsDowngrade);
            Require(next.Options["volume"].Value == "0.8");
            await Commit(service, next);
            Require((await service.CommitAsync(new(next.CandidateId, 3, Preview: true))).IsSuccess,
                "A failed native activation must be able to re-pin the committed theme.");
            Require((await service.CatalogueAsync(new())).Value.Themes.Count == 1);
        }
        using var restored = workspace.Service();
        var catalogue = (await restored.CatalogueAsync(new())).Value;
        Require(catalogue.Themes.Count == 1 && catalogue.Themes[0].SourceIdentity == identity
            && catalogue.Themes[0].Manifest.Version == "1.10.0");
        Require((await restored.CurrentAsync(new())).Value.Options.Single().Values["volume"].Value == "0.8");
    }

    private static async Task UpgradeVersions()
    {
        (string Old, string New, bool Downgrade)[] cases =
        [
            ("1.9", "1.10", false), ("2.0", "1.99", true), ("1.0", "1.0.0", false),
            ("1.0.0.alpha.9", "1.0.0.alpha.10", false), ("1.0.0", "1.0.0.rc.9", true),
            ("1.0.0.alpha.9", "1.0.0", false), ("1.0.0.beta.2", "1.0.0.alpha.9", true),
            ("1.0", "1.0", false), ("999999999999999999999999.0", "1000000000000000000000000.0", false)
        ];
        foreach (var item in cases)
        {
            using var workspace = new Workspace(); using var service = workspace.Service();
            await Commit(service, await Prepare(service, workspace.Theme("old", WithVersion(item.Old))));
            var candidate = await Prepare(service, workspace.Theme("new", WithVersion(item.New)));
            Require(candidate.PreviousVersion == item.Old && candidate.IsDowngrade == item.Downgrade, $"{item.Old} -> {item.New}");
        }
    }

    private static async Task DowngradeConfirmation()
    {
        using var workspace = new Workspace(); using var service = workspace.Service();
        var first = await Prepare(service, workspace.Theme("old", WithVersion("2.0.0")));
        await Commit(service, first);
        var lower = await Prepare(service, workspace.Theme("new"));
        Require(lower.IsDowngrade);
        Require(!(await service.CommitAsync(new(lower.CandidateId, 1))).IsSuccess);
        Require(!(await service.CommitAsync(new(lower.CandidateId, 1, Preview: true))).IsSuccess);
        Require((await service.CurrentAsync(new())).Value.Selection!.Version == "2.0.0");
        Require((await service.CommitAsync(new(lower.CandidateId, 1) { AllowDowngrade = true })).IsSuccess);
        Require((await service.CurrentAsync(new())).Value.Selection!.Version == "1.0.0");
        Require((await service.CatalogueAsync(new())).Value.Themes.Count == 1);
        Require(!(await service.CommitAsync(new(first.CandidateId, 2) { AllowDowngrade = true })).IsSuccess);
    }

    private static async Task ReplacementAuthority()
    {
        using var workspace = new Workspace(); using var service = workspace.Service();
        string original = workspace.Theme("old"); string image = Visual(workspace);
        var first = await Prepare(service, original); await Commit(service, first);
        Require((await service.GrantAsync(new(first.SourceIdentity, ThemeResourceKind.Image, ThemeGrantScope.File, image))).IsSuccess);
        string replacement = workspace.Theme("new", WithVersion("2.0.0"));
        var candidate = await Prepare(service, replacement);
        Require(!(await service.ReadResourceAsync(new(candidate.CandidateId, "file(" + image + ")", ThemeResourceKind.Image))).IsSuccess);
        Require((await service.RevertAsync(new(candidate.CandidateId))).IsSuccess);
        Require((await service.ReadResourceAsync(new(first.CandidateId, "file(" + image + ")", ThemeResourceKind.Image))).IsSuccess);
        await Commit(service, await Prepare(service, replacement));
        Require((await service.CurrentAsync(new())).Value.Grants.Count == 0);
    }

    private static async Task BuiltinReplacement()
    {
        using var workspace = new Workspace(); string builtin = workspace.Theme("builtin");
        using var service = workspace.Service(builtin);
        await Commit(service, await Prepare(service, builtin, ThemeSourceKind.BuiltIn));
        Require(!(await service.PrepareAsync(new(workspace.Theme("replacement", WithVersion("2.0.0")), ThemeSourceKind.Directory))).IsSuccess);
        Require((await service.CurrentAsync(new())).Value.Selection!.SourceIdentity == "builtin");
    }

    private static async Task LegacyBuiltinCollision()
    {
        using var workspace = new Workspace();
        using (var original = workspace.Service())
            await Commit(original, await Prepare(original, workspace.Theme("external", WithVersion("2.0.0"))));
        using var service = workspace.Service(workspace.Theme("builtin"));
        var current = (await service.CurrentAsync(new())).Value;
        var candidate = (await service.PrepareAsync(new("", ThemeSourceKind.Directory, current.Selection!.SourceIdentity))).Value;
        Require(!(await service.CommitAsync(new(candidate.CandidateId, current.Revision))).IsSuccess);
        Require((await service.CatalogueAsync(new())).Value.Themes.Any(i => i.SourceIdentity == "builtin"));
    }
}
