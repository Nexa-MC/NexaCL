using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private static readonly QualifiedId SourceResolverKind = new("nexa:downloads/source-resolver");
    private static readonly QualifiedId TransferProviderKind = new("nexa:downloads/transfer");
    private static readonly BusinessProviderMethod SourceResolverMethod = new("Resolve", Abi100Schemas.DownloadSource, Abi100Schemas.DownloadSource);
    private static readonly BusinessProviderMethod TransferProviderMethod = new("ReadChunk", Abi100Schemas.DownloadChunkRequest, Abi100Schemas.DownloadChunk);
    private async ValueTask TransferProviderDownload(HostInvocation c, PluginDownload item, string stage, CancellationToken token)
    {
        var source = item.Plan.Sources.Single();
        long total = item.Plan.Size ?? throw new AbiHostException(AbiErrors.SCHEMA_INVALID), offset = 0;
        await using var output = new FileStream(stage, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
        var caller = c with { Options = c.Options with { Cancellation = token } };
        do
        {
            token.ThrowIfCancellationRequested();
            long requested = Math.Min(65536, total - offset);
            var chunk = DownloadChunk.Read(await Services.QueryHostProvider(caller, TransferProviderKind, source.Provider!, TransferProviderMethod,
                DownloadChunkRequest.Write(new(source.Uri, offset, requested))).ConfigureAwait(false));
            if (chunk.Offset != offset || chunk.Bytes is not AbiValue.Bytes bytes || bytes.Value.Length > requested
                || bytes.Value.Length == 0 && offset != total || chunk.End != (offset + bytes.Value.Length == total)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            await output.WriteAsync(bytes.Value.ToArray(), token).ConfigureAwait(false); offset += bytes.Value.Length;
            lock (_planGate) { item.Bytes = offset; item.Total = total; item.Revision++; }
            _changes.Writer.TryWrite(true);
        } while (offset < total);
        await output.FlushAsync(token).ConfigureAwait(false);
    }
}
