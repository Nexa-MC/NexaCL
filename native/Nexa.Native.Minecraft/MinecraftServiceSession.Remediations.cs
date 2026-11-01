using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Capabilities;
using Nexa.Services.Minecraft.Launch;

namespace Nexa.Native.Minecraft;

internal sealed partial class MinecraftServiceSession
{
    private MinecraftRemediationService? _remediations;
    private MinecraftRemediationService Remediations => _remediations ??= new(_settings, _store) { SharedStateCache = _cache };
    private async Task<byte[]> ExecuteRemediationAsync(ServiceWireReader reader, CancellationToken token)
    {
        var request = reader.RemediationRequest() ?? throw new InvalidDataException("Missing Minecraft remediation."); reader.End();
        var target = _remediations ?? throw new InvalidOperationException("Minecraft remediation is not configured.");
        var definition = RemediationCatalog.All.FirstOrDefault(item => item.Id == request.Id);
        RemediationResult result = definition is null ? new(request.Id, false, "unsupported", "此修复操作不可用。")
            : definition.RequiresConfirmation && !request.Confirmed ? new(request.Id, false, "confirmation_required", "此操作需要确认。")
            : !target.IsAvailable(request.Id) ? new(request.Id, false, "handler_unavailable", "当前环境没有可执行此操作的服务。")
            : await target.ExecuteAsync(request, token).ConfigureAwait(false);
        using var writer = new ServiceWireWriter(); writer.RemediationResult(result); return writer.Finish();
    }
}
