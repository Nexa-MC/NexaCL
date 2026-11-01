using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    private IInstallCatalogSource? _catalogSource;
    private bool _catalogAdmitted;
    private Task? _catalogConfiguration;
    public Task EnsureCatalogConfiguredAsync(CancellationToken token = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _catalogConfiguration ??= ConfigureCatalogAsync(CancellationToken.None);
            return _catalogConfiguration.WaitAsync(token);
        }
    }
    public void AttachCatalogSource(IInstallCatalogSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (ReferenceEquals(_catalogSource, source)) return;
            if (_catalogAdmitted) throw new InvalidOperationException("Minecraft catalog admission already began.");
            if (_catalogSource is not null) throw new InvalidOperationException("Minecraft catalog source is already attached.");
            _catalogSource = source;
        }
    }
    public static void DeclareCatalogReplicas(XsrStateStoreBuilder builder)
    {
        builder.Replica(InstallCatalogStateContract.StateKey); builder.Replica(MinecraftInstallDraftContract.StateKey);
    }
    public async Task ConfigureCatalogAsync(CancellationToken token = default)
    {
        bool reverse;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_catalogAdmitted) throw new InvalidOperationException("Minecraft catalog is already admitted.");
            _catalogAdmitted = true; reverse = _catalogSource is not null;
        }
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.CatalogConfigure, writer => writer.Boolean(reverse), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); reader.End();
    }
    public Task<XsrResult> ReadCatalogAsync(InstallCatalogReadCommand command, CancellationToken token = default)
        => LaunchResultAsync(MinecraftServiceProtocol.CatalogRead, writer => writer.InstallCatalogReadCommand(command), token);
    public Task<XsrResult> PrefetchCatalogAsync(InstallCatalogPrefetchCommand command, CancellationToken token = default)
        => LaunchResultAsync(MinecraftServiceProtocol.CatalogPrefetch, writer => writer.InstallCatalogPrefetchCommand(command), token);
    public Task<XsrResult> ChangeInstallationDraftAsync(InstallDraftChange change, CancellationToken token = default)
        => LaunchResultAsync(MinecraftServiceProtocol.CatalogDraft, writer => writer.InstallDraftChange(change), token);
    public Task<XsrResult> ReplaceInstallationDraftAsync(InstallDraftReplace change, CancellationToken token = default)
        => LaunchResultAsync(MinecraftServiceProtocol.CatalogDraftReplace, writer => writer.InstallDraftReplace(change), token);
    public async Task<InstallEligibilityResult> ReadInstallationEligibilityAsync(InstallEligibilityQuery query, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.CatalogEligibility, writer => writer.InstallEligibilityQuery(query), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var result = reader.InstallEligibilityResult() ?? throw new InvalidDataException("Missing installation eligibility."); reader.End(); return result;
    }
    public async Task<MinecraftInstallEditPlan> ReadInstallationPlanAsync(MinecraftInstallEditPlanQuery query, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.CatalogEditPlan, writer => writer.MinecraftInstallEditPlanQuery(query), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var result = reader.MinecraftInstallEditPlan() ?? throw new InvalidDataException("Missing installation edit plan."); reader.End(); return result;
    }
    private async Task<byte[]> ExecuteCatalogSourceAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = MinecraftServiceProtocol.Read(bytes, out _); var loader = reader.Nullable_InstallLoader(); string game = reader.String() ?? ""; reader.End();
        var source = Volatile.Read(ref _catalogSource) ?? throw new InvalidOperationException("Minecraft catalog source is not attached.");
        var versions = loader is { } selected ? await source.GetLoadersAsync(selected, game, token).ConfigureAwait(false) : await source.GetGamesAsync(token).ConfigureAwait(false);
        using var writer = new ServiceWireWriter(); writer.Count(versions.Count); foreach (var version in versions) writer.InstallCatalogVersion(version); return writer.Finish();
    }
    private byte[] ApplyCatalogState(int operation, ServiceWireReader reader)
    {
        long revision = reader.Int64(); var availability = (XsrStateAvailability)reader.Int32(); bool hasValue = reader.Boolean();
        if (revision <= 0 || availability is not (XsrStateAvailability.Available or XsrStateAvailability.Unavailable or XsrStateAvailability.Stale))
            throw new InvalidDataException("Invalid installation catalog state.");
        if (operation == MinecraftServiceProtocol.CatalogState)
        {
            var value = hasValue ? reader.InstallCatalogState() ?? throw new InvalidDataException("Missing installation catalog.") : default!;
            reader.End(); if (_replicas is { } store) store.Replicate(store.Resolve(InstallCatalogStateContract.StateKey), revision, availability, hasValue, value);
        }
        else
        {
            var value = hasValue ? reader.MinecraftInstallDraftSnapshot() ?? throw new InvalidDataException("Missing installation draft.") : default!;
            reader.End(); if (_replicas is { } store) store.Replicate(store.Resolve(MinecraftInstallDraftContract.StateKey), revision, availability, hasValue, value);
        }
        return [];
    }
}
