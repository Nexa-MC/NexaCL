using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Accounts;
using Nexa.Services.Minecraft.Launch;
using Nexa.Xsr;

namespace Nexa.Native.Minecraft;

internal sealed class RemoteMinecraftAccounts(ServiceEndpoint endpoint) : IAccountLaunchAccess
{
    public XsrResult<LaunchProfile> GetProfile(int index) => throw new NotSupportedException("Native launch requires asynchronous account access.");
    public async ValueTask<XsrResult<LaunchProfile>> GetProfileAsync(int index, CancellationToken cancellationToken = default)
    {
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.AccountProfile); request.Int32(index);
        byte[] bytes = await endpoint.InvokeAsync(request.Finish(), cancellationToken).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.LaunchProfile() ?? throw new InvalidDataException("Missing launch profile."))
            : XsrResult.Failure<LaunchProfile>(error); reader.End(); return result;
    }
    public async ValueTask<XsrResult<MinecraftLaunchIdentity>> ResolveAsync(int accountIndex, LaunchProfile profile, CancellationToken cancellationToken = default)
    {
        using var request = MinecraftServiceProtocol.Request(MinecraftHostOperations.AccountIdentity); request.Int32(accountIndex); request.LaunchProfile(profile);
        byte[] bytes = await endpoint.InvokeAsync(request.Finish(), cancellationToken).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); var error = reader.XsrError();
        var result = error is null ? XsrResult.Success(reader.MinecraftLaunchIdentity() ?? throw new InvalidDataException("Missing launch identity."))
            : XsrResult.Failure<MinecraftLaunchIdentity>(error); reader.End(); return result;
    }
}
