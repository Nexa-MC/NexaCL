using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Resources;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    private IResourceCatalogSource? _resourceCatalogSource;
    private bool _resourceCatalogAdmitted;
    public void AttachResourceCatalogSource(IResourceCatalogSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_resourceCatalogAdmitted || _resourceCatalogSource is not null) throw new InvalidOperationException("Resource source is already admitted.");
            _resourceCatalogSource = source;
        }
    }
    public async Task ConfigureResourcesAsync(string? favoritesPath, string? curseForgeKey = null, CancellationToken token = default)
    {
        bool reverse;
        lock (_gate) { ObjectDisposedException.ThrowIf(_closed, this); if (_resourceCatalogAdmitted) throw new InvalidOperationException("Resources are already admitted."); _resourceCatalogAdmitted = true; reverse = _resourceCatalogSource is not null; }
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.ResourceConfigure, writer =>
        { writer.String(favoritesPath); writer.String(curseForgeKey); writer.Boolean(reverse); }, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); reader.End();
    }
    private async Task<byte[]> ExecuteResourceSourceAsync(int operation, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        var source = Volatile.Read(ref _resourceCatalogSource) ?? throw new InvalidOperationException("Resource source is not attached.");
        using var reader = MinecraftServiceProtocol.Read(bytes, out _); using var writer = new ServiceWireWriter();
        switch (operation)
        {
            case MinecraftHostOperations.ResourceSearch:
                var search = reader.ResourceSearchQuery() ?? throw new InvalidDataException("Missing resource search."); reader.End();
                writer.ResourceSearchResult(await source.SearchAsync(search, token).ConfigureAwait(false)); break;
            case MinecraftHostOperations.ResourceDetail:
                var detail = reader.ResourceDetailQuery() ?? throw new InvalidDataException("Missing resource detail."); reader.End();
                writer.ResourceDetail(await source.DetailAsync(detail, token).ConfigureAwait(false)); break;
            case MinecraftHostOperations.ResourceChangelog:
                var changelog = reader.ResourceChangelogQuery() ?? throw new InvalidDataException("Missing resource changelog."); reader.End();
                writer.ResourceChangelog(source is IResourceChangelogSource logs ? await logs.ReadChangelogAsync(changelog, token).ConfigureAwait(false) : new("")); break;
            case MinecraftHostOperations.ResourceVersion:
                var command = reader.ResourceDownloadCommand() ?? throw new InvalidDataException("Missing resource version."); reader.End();
                writer.ResourceVersion(source is IResourceFileSource files ? await files.ReadVersionAsync(command, token).ConfigureAwait(false)
                    : (await source.DetailAsync(new(command.ProjectId) { Sources = [new(command.Provider, command.ProjectId)], MirrorFirst = command.MirrorFirst, Refresh = true }, token).ConfigureAwait(false))
                        .Versions.FirstOrDefault(version => version.Id == command.VersionId && version.Provider == command.Provider && version.ProjectId == command.ProjectId)); break;
            default: throw new InvalidDataException("Unknown resource source operation.");
        }
        return writer.Finish();
    }
}
