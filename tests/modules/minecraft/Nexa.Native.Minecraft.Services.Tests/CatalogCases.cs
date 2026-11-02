using Nexa.Native.Minecraft.Client;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr.State;

internal static class CatalogCases
{
    internal static async Task RunAsync(NativeMinecraftClient client, XsrStateStore store, CancellationToken token)
    {
        client.AttachCatalogSource(new Source()); await client.ConfigureCatalogAsync(token);
        if (!(await client.ReadCatalogAsync(new(), token)).IsSuccess
            || !(await client.ReadCatalogAsync(new("1.20.1", InstallLoader.Fabric), token)).IsSuccess)
            throw new InvalidDataException("Native installation catalog rejected its source.");
        var catalog = store.Read<InstallCatalogState>(store.Resolve(InstallCatalogStateContract.StateKey), token);
        if (!catalog.HasValue || !catalog.Value.Catalogs.Any(item => item.Loader == InstallLoader.Fabric && item.Versions.Any(version => version.Id == "0.16.0")))
            throw new InvalidDataException("Native installation catalog lost a sibling result or source revision.");
        foreach (var change in new[] { new InstallDraftChange(InstallDraftChangeKind.SetChosen, Chosen: true),
            new(InstallDraftChangeKind.SetVersion, "1.20.1"), new(InstallDraftChangeKind.SetLoader, Loader: InstallLoader.Fabric),
            new(InstallDraftChangeKind.SetBuild, "0.16.0", InstallLoader.Fabric), new(InstallDraftChangeKind.AddAddon, Loader: InstallLoader.FabricApi) })
            if (!(await client.ChangeInstallationDraftAsync(change, token)).IsSuccess) throw new InvalidDataException("Native installation draft rejected a valid choice.");
        var draft = store.Read<MinecraftInstallDraftSnapshot>(store.Resolve(MinecraftInstallDraftContract.StateKey), token);
        if (!draft.HasValue || draft.Value.Builds.GetValueOrDefault(InstallLoader.Fabric) != "0.16.0" || !draft.Value.Addons.Contains(InstallLoader.FabricApi))
            throw new InvalidDataException("Native draft lost its enum-keyed selections.");
        var eligibility = await client.ReadInstallationEligibilityAsync(new("1.20.1", [new(InstallLoader.Fabric, "0.16.0")], InstallLoader.Fabric,
            InstallLoader.Fabric, ["0.16.0"]), token);
        if (!eligibility.Builds.ContainsKey("0.16.0")) throw new InvalidDataException("Native eligibility lost its typed result map.");
        foreach (bool duplicate in new[] { false, true })
        {
            using var writer = new ServiceWireWriter(); writer.Count(duplicate ? 2 : 1);
            writer.Int32(duplicate ? (int)InstallLoader.Fabric : int.MaxValue); writer.String("first");
            if (duplicate) { writer.Int32((int)InstallLoader.Fabric); writer.String("second"); }
            using var reader = new ServiceWireReader(writer.Finish()); bool rejected = false;
            try { _ = reader.IReadOnlyDictionary_InstallLoader_String(); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new InvalidDataException("Installation map accepted an undefined or duplicate enum key.");
        }
        Console.WriteLine("Native installation catalogs, draft enum maps and typed eligibility passed.");
    }
    private sealed class Source : IInstallCatalogSource
    {
        public async Task<IReadOnlyList<InstallCatalogVersion>> GetGamesAsync(CancellationToken token)
        { await Task.Yield(); token.ThrowIfCancellationRequested(); return [new("1.20.1", "fixture release")]; }
        public async Task<IReadOnlyList<InstallCatalogVersion>> GetLoadersAsync(InstallLoader loader, string game, CancellationToken token)
        { await Task.Yield(); token.ThrowIfCancellationRequested(); return loader == InstallLoader.Fabric ? [new("0.16.0", "fixture loader")] : []; }
    }
}
