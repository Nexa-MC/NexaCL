using System.IO.Compression;
using System.Text;
using Nexa.Services.Themes;

namespace Nexa.Services.Themes.Tests;

internal static class Program
{
    private static readonly (string Name, Func<Task> Run)[] Tests =
    [
        ("Prepare, commit and restart retain last known good", CommitAndRestart),
        ("Preview does not write selection", PreviewDoesNotCommit),
        ("Incomplete candidate recovers committed theme", Recovery),
        ("Manifest rejects behavioral declarations and duplicates", InvalidManifest),
        ("Required capability rejects while optional declares fallback", Capabilities),
        ("ZIP rejects traversal, duplicates and reserved paths", ArchivePaths),
        ("Folder sources reject links", FolderLinks),
        ("System aliases preserve source ownership and reject other links", PathAliases),
        ("Ordered NSS enforces total budget", StyleBudget),
        ("Same theme ID cannot inherit source grants", AuthorityIsolation),
        ("External resource requires kind-bound selected grant", ResourceGrant),
        ("Revocation and retirement release immutable leases", LeaseLifetime),
        ("Resource folder bounds actual playlist", PlaylistBudget),
        ("Options validate step, type and persistent revisions", Options),
        ("Options import cannot smuggle resource grants", OptionsImport),
        ("Legacy migration is idempotent and keeps unknown fields", LegacyMigration),
        ("Directory watch debounces candidates and keeps committed selection", DirectoryWatch),
        ("Active theme cannot be uninstalled", Uninstall),
        ("Malformed active SVG does not prepare a lease", ActiveSvg),
        ("Failed durable commit preserves prior selection and resource leases", FailedCommit),
        ("Default values and appearance declarations enforce finite typed grammar", TypedManifest),
        ("Conditions evaluate safely and reject unknown options", Conditions),
        ("Provisional options preserve selection until validated commit", ProvisionalOptions),
        ("Repeated source updates retire unreferenced immutable revisions", SourceRetirement),
        ("Owned snapshot tampering rejects while host built-in root is reused", SnapshotIntegrity),
        ("First preview has owned catalogue, options and recoverable grants without replacing last good", FirstPreview),
        ("Same-source preview cancellation resolves committed revision explicitly", CommittedSourcePreview),
    ];

    private static async Task<int> Main()
    {
        int failed = 0;
        foreach (var test in Tests)
        {
            try { await test.Run().ConfigureAwait(false); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
        }
        Console.WriteLine($"NTP/1 services: {Tests.Length - failed} passed, {failed} failed."); return failed == 0 ? 0 : 1;
    }
    private sealed class Workspace(string? temporaryRoot = null) : IDisposable
    {
        internal string Root { get; } = Path.Combine(temporaryRoot ?? Path.GetTempPath(), "nexa-ntp-tests-" + Guid.NewGuid().ToString("N"));
        internal string Data => Path.Combine(Root, "data");
        internal string Theme(string name = "theme", string? manifest = null)
        {
            string path = Path.Combine(Root, name); Directory.CreateDirectory(Path.Combine(path, "styles"));
            File.WriteAllText(Path.Combine(path, "mimetype"), "application/x-nexa-theme\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(path, "theme.json"), manifest ?? Manifest, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(path, "styles", "main.nss"), ".card { Background:#123456; }", new UTF8Encoding(false)); return path;
        }
        internal ThemePackageService Service(string? builtin = null, params string[] capabilities) => new(Data, builtin, capabilities);
        public void Dispose() { try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    private const string Manifest = """
        {"format":"NTP/1","id":"tests.theme","version":"1.0.0","name":{"en-US":"Test Theme"},"styleApi":1,"nss":"NSS/1","styles":["styles/main.nss"],"options":[{"id":"accent","kind":"Color","default":"#123456","label":{"en-US":"Accent"}},{"id":"volume","kind":"Range","default":0.5,"min":0,"max":1,"step":0.1,"label":{"en-US":"Volume"}},{"id":"music.enabled","kind":"Boolean","default":false,"label":{"en-US":"Music"}},{"id":"music.path","kind":"Files","resourceKind":"Audio","default":[],"label":{"en-US":"Playlist"}}],"appearance":{}}
        """;
    private static void Require(bool condition, string message = "Contract assertion failed.") { if (!condition) throw new InvalidOperationException(message); }
    private static async Task<ThemeCandidate> Prepare(ThemePackageService service, string path, ThemeSourceKind kind = ThemeSourceKind.Directory)
    { var result = await service.PrepareAsync(new(path, kind)); Require(result.IsSuccess, result.Error?.Message ?? "prepare failed"); return result.Value; }
    private static async Task Commit(ThemePackageService service, ThemeCandidate candidate)
    { var state = (await service.CurrentAsync(new())).Value; var result = await service.CommitAsync(new(candidate.CandidateId, state.Revision)); Require(result.IsSuccess, result.Error?.Message ?? "commit failed"); }

    private static async Task CommitAndRestart()
    {
        using var workspace = new Workspace(); string path = workspace.Theme(); string identity;
        using (var service = workspace.Service())
        {
            var candidate = await Prepare(service, path);
            var inspected = Task.Factory.StartNew(() => service.CurrentAsync(new()).AsTask().GetAwaiter().GetResult(),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            // Keep this bounded wait on the preparing thread so failed cleanup can still dispose there.
            Require(inspected.Wait(TimeSpan.FromSeconds(10)), "Candidate cleanup retained the service monitor across threads.");
            Require(inspected.Result.IsSuccess && inspected.Result.Value.Selection is null);
            await Commit(service, candidate); identity = candidate.SourceIdentity;
        }
        using var restored = workspace.Service(); var state = (await restored.CurrentAsync(new())).Value; Require(state.Selection?.SourceIdentity == identity && state.LastKnownGood == state.Selection);
        var reload = await restored.PrepareAsync(new("", ThemeSourceKind.Directory, identity)); Require(reload.IsSuccess && reload.Value.Styles[0].Text.Contains("#123456", StringComparison.Ordinal));
    }
    private static async Task PreviewDoesNotCommit()
    {
        using var workspace = new Workspace(); using var service = workspace.Service(); var candidate = await Prepare(service, workspace.Theme());
        Require((await service.CommitAsync(new(candidate.CandidateId, 0, true))).IsSuccess); Require((await service.CurrentAsync(new())).Value.Selection is null);
    }
    private static async Task Recovery()
    {
        using var workspace = new Workspace(); string path = workspace.Theme(); string identity;
        using (var service = workspace.Service())
        {
            var first = await Prepare(service, path); await Commit(service, first); identity = first.SourceIdentity;
            File.WriteAllText(Path.Combine(path, "styles", "main.nss"), ".card { Background:#654321; }");
            Require((await service.PrepareAsync(new(path, ThemeSourceKind.Directory, identity))).IsSuccess);
        }
        using var restored = workspace.Service(); var state = (await restored.CurrentAsync(new())).Value; Require(state.RecoveredIncompleteTransaction && state.Selection?.SourceIdentity == identity);
        var candidate = (await restored.PrepareAsync(new("", ThemeSourceKind.Directory, identity))).Value; Require(candidate.Styles[0].Text.Contains("#123456", StringComparison.Ordinal));
    }
    private static async Task InvalidManifest()
    {
        using var workspace = new Workspace(); using var service = workspace.Service();
        foreach (string content in new[] { Manifest.Replace("\"appearance\":{}", "\"command\":\"launch\",\"appearance\":{}", StringComparison.Ordinal), Manifest.Replace("\"format\":\"NTP/1\"", "\"format\":\"NTP/1\",\"format\":\"NTP/1\"", StringComparison.Ordinal) })
            Require(!(await service.PrepareAsync(new(workspace.Theme(Guid.NewGuid().ToString("N"), content), ThemeSourceKind.Directory))).IsSuccess);
    }
    private static async Task Capabilities()
    {
        using var workspace = new Workspace(); using var service = workspace.Service();
        string required = Manifest.Replace("\"appearance\":{}", "\"requiredCapabilities\":[\"unavailable.font\"],\"appearance\":{}", StringComparison.Ordinal);
        Require(!(await service.PrepareAsync(new(workspace.Theme("required", required), ThemeSourceKind.Directory))).IsSuccess);
        string optional = Manifest.Replace("\"appearance\":{}", "\"optionalCapabilities\":[{\"name\":\"unavailable.font\",\"fallback\":\"system\"}],\"appearance\":{}", StringComparison.Ordinal);
        var candidate = await Prepare(service, workspace.Theme("optional", optional)); Require(candidate.UnsupportedOptionalCapabilities.Count == 1 && candidate.UnsupportedOptionalCapabilities[0] == "unavailable.font");
    }
    private static async Task ArchivePaths()
    {
        using var workspace = new Workspace(); Directory.CreateDirectory(workspace.Root); using var service = workspace.Service();
        string validRoot = workspace.Theme("valid-archive");
        string validZip = Path.Combine(workspace.Root, "valid.ntp"); ZipFile.CreateFromDirectory(validRoot, validZip);
        Require((await service.PrepareAsync(new(validZip, ThemeSourceKind.Package))).IsSuccess, "Valid control ZIP must be admitted.");
        foreach (string[] names in new[] { new[] { "../outside" }, new[] { "Styles/main.nss", "styles/main.nss" }, new[] { "styles/a.nss", "STYLES/b.nss" }, new[] { "CON.txt" }, new[] { "a\\b" }, new[] { "images/a.svg", "images" }, new[] { "image\u00e9.svg", "imagee\u0301.svg" }, new[] { "PLUGIN.DLL" } })
        {
            string zip = Path.Combine(workspace.Root, Guid.NewGuid().ToString("N") + ".ntp");
            ZipFile.CreateFromDirectory(validRoot, zip);
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update)) foreach (string name in names) { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write("x"); }
            Require(!(await service.PrepareAsync(new(zip, ThemeSourceKind.Package))).IsSuccess);
        }
        Require(!File.Exists(Path.Combine(workspace.Root, "outside")));
    }
    private static async Task FolderLinks()
    {
        if (OperatingSystem.IsWindows()) return;
        using var workspace = new Workspace(); string path = workspace.Theme(); string external = Path.Combine(workspace.Root, "external"); File.WriteAllText(external, "secret");
        File.CreateSymbolicLink(Path.Combine(path, "linked.txt"), external); using var service = workspace.Service(); Require(!(await service.PrepareAsync(new(path, ThemeSourceKind.Directory))).IsSuccess);
    }
    private static async Task PathAliases()
    {
        if (!OperatingSystem.IsWindows())
        {
            using var workspace = new Workspace(); string source = workspace.Theme(); using var service = workspace.Service();
            var candidate = await Prepare(service, source); string image = Visual(workspace);
            string linkedSource = Path.Combine(workspace.Root, "linked-source"), linkedImage = Path.Combine(workspace.Root, "linked.svg"), linkedData = Path.Combine(workspace.Root, "linked-data");
            Directory.CreateSymbolicLink(linkedSource, source); File.CreateSymbolicLink(linkedImage, image); Directory.CreateSymbolicLink(linkedData, workspace.Data);
            Require(!(await service.PrepareAsync(new(linkedSource, ThemeSourceKind.Directory))).IsSuccess, "An arbitrary source link must remain rejected.");
            Require(!(await service.GrantAsync(new(candidate.SourceIdentity, ThemeResourceKind.Image, ThemeGrantScope.File, linkedImage))).IsSuccess, "An arbitrary resource link must remain rejected.");
            Require((await service.GrantAsync(new(candidate.SourceIdentity, ThemeResourceKind.Image, ThemeGrantScope.File, image))).IsSuccess);
            Require(!(await service.ReadResourceAsync(new(candidate.CandidateId, "file(" + linkedImage + ")", ThemeResourceKind.Image))).IsSuccess, "A link cannot inherit a grant for its target.");
            bool rejected = false;
            try { using var linkedService = new ThemePackageService(Path.Combine(linkedData, "nested"), null, []); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, "An arbitrary data-root ancestor link must remain rejected.");
        }
        if (!OperatingSystem.IsMacOS()) return;
        foreach (string alias in new[] { "/var", "/tmp", "/etc" })
        {
            Require(ThemePaths.Canonical(alias + "/nexa-path-test") == "/private" + alias + "/nexa-path-test");
            Require(ThemePaths.Canonical(alias + "/./nexa-path-test") == "/private" + alias + "/nexa-path-test");
            Require(ThemePaths.Canonical(alias + "-other/nexa-path-test") == alias + "-other/nexa-path-test", "Only a complete root component is an OS alias.");
        }
        Require(ThemePaths.Canonical("/private/tmp/nexa-path-test") == "/private/tmp/nexa-path-test");
        using var aliased = new Workspace("/tmp"); string path = aliased.Theme(), builtin = aliased.Theme("builtin"), selected = Visual(aliased); string identity;
        using (var service = aliased.Service(builtin))
        {
            var first = await Prepare(service, path); identity = first.SourceIdentity;
            var equivalent = await service.PrepareAsync(new(ThemePaths.Canonical(path), ThemeSourceKind.Directory, identity)); Require(equivalent.IsSuccess, "OS aliases must retain the same source identity.");
            var catalogue = (await service.CatalogueAsync(new())).Value;
            Require(catalogue.Themes.Single(theme => theme.SourceIdentity == identity).DevelopmentDirectory == ThemePaths.Canonical(path));
            Require(catalogue.Themes.Single(theme => theme.SourceIdentity == "builtin").InstalledDirectory == ThemePaths.Canonical(builtin));
            Require((await service.GrantAsync(new(identity, ThemeResourceKind.Image, ThemeGrantScope.File, selected))).IsSuccess);
            Require((await service.GrantAsync(new(identity, ThemeResourceKind.Image, ThemeGrantScope.File, ThemePaths.Canonical(selected)))).IsSuccess);
            Require((await service.CurrentAsync(new())).Value.Grants.Single().CanonicalPath == ThemePaths.Canonical(selected), "Equivalent OS aliases must share one canonical grant.");
            Require((await service.ReadResourceAsync(new(equivalent.Value.CandidateId, "file(" + selected + ")", ThemeResourceKind.Image))).IsSuccess);
            Require((await service.ReadResourceAsync(new(equivalent.Value.CandidateId, "file(" + ThemePaths.Canonical(selected) + ")", ThemeResourceKind.Image))).IsSuccess);
            await Commit(service, equivalent.Value);
        }
        using var restored = aliased.Service(builtin);
        Require((await restored.CurrentAsync(new())).Value.Grants.Single().CanonicalPath == ThemePaths.Canonical(selected));
        Require((await restored.PrepareAsync(new(ThemePaths.Canonical(path), ThemeSourceKind.Directory, identity))).IsSuccess, "Canonical source ownership must survive restart.");
    }
    private static async Task StyleBudget()
    {
        using var workspace = new Workspace(); string path = workspace.Theme(); File.WriteAllText(Path.Combine(path, "styles", "main.nss"), new string(' ', 1024 * 1024 + 1));
        using var service = workspace.Service(); Require(!(await service.PrepareAsync(new(path, ThemeSourceKind.Directory))).IsSuccess);
    }
    private static string Visual(Workspace workspace)
    { string path = Path.Combine(workspace.Root, "selected.svg"); File.WriteAllText(path, "<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0 L1 1\"/></svg>"); return path; }
    private static async Task AuthorityIsolation()
    {
        using var workspace = new Workspace(); using var service = workspace.Service(); var first = await Prepare(service, workspace.Theme("first")); string image = Visual(workspace);
        Require((await service.GrantAsync(new(first.SourceIdentity, ThemeResourceKind.Image, ThemeGrantScope.File, image))).IsSuccess);
        await Commit(service, first);
        var second = await Prepare(service, workspace.Theme("second")); Require(first.Manifest.Id == second.Manifest.Id && first.SourceIdentity != second.SourceIdentity);
        Require(!(await service.ReadResourceAsync(new(second.CandidateId, "file(" + image + ")", ThemeResourceKind.Image))).IsSuccess);
        Require(!(await service.PrepareAsync(new(workspace.Theme("third"), ThemeSourceKind.Directory, first.SourceIdentity))).IsSuccess);
    }
    private static async Task ResourceGrant()
    {
        using var workspace = new Workspace(); using var service = workspace.Service(); var candidate = await Prepare(service, workspace.Theme()); string image = Visual(workspace);
        Require(!(await service.ReadResourceAsync(new(candidate.CandidateId, "file(" + image + ")", ThemeResourceKind.Image))).IsSuccess);
        Require((await service.GrantAsync(new(candidate.SourceIdentity, ThemeResourceKind.Icon, ThemeGrantScope.File, image))).IsSuccess);
        Require(!(await service.ReadResourceAsync(new(candidate.CandidateId, "file(" + image + ")", ThemeResourceKind.Image))).IsSuccess);
        Require((await service.ReadResourceAsync(new(candidate.CandidateId, "file(" + image + ")", ThemeResourceKind.Icon))).IsSuccess);
    }
    private static async Task LeaseLifetime()
    {
        using var workspace = new Workspace(); using var service = workspace.Service(); var candidate = await Prepare(service, workspace.Theme()); string image = Visual(workspace);
        Require((await service.GrantAsync(new(candidate.SourceIdentity, ThemeResourceKind.Icon, ThemeGrantScope.File, image))).IsSuccess);
        var payload = (await service.ReadResourceAsync(new(candidate.CandidateId, "file(" + image + ")", ThemeResourceKind.Icon))).Value; Require(File.Exists(payload.PreparedPath));
        var grant = (await service.CurrentAsync(new())).Value.Grants.Single(); Require((await service.RevokeAsync(new(grant.GrantId))).IsSuccess);
        Require(!File.Exists(payload.PreparedPath) && payload.Bytes.All(b => b == 0));
        await service.RevertAsync(new(candidate.CandidateId)); Require(!(await service.ReadResourceAsync(new(candidate.CandidateId, "file(" + image + ")", ThemeResourceKind.Icon))).IsSuccess);
    }
    private static async Task PlaylistBudget()
    {
        using var workspace = new Workspace(); using var service = workspace.Service(); var candidate = await Prepare(service, workspace.Theme()); string folder = Path.Combine(workspace.Root, "audio"); Directory.CreateDirectory(folder);
        for (int i = 0; i < 129; i++) File.WriteAllBytes(Path.Combine(folder, i + ".mp3"), [73, 68, 51]);
        Require((await service.GrantAsync(new(candidate.SourceIdentity, ThemeResourceKind.Audio, ThemeGrantScope.Directory, folder))).IsSuccess);
        Require(!(await service.ListResourcesAsync(new(candidate.CandidateId, "folder(" + folder + ")", ThemeResourceKind.Audio))).IsSuccess);
        File.Delete(Path.Combine(folder, "128.mp3")); var list = await service.ListResourcesAsync(new(candidate.CandidateId, "folder(" + folder + ")", ThemeResourceKind.Audio));
        Require(list.IsSuccess && list.Value.Entries.Count == 128 && list.Value.Entries.All(e => e.Bytes.Length == 0 && File.Exists(e.PreparedPath)));
    }
    private static async Task Options()
    {
        using var workspace = new Workspace(); using var service = workspace.Service(); var candidate = await Prepare(service, workspace.Theme()); await Commit(service, candidate);
        long revision = (await service.CurrentAsync(new())).Value.Revision;
        Require(!(await service.SaveOptionsAsync(new(candidate.SourceIdentity, new Dictionary<string, ThemeOptionValue> { ["volume"] = new(ThemeOptionKind.Range, "0.15") }, revision))).IsSuccess);
        Require((await service.SaveOptionsAsync(new(candidate.SourceIdentity, new Dictionary<string, ThemeOptionValue> { ["volume"] = new(ThemeOptionKind.Range, "0.8") }, revision))).IsSuccess);
        Require(!(await service.SaveOptionsAsync(new(candidate.SourceIdentity, new Dictionary<string, ThemeOptionValue>(), revision))).IsSuccess);
        var current = (await service.PrepareAsync(new("", ThemeSourceKind.Directory, candidate.SourceIdentity))).Value; Require(current.Options["volume"].Value == "0.8");
    }
    private static async Task OptionsImport()
    {
        using var workspace = new Workspace(); using var service = workspace.Service(); var candidate = await Prepare(service, workspace.Theme()); await Commit(service, candidate);
        var exported = (await service.ExportOptionsAsync(new(candidate.SourceIdentity))).Value; long revision = (await service.CurrentAsync(new())).Value.Revision;
        Require((await service.ImportOptionsAsync(new(candidate.SourceIdentity, exported.Json, revision))).IsSuccess);
        string forged = exported.Json[..^1] + ",\"grants\":[{\"path\":\"/tmp\"}]}";
        Require(!(await service.ImportOptionsAsync(new(candidate.SourceIdentity, forged, revision + 1))).IsSuccess && (await service.CurrentAsync(new())).Value.Grants.Count == 0);
    }
    private static async Task LegacyMigration()
    {
        using var workspace = new Workspace(); using var service = workspace.Service(workspace.Theme("builtin"));
        string one = Path.Combine(workspace.Root, "one.mp3"), two = Path.Combine(workspace.Root, "two.mp3"); File.WriteAllBytes(one, [73, 68, 51]); File.WriteAllBytes(two, [73, 68, 51]);
        var values = new Dictionary<string, string> { ["music.enabled"] = "True", ["music.path"] = one + "|" + two, ["unknown-legacy"] = "keep", ["appearance.background-path"] = Path.Combine(workspace.Root, "missing.png") };
        Require((await service.MigrateAsync(new(values))).IsSuccess); var first = (await service.CurrentAsync(new())).Value; var options = first.Options.Single();
        Require(options.Values["music.enabled"].Value == "true" && options.Values["music.path"].Values?.Count == 2 && options.RetainedLegacyValues["unknown-legacy"] == "keep");
        Require(first.Grants.Count == 2 && first.Grants.All(grant => grant.SourceIdentity == "builtin" && grant.Kind == ThemeResourceKind.Audio));
        Require((await service.MigrateAsync(new(new Dictionary<string, string>()))).IsSuccess && (await service.CurrentAsync(new())).Value.Revision == first.Revision);
    }
    private static async Task DirectoryWatch()
    {
        using var workspace = new Workspace(); using var service = workspace.Service(); string path = workspace.Theme(); var candidate = await Prepare(service, path); await Commit(service, candidate);
        var notified = new TaskCompletionSource<ThemeWatchSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.WatchChanged += snapshot => notified.TrySetResult(snapshot);
        Require((await service.WatchAsync(new(candidate.SourceIdentity, true))).IsSuccess);
        for (int i = 0; i < 3; i++) { File.WriteAllText(Path.Combine(path, "styles", "main.nss"), ".card { Background:#abcdef; }" + i); await Task.Delay(30); }
        var watched = await notified.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Require(watched?.CandidateId is not null && watched.Revision <= 2); var prepared = (await service.CandidateAsync(new(watched!.CandidateId!.Value))).Value;
        Require(prepared.SourceRevision != candidate.SourceRevision && (await service.CurrentAsync(new())).Value.Selection?.Generation == candidate.Generation);
        await service.WatchAsync(new(candidate.SourceIdentity, false)); Require(!(await service.WatchStatusAsync(new(candidate.SourceIdentity))).IsSuccess);
    }
    private static async Task Uninstall()
    {
        using var workspace = new Workspace(); using var service = workspace.Service(); var candidate = await Prepare(service, workspace.Theme()); await Commit(service, candidate);
        Require(!(await service.RemoveAsync(new(candidate.SourceIdentity))).IsSuccess);
    }
    private static async Task ActiveSvg()
    {
        using var workspace = new Workspace(); using var service = workspace.Service(); string path = workspace.Theme(); File.WriteAllText(Path.Combine(path, "active.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");
        var candidate = await Prepare(service, path); Require(!(await service.ReadResourceAsync(new(candidate.CandidateId, "asset(active.svg)", ThemeResourceKind.Icon))).IsSuccess);
    }
    private static async Task FailedCommit()
    {
        using var workspace = new Workspace(); using var service = workspace.Service(); string firstRoot = workspace.Theme("first");
        File.WriteAllText(Path.Combine(firstRoot, "icon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0 L1 1\"/></svg>");
        var first = await Prepare(service, firstRoot); await Commit(service, first); var original = (await service.CurrentAsync(new())).Value;
        var lease = (await service.ReadResourceAsync(new(first.CandidateId, "asset(icon.svg)", ThemeResourceKind.Icon))).Value;
        var next = await Prepare(service, workspace.Theme("next")); string state = Path.Combine(workspace.Data, "themes", "state.json"), backup = state + ".saved";
        File.Move(state, backup); Directory.CreateDirectory(state);
        try
        {
            Require(!(await service.CommitAsync(new(next.CandidateId, original.Revision))).IsSuccess);
            var after = (await service.CurrentAsync(new())).Value;
            Require(after.Selection == original.Selection && after.LastKnownGood == original.LastKnownGood && after.Revision == original.Revision);
            Require(File.Exists(lease.PreparedPath) && lease.Bytes.Any(b => b != 0));
        }
        finally { Directory.Delete(state); File.Move(backup, state); }
        Require((await service.RevertAsync(new(next.CandidateId))).IsSuccess);
    }
    private static async Task TypedManifest()
    {
        using var workspace = new Workspace(); using var service = workspace.Service();
        string[] invalid =
        [
            Manifest.Replace("\"default\":false", "\"default\":\"false\"", StringComparison.Ordinal),
            Manifest.Replace("\"default\":0.5", "\"default\":\"0.5\"", StringComparison.Ordinal),
            Manifest.Replace("\"appearance\":{}", "\"appearance\":{\"properties\":{\"window-opacity\":0}}", StringComparison.Ordinal),
            Manifest.Replace("\"appearance\":{}", "\"appearance\":{\"properties\":{\"music.enabled\":\"option(accent)\"}}", StringComparison.Ordinal),
            Manifest.Replace("\"appearance\":{}", "\"appearance\":{\"properties\":{\"hardware-disable\":true}}", StringComparison.Ordinal),
            Manifest.Replace("\"appearance\":{}", "\"appearance\":{\"resources\":{\"launcherIcon\":{\"expression\":\"asset(icon.mp3)\",\"kind\":\"Audio\",\"required\":false,\"fallback\":\"none\"}}}", StringComparison.Ordinal)
        ];
        foreach (string text in invalid) Require(!(await service.PrepareAsync(new(workspace.Theme(Guid.NewGuid().ToString("N"), text), ThemeSourceKind.Directory))).IsSuccess);
    }
    private static Task Conditions()
    {
        var values = new Dictionary<string, ThemeOptionValue> { ["enabled"] = new(ThemeOptionKind.Boolean, "true"), ["variant"] = new(ThemeOptionKind.Select, "dark"), ["size"] = new(ThemeOptionKind.Number, "24") };
        Require(ThemeOptionCondition.Evaluate("option(enabled) && (option(variant) == 'dark' || option(size) == 0)", values));
        Require(!ThemeOptionCondition.Evaluate("!option(enabled) || option(size) != 24", values));
        foreach (string expression in new[] { "option(missing)", "launch()", "option(size)", new string('(', 20) + "true" + new string(')', 20) })
        {
            bool rejected = false; try { ThemeOptionCondition.Evaluate(expression, values); } catch (InvalidDataException) { rejected = true; }
            Require(rejected);
        }
        return Task.CompletedTask;
    }
    private static async Task ProvisionalOptions()
    {
        using var workspace = new Workspace(); using var service = workspace.Service(); var initial = await Prepare(service, workspace.Theme()); await Commit(service, initial);
        var before = (await service.CurrentAsync(new())).Value;
        var values = new Dictionary<string, ThemeOptionValue> { ["volume"] = new(ThemeOptionKind.Range, "0.8") };
        var prepared = await service.PrepareAsync(new("", ThemeSourceKind.Directory, initial.SourceIdentity, values, before.Revision));
        Require(prepared.IsSuccess && prepared.Value.Options["volume"].Value == "0.8");
        var still = (await service.CurrentAsync(new())).Value;
        Require(still.Selection == before.Selection && still.Options.Single().Values["volume"].Value == "0.5" && still.Revision == before.Revision);
        var invalid = new Dictionary<string, ThemeOptionValue> { ["volume"] = new(ThemeOptionKind.Range, "0.85") };
        Require(!(await service.PrepareAsync(new("", ThemeSourceKind.Directory, initial.SourceIdentity, invalid, before.Revision))).IsSuccess);
        Require(!(await service.PrepareAsync(new("", ThemeSourceKind.Directory, initial.SourceIdentity, values, before.Revision - 1))).IsSuccess);
        string exported = (await service.ExportOptionsAsync(new(initial.SourceIdentity))).Value.Json;
        Require((await service.PrepareAsync(new("", ThemeSourceKind.Directory, initial.SourceIdentity, ExpectedRevision: before.Revision, OptionsDocument: exported))).IsSuccess);
        Require(!(await service.PrepareAsync(new("", ThemeSourceKind.Directory, initial.SourceIdentity, ExpectedRevision: before.Revision, OptionsDocument: exported[..^1] + ",\"grants\":[]}"))).IsSuccess);
        await Commit(service, prepared.Value); var committed = (await service.CurrentAsync(new())).Value;
        Require(committed.Options.Single().Values["volume"].Value == "0.8" && committed.Selection?.Generation == prepared.Value.Generation);
        var stale = (await service.PrepareAsync(new("", ThemeSourceKind.Directory, initial.SourceIdentity))).Value;
        Require((await service.SaveOptionsAsync(new(initial.SourceIdentity, new Dictionary<string, ThemeOptionValue> { ["volume"] = new(ThemeOptionKind.Range, "0.7") }, committed.Revision))).IsSuccess);
        long fresh = (await service.CurrentAsync(new())).Value.Revision;
        Require(!(await service.CommitAsync(new(stale.CandidateId, fresh))).IsSuccess && (await service.CurrentAsync(new())).Value.Options.Single().Values["volume"].Value == "0.7");
    }
    private static async Task SourceRetirement()
    {
        using var workspace = new Workspace(); using var service = workspace.Service(); string path = workspace.Theme(); var initial = await Prepare(service, path); await Commit(service, initial);
        for (int revision = 0; revision < 5; revision++)
        {
            string previous = (await service.CatalogueAsync(new())).Value.Themes.Single().InstalledDirectory!;
            File.WriteAllText(Path.Combine(path, "styles", "main.nss"), ".card { Background:#123456; } /* " + revision + " */");
            var next = (await service.PrepareAsync(new(path, ThemeSourceKind.Directory, initial.SourceIdentity))).Value; await Commit(service, next);
            var installed = (await service.CatalogueAsync(new())).Value.Themes.Single();
            Require(installed.InstalledDirectory != previous && !Directory.Exists(previous) && Directory.Exists(installed.InstalledDirectory));
        }
    }
    private static async Task SnapshotIntegrity()
    {
        using var workspace = new Workspace(); string builtinRoot = workspace.Theme("builtin"); using var service = workspace.Service(builtinRoot);
        var direct = await service.PrepareAsync(new(builtinRoot, ThemeSourceKind.BuiltIn)); Require(direct.IsSuccess && direct.Value.SourceIdentity == "builtin"); await Commit(service, direct.Value);
        Require((await service.CatalogueAsync(new())).Value.Themes.Single().InstalledDirectory == ThemePaths.Canonical(builtinRoot));
        var external = await Prepare(service, workspace.Theme("external")); await Commit(service, external);
        string snapshot = (await service.CatalogueAsync(new())).Value.Themes.Single(theme => theme.SourceIdentity == external.SourceIdentity).InstalledDirectory!;
        File.WriteAllText(Path.Combine(snapshot, "styles", "main.nss"), ".card { Background:#ffffff; }");
        Require(!(await service.PrepareAsync(new("", ThemeSourceKind.Directory, external.SourceIdentity))).IsSuccess && (await service.CurrentAsync(new())).Value.Selection?.SourceRevision == external.SourceRevision);
    }
    private static async Task FirstPreview()
    {
        foreach (bool cancelPreview in new[] { false, true })
        {
            using var workspace = new Workspace(); string committedIdentity, previewIdentity;
            using (var service = workspace.Service())
            {
                var committed = await Prepare(service, workspace.Theme("committed")); await Commit(service, committed); committedIdentity = committed.SourceIdentity;
                var preview = await Prepare(service, workspace.Theme("preview")); previewIdentity = preview.SourceIdentity;
                var catalogue = (await service.CatalogueAsync(new())).Value;
                Require(catalogue.Themes.Single(theme => theme.SourceIdentity == committedIdentity).IsInstalled);
                Require(!catalogue.Themes.Single(theme => theme.SourceIdentity == previewIdentity).IsInstalled);
                string selected = Visual(workspace); Require((await service.GrantAsync(new(previewIdentity, ThemeResourceKind.Icon, ThemeGrantScope.File, selected))).IsSuccess);
                long revision = (await service.CurrentAsync(new())).Value.Revision;
                Require((await service.CommitAsync(new(preview.CandidateId, revision, true))).IsSuccess);
                var values = new Dictionary<string, ThemeOptionValue> { ["volume"] = new(ThemeOptionKind.Range, "0.8") };
                var changed = (await service.PrepareAsync(new("", ThemeSourceKind.Directory, previewIdentity, values, revision))).Value;
                Require(changed.SourceIdentity == previewIdentity && changed.Options["volume"].Value == "0.8");
                Require((await service.SaveOptionsAsync(new(previewIdentity, values, revision))).IsSuccess);
                Require((await service.CommitAsync(new(changed.CandidateId, (await service.CurrentAsync(new())).Value.Revision, true))).IsSuccess);
                Require((await service.RevertAsync(new(preview.CandidateId))).IsSuccess);
                var resource = (await service.ReadResourceAsync(new(changed.CandidateId, "file(" + selected + ")", ThemeResourceKind.Icon))).Value;
                for (int index = 0; index < 10; index++) await Prepare(service, workspace.Theme("noise-" + index));
                Require((await service.CandidateAsync(new(changed.CandidateId))).IsSuccess && File.Exists(resource.PreparedPath));
                Require((await service.CurrentAsync(new())).Value.Selection?.SourceIdentity == committedIdentity);
                if (cancelPreview) Require((await service.RevertAsync(new(changed.CandidateId))).IsSuccess);
            }
            using var restored = workspace.Service(); var state = (await restored.CurrentAsync(new())).Value;
            Require(state.Selection?.SourceIdentity == committedIdentity && state.LastKnownGood?.SourceIdentity == committedIdentity);
            Require(!state.Grants.Any(grant => grant.SourceIdentity == previewIdentity) && !state.Options.Any(options => options.SourceIdentity == previewIdentity));
            Require((await restored.CatalogueAsync(new())).Value.Themes.All(theme => theme.SourceIdentity != previewIdentity));
        }
    }
    private static async Task CommittedSourcePreview()
    {
        using var workspace = new Workspace(); using var service = workspace.Service(); string path = workspace.Theme(); var initial = await Prepare(service, path); await Commit(service, initial);
        File.WriteAllText(Path.Combine(path, "styles", "main.nss"), ".card { Background:#abcdef; }");
        var preview = (await service.PrepareAsync(new(path, ThemeSourceKind.Directory, initial.SourceIdentity))).Value;
        Require((await service.CommitAsync(new(preview.CandidateId, (await service.CurrentAsync(new())).Value.Revision, true))).IsSuccess);
        var active = (await service.PrepareAsync(new("", ThemeSourceKind.Directory, initial.SourceIdentity))).Value;
        var committed = (await service.PrepareAsync(new("", ThemeSourceKind.Directory, initial.SourceIdentity) { UseCommittedSource = true })).Value;
        Require(active.SourceRevision == preview.SourceRevision && active.Styles[0].Text.Contains("#abcdef", StringComparison.Ordinal));
        Require(committed.SourceRevision == initial.SourceRevision && committed.Styles[0].Text.Contains("#123456", StringComparison.Ordinal));
        Require(!(await service.PrepareAsync(new("", ThemeSourceKind.Directory, preview.SourceIdentity, OptionsOverride: null) { UseCommittedSource = true, SourcePath = path })).IsSuccess);
    }
}
