using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Settings;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft;

internal sealed partial class RemoteMinecraftSettings(ServiceEndpoint endpoint) : ISettingsPolicyReader, ISettingsSnapshotReader, ISettingsRenameTransactions, ISettingsRecoveryTransactions, ISettingsPolicyWriter
{
    public async ValueTask<XsrResult> SetAsync(SettingsMutation command, CancellationToken cancellationToken = default)
    {
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.SettingsPolicySet); request.SettingsMutation(command);
        byte[] bytes = await _endpoint.InvokeAsync(request.Finish(), cancellationToken).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End(); return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    private readonly ServiceEndpoint _endpoint = endpoint;
    public async ValueTask<XsrResult<SettingsRenameReceipt>> PrepareRenameAsync(string source, string destination, CancellationToken token = default)
    {
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.SettingsRenamePrepare); request.String(source); request.String(destination);
        byte[] bytes = await _endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.SettingsRenameReceipt() ?? throw new InvalidDataException("Missing rename settings identity."))
            : XsrResult.Failure<SettingsRenameReceipt>(error); reader.End();
        if (result.IsSuccess && (result.Value!.Identity.Length != 64 || !result.Value.Identity.All(char.IsAsciiHexDigit)))
            throw new InvalidDataException("Invalid rename settings identity.");
        return result;
    }
    public async ValueTask<XsrResult> ApplyRenameAsync(SettingsRenameReceipt receipt, bool reverse, CancellationToken token = default)
    {
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.SettingsRenameApply); request.SettingsRenameReceipt(receipt); request.Boolean(reverse);
        byte[] bytes = await _endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError(); reader.End();
        return error is null ? XsrResult.Success() : XsrResult.Failure(error);
    }
    public async ValueTask<CapturedSettingsSnapshot> CaptureAsync(CancellationToken token = default)
    {
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.SettingsSnapshot);
        byte[] bytes = await _endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes);
        var result = reader.CapturedSettingsSnapshot() ?? throw new InvalidDataException("Missing canonical settings snapshot.");
        reader.End(); return result;
    }
    public async ValueTask<XsrResult<SettingsEffectiveSnapshot>> ReadAsync(SettingsEffectiveQuery query, CancellationToken token = default)
    {
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.SettingsPolicyRead); request.SettingsEffectiveQuery(query);
        byte[] bytes = await _endpoint.InvokeAsync(request.Finish(), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.SettingsEffectiveSnapshot() ?? throw new InvalidDataException("Missing canonical settings policy."))
            : XsrResult.Failure<SettingsEffectiveSnapshot>(error); reader.End(); return result;
    }
}
