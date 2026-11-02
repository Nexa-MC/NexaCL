using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr.State;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private InstallCatalogService? _catalog;
    private MinecraftInstallDraftService? _draft;
    private bool _reverseInstallationCatalog;
    private async Task<byte[]> ExecuteCatalogAsync(int operation, ServiceWireReader reader, CancellationToken token)
    {
        using var writer = new ServiceWireWriter();
        if (operation == MinecraftServiceProtocol.CatalogConfigure)
        {
            bool reverseSource = reader.Boolean(); reader.End(); await _initialization.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _closing), this);
                if (_catalog is not null) throw new InvalidOperationException("Minecraft catalog is already configured.");
                var source = reverseSource ? new CatalogSource(this) : _installationCatalogSource ?? throw new InvalidOperationException("Installation transport is not configured.");
                _reverseInstallationCatalog = reverseSource;
                _catalog = new(_store, source, _cache, () => reverseSource ? "reverse-install-catalog-v1" : "official-install-providers-v1|" + _installationRegion!.CountryCode,
                    informationCache: _catalogCacheDirectory is null || reverseSource ? null : new InstallCatalogInformationCache(_catalogCacheDirectory),
                    regionalPolicy: _installationRegion)
                { WorkScheduler = _work };
                _draft = new(_store); _draft.Apply(new(InstallDraftChangeKind.Reset));
            }
            finally { _initialization.Release(); }
            return writer.Finish();
        }
        var catalog = _catalog ?? throw new InvalidOperationException("Minecraft catalog is not configured.");
        switch (operation)
        {
            case MinecraftServiceProtocol.CatalogRead:
                var query = reader.InstallCatalogReadCommand() ?? throw new InvalidDataException("Missing catalog request."); reader.End();
                writer.XsrError((await catalog.ReadAsync(query, token).ConfigureAwait(false)).Error); break;
            case MinecraftServiceProtocol.CatalogPrefetch:
                var prefetch = reader.InstallCatalogPrefetchCommand() ?? throw new InvalidDataException("Missing catalog prefetch."); reader.End();
                writer.XsrError((await catalog.PrefetchAsync(prefetch, token).ConfigureAwait(false)).Error); break;
            case MinecraftServiceProtocol.CatalogEligibility:
                var eligibility = reader.InstallEligibilityQuery() ?? throw new InvalidDataException("Missing eligibility query."); reader.End();
                writer.InstallEligibilityResult(catalog.Evaluate(eligibility)); break;
            case MinecraftServiceProtocol.CatalogEditPlan:
                var edit = reader.MinecraftInstallEditPlanQuery() ?? throw new InvalidDataException("Missing installation edit plan."); reader.End();
                writer.MinecraftInstallEditPlan(MinecraftInstallEditPlanner.Evaluate(edit)); break;
            case MinecraftServiceProtocol.CatalogDraft:
                var change = reader.InstallDraftChange() ?? throw new InvalidDataException("Missing installation draft change."); reader.End();
                writer.XsrError(_draft!.Apply(change).Error); break;
            case MinecraftServiceProtocol.CatalogDraftReplace:
                var replacement = reader.InstallDraftReplace() ?? throw new InvalidDataException("Missing installation draft replacement."); reader.End();
                writer.XsrError(_draft!.Replace(replacement).Error); break;
            default: throw new InvalidDataException("Unknown installation catalog operation.");
        }
        return writer.Finish();
    }
    private bool PublishCatalogState(XsrStateChange change)
    {
        if (change.SemanticId == InstallCatalogStateContract.StateKey)
        {
            var captured = _store.Read<InstallCatalogState>(_store.Resolve(change.SemanticId));
            _publications.Post(45, captured.Revision, () =>
            {
                using var writer = MinecraftServiceProtocol.Request(MinecraftServiceProtocol.CatalogState);
                writer.Int64(captured.Revision); writer.Int32((int)captured.Availability); writer.Boolean(captured.HasValue);
                if (captured.HasValue) writer.InstallCatalogState(captured.Value); return writer.Finish();
            });
        }
        else if (change.SemanticId == MinecraftInstallDraftContract.StateKey)
        {
            var captured = _store.Read<MinecraftInstallDraftSnapshot>(_store.Resolve(change.SemanticId));
            _publications.Post(46, captured.Revision, () =>
            {
                using var writer = MinecraftServiceProtocol.Request(MinecraftServiceProtocol.DraftState);
                writer.Int64(captured.Revision); writer.Int32((int)captured.Availability); writer.Boolean(captured.HasValue);
                if (captured.HasValue) writer.MinecraftInstallDraftSnapshot(captured.Value); return writer.Finish();
            });
        }
        else return false;
        return true;
    }
    private sealed class InstallationCatalogSource(MinecraftServiceSession owner) : IInstallCatalogSource
    {
        private IInstallCatalogSource Source => owner._reverseInstallationCatalog ? new CatalogSource(owner) : owner._installationCatalogSource!;
        public Task<IReadOnlyList<InstallCatalogVersion>> GetGamesAsync(CancellationToken token) => Source.GetGamesAsync(token);
        public Task<IReadOnlyList<InstallCatalogVersion>> GetLoadersAsync(InstallLoader loader, string game, CancellationToken token) => Source.GetLoadersAsync(loader, game, token);
    }
    private sealed class CatalogSource(MinecraftServiceSession owner) : IInstallCatalogSource
    {
        public Task<IReadOnlyList<InstallCatalogVersion>> GetGamesAsync(CancellationToken token) => ReadAsync(null, "", token);
        public Task<IReadOnlyList<InstallCatalogVersion>> GetLoadersAsync(InstallLoader loader, string game, CancellationToken token) => ReadAsync(loader, game, token);
        private async Task<IReadOnlyList<InstallCatalogVersion>> ReadAsync(InstallLoader? loader, string game, CancellationToken token)
        {
            using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.CatalogSource); request.Nullable_InstallLoader(loader); request.String(game);
            byte[] bytes = await owner._endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
            using var reader = new ServiceWireReader(bytes); int count = reader.Count(); if (count < 0) throw new InvalidDataException("Missing catalog versions.");
            InstallCatalogVersion[] values = new InstallCatalogVersion[count];
            for (int i = 0; i < count; i++) values[i] = reader.InstallCatalogVersion() ?? throw new InvalidDataException("Missing catalog version.");
            reader.End(); return Array.AsReadOnly(values);
        }
    }
}
