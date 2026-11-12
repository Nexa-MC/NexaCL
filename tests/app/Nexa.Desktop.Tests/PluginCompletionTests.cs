using System.Collections.Immutable;
using System.Security.Cryptography;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Composition.Plugins;
using Nexa.Services.Minecraft;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private sealed class CompletionPluginEntry(string id) : IPluginBootstrapV1
    {
        internal ABIContext? Context;
        internal readonly Dictionary<string, Func<AbiValue, ValueTask<AbiReply<AbiValue>>>> Callbacks = [];
        internal readonly Dictionary<string, ulong> Handlers = [];
        internal Func<Abi100Context, Task>? Initialize;
        private static readonly ImmutableArray<string> Permissions = Abi100Catalog.Operations.Select(o => o.Permission).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToImmutableArray();
        internal static InvocationOptions Options() => new(Guid.NewGuid(), TimeSpan.FromSeconds(10), default) { IdempotencyKey = Guid.NewGuid().ToString("N") };
        internal PluginLoadSource Source() => new(new(1, id, "1.0.0", 1, new("test", "test"), new([], [], "fixture"), [100], [], [], [], Permissions, []), Permissions, _ => ValueTask.FromResult<IPluginBootstrapV1>(this), () => ValueTask.CompletedTask);
        public async ValueTask<AbiReply<PreInitResult>> OnPreInitialize(PreInitContext context)
        {
            foreach (var callback in Callbacks) Handlers.Add(callback.Key, context.Callbacks.Register(callback.Key, (input, _) => callback.Value(input)).Value);
            var handle = await context.Negotiator.GetABIContext(100, Options()); AssertTrue(handle.Success);
            var draft = new Abi100Draft(handle.Value);
            foreach (string contract in Abi100Catalog.Operations.Select(o => o.ContractId).Distinct()) draft = draft.Require(contract);
            var made = await draft.Make(context.Negotiator, Guid.NewGuid(), Permissions, default); AssertTrue(made.Success);
            Context = made.Value; return AbiReply<PreInitResult>.Ok(new(Context, [], [], [], []));
        }
        public async ValueTask<AbiReply<Unit>> OnInitialize(ABIContext context, StageContext stage)
        { using var api = context.ForStage100(stage); if (Initialize is not null) { try { await Initialize(api); } catch (Exception error) { Console.Error.WriteLine(error); throw; } } return AbiReply<Unit>.Ok(new()); }
        public ValueTask<AbiReply<Unit>> OnPostInitialize(ABIContext context, StageContext stage) => Ok();
        public ValueTask<AbiReply<CheckupResult>> OnCheckup(ABIContext context, StageContext stage) => ValueTask.FromResult(AbiReply<CheckupResult>.Ok(new(CheckupStatus.Passed, [], [])));
        public ValueTask<AbiReply<Unit>> OnFinalize(ABIContext context, StageContext stage) => Ok();
        public ValueTask<AbiReply<Unit>> OnActivated(ABIContext context, InvocationContext invocation) => Ok();
        public ValueTask<AbiReply<Unit>> OnStop(StopContext context) => Ok();
        public ValueTask<AbiReply<Unit>> OnAbort(AbortContext context) => Ok();
        private static ValueTask<AbiReply<Unit>> Ok() => ValueTask.FromResult(AbiReply<Unit>.Ok(new()));
    }
    private static T CompletionValue<T>(AbiReply<T> reply)
    { if (!reply.Success) throw new InvalidOperationException("Plugin completion: " + reply.Error); return reply.Value; }
    private static async Task<OperationRecord> CompletionTerminal(Abi100Context api, AbiReply<OperationRef> reply)
    {
        var reference = CompletionValue(reply); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var record = CompletionValue(await api.Tasks.Read(new(reference.Id), CompletionPluginEntry.Options()));
            if (record.State is OperationState.Completed or OperationState.Failed or OperationState.Cancelled or OperationState.OutcomeUnknown) return record;
            await Task.Delay(5, timeout.Token);
        }
    }
    private static void PluginCompletionProviders() => PluginCompletionProvidersAsync().GetAwaiter().GetResult();
    private static async Task PluginCompletionProvidersAsync()
    {
        var descriptors = new List<MinecraftInstanceDescriptor>();
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource(descriptors), addProfile: true);
        string directory = Path.Combine(fixture.TemporaryDirectory, "minecraft", "versions", "provider"); Directory.CreateDirectory(directory);
        var item = Instance("provider"); descriptors.Add(item with { DirectoryPath = directory, Version = item.Version with { DirectoryPath = directory } });
        await fixture.Library.Service.RefreshAsync();
        await using var business = new PluginBusinessRoutes(fixture.Store, fixture.Library.Commands, fixture.Onboarding.Commands, foundationCommands: fixture.Foundation.Commands, foundationQueries: fixture.Foundation.Queries);
        business.BindAccounts(fixture.Onboarding.Queries); business.BindDownloads(fixture.Foundation.Host.Downloads);
        var confirmation = new BackupConfirmation();
        await using var app = new PluginApplication(Path.Combine(fixture.TemporaryDirectory, "completion-providers"), (_, _) => throw new NotSupportedException(), business.Routes, confirmation, (_, _) => { }, (_, _) => { }, _ => { });
        business.BindApplication(app); business.BindConfirmation(confirmation); business.Bind(app.Runtime.Services);
        var entry = new CompletionPluginEntry("example.completion"); var consumer = new CompletionPluginEntry("example.completion-consumer");
        var options = CompletionPluginEntry.Options; bool corrupt = false; int callbacks = 0;
        byte[] payload = "provider-controlled bytes"u8.ToArray();
        QualifiedId Id(string name) => new("example.completion:" + name);
        entry.Callbacks.Add("provider", async input =>
        {
            callbacks++; var call = BusinessProviderCall.Read(input); AssertTrue(Abi100Schemas.Validate(call.InputSchema, call.Inputs));
            using var api = entry.Context!.AsAbi100();
            AssertEqual(AbiErrors.STAGE_DENIED, (await api.Tasks.CreateOwned(new(Id("effect"), new("forbidden", []), new("parent"), true), options())).Error!.Code);
            AbiValue result = call.Kind.Value switch
            {
                "nexa:diagnostics/rules" => DiagnosticAnalysis.Write(new(corrupt ? new("forged") : InstanceSummary.Read(call.Inputs).Id, [], [])),
                "nexa:content/format" => ContentInspection.Write(new(Id("format"), [new("name", new AbiValue.Utf8String(ContentInspectionInput.Read(call.Inputs).Name))], [])),
                "nexa:catalog/translation" => CatalogTranslation.Write(new("Translated " + CatalogTranslationInput.Read(call.Inputs).Locale, "Translated summary")),
                "nexa:accounts/authentication" => AccountProviderConfiguration.Write(new("yggdrasil", new(corrupt ? "file:///private" : "https://auth.example.test/api/yggdrasil/"))),
                "nexa:install/loader-catalog" => Page<LoaderVersion>.Write(new([new(new("nexa:loader/fabric"), "0.16.0", LoaderVersionRequest.Read(call.Inputs).GameVersion, true)], null, new("one"))),
                "nexa:java/distributions" => Page<JavaDistribution>.Write(new([new(Id("java"), "21.0.1", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux", new([new(new("https://example.test/java.zip"), null, null)], new(Guid.NewGuid(), Guid.NewGuid(), "unused"), 100, new string('a', 64)))], null, new("one"))),
                "nexa:downloads/source-resolver" => DownloadSource.Write(new(new("https://example.test/resolved"), Id("transfer"), null)),
                "nexa:downloads/transfer" => Chunk(DownloadChunkRequest.Read(call.Inputs)),
                _ => throw new InvalidOperationException(call.Kind.Value)
            };
            AssertTrue(Abi100Schemas.Validate(call.OutputSchema, result)); return AbiReply<AbiValue>.Ok(result);
        });
        AbiValue Chunk(DownloadChunkRequest request)
        {
            AssertEqual("https://example.test/resolved", request.Source.Value);
            var data = payload.Skip((int)request.Offset).Take((int)request.Length).ToImmutableArray();
            return DownloadChunk.Write(new(corrupt ? request.Offset + 1 : request.Offset, new AbiValue.Bytes(data), request.Offset + data.Length == payload.Length));
        }
        entry.Initialize = async api =>
        {
            ProviderDescriptor Provider(string name, string kind, params string[] methods) => new(Id(name), new(kind), 100, Abi100Schemas.BusinessProviderCall.Digest, new(name, []), [.. methods], ["application"], SelectionPolicy.ExplicitRequest, ConcurrencyPolicy.Parallel, 5000, ProviderFailurePolicy.FailOperation, Abi100Schemas.Empty, entry.Handlers["provider"]);
            CompletionValue(await api.Minecraft.Diagnostics.RegisterRuleProvider(Provider("diagnostic", "nexa:diagnostics/rules", "Analyze"), options()));
            CompletionValue(await api.Minecraft.Content.RegisterFormatProvider(Provider("format", "nexa:content/format", "Inspect"), options()));
            CompletionValue(await api.Catalog.RegisterTranslationProvider(Provider("translation", "nexa:catalog/translation", "Translate"), options()));
            CompletionValue(await api.Catalog.RegisterCollection(new(Id("collection"), [new(new("nexa:modrinth"), "example")], []), options()));
            CompletionValue(await api.Accounts.RegisterProvider(Provider("account", "nexa:accounts/authentication", "ResolveSignIn"), options()));
            CompletionValue(await api.Minecraft.Install.RegisterLoaderProvider(Provider("loader", "nexa:install/loader-catalog", "ListVersions"), options()));
            CompletionValue(await api.Minecraft.Java.RegisterDistributionProvider(Provider("java", "nexa:java/distributions", "ListDistributions"), options()));
            CompletionValue(await api.Downloads.RegisterSourceResolver(Provider("resolver", "nexa:downloads/source-resolver", "Resolve"), options()));
            CompletionValue(await api.Downloads.RegisterTransferProvider(Provider("transfer", "nexa:downloads/transfer", "ReadChunk"), options()));
        };
        foreach (var load in await app.Runtime.LoadAsync([entry.Source(), consumer.Source(), new CatalogPluginEntry("example.catalog").Source()])) if (load.State != LoadState.Active) throw new InvalidOperationException("Completion load: " + load.Error);
        using var api = consumer.Context!.AsAbi100();
        var instance = CompletionValue(await api.Minecraft.Instances.List(new(null, new(null, 16)), options())).Items.Single();
        AssertEqual(instance.Id, CompletionValue(await api.Minecraft.Diagnostics.AnalyzeWithProvider(new(Id("diagnostic"), instance.Id), options())).Instance);
        corrupt = true; AssertFalse((await api.Minecraft.Diagnostics.AnalyzeWithProvider(new(Id("diagnostic"), instance.Id), options())).Success); corrupt = false;
        string file = Path.Combine(directory, "sample.nbt"); await File.WriteAllBytesAsync(file, payload);
        var owner = app.Runtime.Active.Single(p => p.PluginId == "example.completion-consumer");
        var grant = app.Files.GrantPath(owner, file);
        AssertEqual(Id("format"), CompletionValue(await api.Minecraft.Content.InspectWithProvider(new(Id("format"), grant), options())).Format);
        using (var other = entry.Context!.AsAbi100()) AssertFalse((await other.Minecraft.Content.InspectWithProvider(new(Id("format"), grant), options())).Success);
        AssertEqual(Id("collection"), CompletionValue(await api.Catalog.ListCollections(new(null, 16), options())).Items.Single().Id);
        AssertEqual(Id("collection"), CompletionValue(await api.Catalog.ReadCollection(new(Id("collection")), options())).Id);
        AssertEqual("Translated zh-CN", CompletionValue(await api.Catalog.ReadTranslatedProject(new(Id("translation"), new(CatalogPluginEntry.Provider, "first"), "zh-CN"), options())).Name);
        var signin = CompletionValue(await api.Accounts.BeginSignIn(new(Id("account"), "application"), options()));
        AssertEqual(SignInState.Challenge, signin.State); CompletionValue(await api.Accounts.CancelSignIn(new(signin.Flow), options()));
        corrupt = true; AssertFalse((await api.Accounts.BeginSignIn(new(Id("account"), "application"), options())).Success); corrupt = false;
        AssertEqual("0.16.0", CompletionValue(await api.Minecraft.Install.ListLoaderVersions(new(Id("loader"), "1.21.1", new(null, 16)), options())).Items.Single().Version);
        var distribution = CompletionValue(await api.Minecraft.Java.ListDistributions(new(null, 16), options())).Items.Single();
        var javaPlan = CompletionValue(await api.Minecraft.Java.PrepareInstall(new(distribution, instance.Root), options()));
        AssertTrue(javaPlan.Summary.MessageKey.Contains(new string('a', 64), StringComparison.Ordinal));
        string target = Path.Combine(fixture.TemporaryDirectory, "provider.bin");
        var download = new DownloadPlan([new(new("https://example.test/original"), Id("resolver"), null)], app.Files.GrantPath(owner, target, true), payload.Length, Convert.ToHexString(SHA256.HashData(payload)));
        var resolved = CompletionValue(await api.Downloads.Resolve(download, options())); AssertEqual(Id("transfer"), resolved.Sources.Single().Provider);
        var transferred = await CompletionTerminal(api, await api.Downloads.Start(download, options())); AssertEqual(OperationState.Completed, transferred.State);
        byte[] downloadedBytes = await File.ReadAllBytesAsync(target); AssertTrue(payload.SequenceEqual(downloadedBytes));
        target = Path.Combine(fixture.TemporaryDirectory, "invalid-provider.bin"); corrupt = true;
        // Resolver still resolves, but a forged chunk offset cannot publish any target.
        var invalid = await CompletionTerminal(api, await api.Downloads.Start(download with { Target = app.Files.GrantPath(owner, target, true) }, options()));
        AssertEqual(OperationState.Failed, invalid.State); AssertFalse(File.Exists(target)); corrupt = false;
        await app.Runtime.StopAsync("example.completion");
        AssertFalse((await api.Minecraft.Diagnostics.AnalyzeWithProvider(new(Id("diagnostic"), instance.Id), options())).Success);
        AssertEqual(0, CompletionValue(await api.Catalog.ListCollections(new(null, 16), options())).Items.Length);
        AssertEqual(0, CompletionValue(await api.Minecraft.Java.ListDistributions(new(null, 16), options())).Items.Length);
        AssertTrue(callbacks >= 10);
    }
    private static void PluginCompletionSettings() => PluginCompletionSettingsAsync().GetAwaiter().GetResult();
    private static async Task PluginCompletionSettingsAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        await using var business = new PluginBusinessRoutes(fixture.Store, fixture.Library.Commands, fixture.Onboarding.Commands);
        var confirmation = new BackupConfirmation();
        await using var app = new PluginApplication(Path.Combine(fixture.TemporaryDirectory, "completion-settings"), (_, _) => throw new NotSupportedException(), business.Routes, confirmation, (_, _) => { }, (_, _) => { }, _ => { });
        business.BindApplication(app); business.BindConfirmation(confirmation); business.Bind(app.Runtime.Services);
        var options = CompletionPluginEntry.Options;
        var query = new SettingQuery(new("example.settings:title"), new(new("nexa:application"), null, "global"));
        CompletionPluginEntry Entry(ulong version)
        {
            var entry = new CompletionPluginEntry("example.settings");
            entry.Callbacks.Add("migrate", async input =>
            {
                var migration = SettingMigrationInput.Read(input); AssertEqual(1UL, migration.FromVersion); AssertEqual(2UL, migration.ToVersion);
                using var callbackApi = entry.Context!.AsAbi100();
                AssertEqual(AbiErrors.STAGE_DENIED, (await callbackApi.Tasks.CreateOwned(new(new("example.settings:effect"), new("forbidden", []), new("parent"), true), options())).Error!.Code);
                return AbiReply<AbiValue>.Ok(TextResult.Write(new(TextResult.Read(migration.Value).Text + " upgraded")));
            });
            entry.Initialize = async api =>
            {
                var schema = new SettingSchema(query.Id, Abi100Schemas.TextResult, TextResult.Write(new("default")), ["global"]);
                if (version == 1) CompletionValue(await api.Settings.RegisterSchema(schema, options()));
                else CompletionValue(await api.Settings.RegisterVersionedSchema(new(schema, version), options()));
            };
            return entry;
        }
        var entry = Entry(1); AssertEqual(LoadState.Active, (await app.Runtime.LoadAsync([entry.Source()])).Single().State);
        using (var api = entry.Context!.AsAbi100())
        {
            var initial = CompletionValue(await api.Settings.GetEffective(query, options())); AssertEqual("default", TextResult.Read(initial.Value).Text);
            var plan = CompletionValue(await api.Settings.PreviewChange(new(query.Id, query.Target, TextResult.Write(new("saved")), initial.Revision), options()));
            AssertEqual(OperationState.Completed, (await CompletionTerminal(api, await api.Settings.Apply(new(plan.Id, plan.Revision, null), options()))).State);
            AssertEqual("saved", TextResult.Read(CompletionValue(await api.Settings.GetEffective(query, options())).Value).Text);
            AssertFalse((await api.Settings.PreviewChange(new(query.Id, query.Target, TextResult.Write(new("stale")), initial.Revision), options())).Success);
        }
        await app.Runtime.StopAsync("example.settings"); entry = Entry(2); AssertEqual(LoadState.Active, (await app.Runtime.LoadAsync([entry.Source()])).Single().State);
        using (var api = entry.Context!.AsAbi100())
        {
            AssertFalse((await api.Settings.GetEffective(query, options())).Success);
            var migration = new SettingMigration(query.Id, query.Target, 1, 2, entry.Handlers["migrate"]);
            confirmation.Allow = false; AssertEqual(OperationState.Failed, (await CompletionTerminal(api, await api.Settings.Migrate(migration, options()))).State);
            confirmation.Allow = true; AssertEqual(OperationState.Completed, (await CompletionTerminal(api, await api.Settings.Migrate(migration, options()))).State);
            AssertEqual("saved upgraded", TextResult.Read(CompletionValue(await api.Settings.GetEffective(query, options())).Value).Text);
            AssertFalse((await api.Settings.Migrate(migration, options())).Success);
        }
        await app.Runtime.StopAsync("example.settings"); entry = Entry(2); AssertEqual(LoadState.Active, (await app.Runtime.LoadAsync([entry.Source()])).Single().State);
        using var reloaded = entry.Context!.AsAbi100(); AssertEqual("saved upgraded", TextResult.Read(CompletionValue(await reloaded.Settings.GetEffective(query, options())).Value).Text);
    }
    private static void PluginCompletionInstances() => PluginCompletionInstancesAsync().GetAwaiter().GetResult();
    private static async Task PluginCompletionInstancesAsync()
    {
        var descriptors = new List<MinecraftInstanceDescriptor>();
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource(descriptors));
        string root = Path.Combine(fixture.TemporaryDirectory, "minecraft"), directory = Path.Combine(root, "versions", "source");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "source.json"), """{"id":"source","mainClass":"net.minecraft.client.main.Main","_minecraftVersion":"1.21.1","libraries":[]}""");
        await File.WriteAllTextAsync(Path.Combine(directory, "source.jar"), "game-jar");
        await new MinecraftInstanceMetadataStore().SaveAsync(directory, new() { InstanceIsolation = true });
        await File.WriteAllTextAsync(Path.Combine(directory, "options.txt"), "original-options");
        var descriptor = Instance("source"); descriptors.Add(descriptor with { DirectoryPath = directory, Version = descriptor.Version with { DirectoryPath = directory } });
        await fixture.Library.Service.RefreshAsync();
        await using var business = new PluginBusinessRoutes(fixture.Store, fixture.Library.Commands, fixture.Onboarding.Commands);
        var confirmation = new BackupConfirmation();
        await using var app = new PluginApplication(Path.Combine(fixture.TemporaryDirectory, "completion-instances"), (_, _) => throw new NotSupportedException(), business.Routes, confirmation, (_, _) => { }, (_, _) => { }, _ => { });
        business.BindApplication(app); business.BindConfirmation(confirmation); business.Bind(app.Runtime.Services);
        var entry = new CompletionPluginEntry("example.instances"); AssertEqual(LoadState.Active, (await app.Runtime.LoadAsync([entry.Source()])).Single().State);
        using var api = entry.Context!.AsAbi100(); var options = CompletionPluginEntry.Options;
        async Task<InstanceSummary> Source() => CompletionValue(await api.Minecraft.Instances.List(new(null, new(null, 16)), options())).Items.Single(i => i.Name == "source");
        var source = await Source();
        AssertFalse((await api.Minecraft.Instances.PrepareClone(new(source.Id, "../escape", source.Root, source.Revision), options())).Success);
        var clone = CompletionValue(await api.Minecraft.Instances.PrepareClone(new(source.Id, "copy", source.Root, source.Revision), options()));
        await fixture.Library.Service.RefreshAsync();
        AssertEqual(OperationState.Failed, (await CompletionTerminal(api, await api.Minecraft.Instances.CommitClone(new(clone.Id, clone.Revision, null), options()))).State);
        source = await Source(); clone = CompletionValue(await api.Minecraft.Instances.PrepareClone(new(source.Id, "copy", source.Root, source.Revision), options()));
        confirmation.Allow = false;
        AssertEqual(OperationState.Failed, (await CompletionTerminal(api, await api.Minecraft.Instances.CommitClone(new(clone.Id, clone.Revision, null), options()))).State);
        AssertFalse(Directory.Exists(Path.Combine(root, "versions", "copy")));
        confirmation.Allow = true; clone = CompletionValue(await api.Minecraft.Instances.PrepareClone(new(source.Id, "copy", source.Root, source.Revision), options()));
        var completed = await CompletionTerminal(api, await api.Minecraft.Instances.CommitClone(new(clone.Id, clone.Revision, null), options()));
        if (completed.State != OperationState.Completed) throw new InvalidOperationException("Clone: " + completed.Failure);
        string copied = Path.Combine(root, "versions", "copy");
        AssertEqual("original-options", await File.ReadAllTextAsync(Path.Combine(copied, "options.txt")));
        AssertEqual("game-jar", await File.ReadAllTextAsync(Path.Combine(copied, "copy.jar")));
        using (var manifest = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(copied, "copy.json")))) AssertEqual("copy", manifest.RootElement.GetProperty("id").GetString());
        AssertTrue((await new MinecraftInstanceMetadataStore().LoadAsync(copied)).InstanceIsolation);
        AssertFalse((await api.Minecraft.Instances.CommitClone(new(clone.Id, clone.Revision, null), options())).Success);
        source = await Source(); AssertFalse((await api.Minecraft.Instances.PrepareClone(new(source.Id, "copy", source.Root, source.Revision), options())).Success);
        // Publish a new discovery snapshot for the deterministic fixture source.
        var copyDescriptor = Instance("copy"); descriptors.Add(copyDescriptor with { DirectoryPath = copied, Version = copyDescriptor.Version with { DirectoryPath = copied } });
        await fixture.Library.Service.RefreshAsync();
        var copy = CompletionValue(await api.Minecraft.Instances.List(new(null, new(null, 16)), options())).Items.Single(i => i.Name == "copy");
        var deletion = CompletionValue(await api.Minecraft.Instances.PrepareDelete(new(copy.Id, copy.Revision, true), options()));
        AssertEqual(OperationState.Completed, (await CompletionTerminal(api, await api.Minecraft.Instances.CommitDelete(new(deletion.Id, deletion.Revision, null), options()))).State);
        AssertFalse(Directory.Exists(copied)); AssertTrue(Directory.EnumerateDirectories(Path.Combine(root, ".recycle")).Any());
        AssertTrue(Directory.Exists(directory)); descriptors.RemoveAt(1); await fixture.Library.Service.RefreshAsync();
        string outside = Path.Combine(fixture.TemporaryDirectory, "outside"); Directory.CreateDirectory(outside); await File.WriteAllTextAsync(Path.Combine(outside, "keep"), "keep");
        if (!OperatingSystem.IsWindows()) Directory.CreateSymbolicLink(Path.Combine(directory, "linked-data"), outside);
        source = await Source(); deletion = CompletionValue(await api.Minecraft.Instances.PrepareDelete(new(source.Id, source.Revision, false), options()));
        AssertEqual(OperationState.Completed, (await CompletionTerminal(api, await api.Minecraft.Instances.CommitDelete(new(deletion.Id, deletion.Revision, null), options()))).State);
        AssertFalse(Directory.Exists(directory)); AssertEqual("keep", await File.ReadAllTextAsync(Path.Combine(outside, "keep")));
    }

}
