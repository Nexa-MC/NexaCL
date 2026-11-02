using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Accounts;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient
{
    private IAccountLaunchAccess? _accounts;
    public void AttachAccounts(IAccountLaunchAccess accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_accounts is not null) throw new InvalidOperationException("Canonical launch accounts are already attached.");
            _accounts = accounts;
        }
    }
    private async Task<byte[]> ExecuteAccountsAsync(int operation, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        using var reader = MinecraftServiceProtocol.Read(bytes, out _); int index = reader.Int32();
        using var writer = new ServiceWireWriter();
        var accounts = Volatile.Read(ref _accounts) ?? throw new InvalidOperationException("Canonical launch accounts are not attached.");
        if (operation == MinecraftHostOperations.AccountProfile)
        {
            reader.End(); var result = await accounts.GetProfileAsync(index, token).ConfigureAwait(false);
            writer.XsrError(result.Error); if (result.IsSuccess) writer.LaunchProfile(result.Value);
        }
        else
        {
            var profile = reader.LaunchProfile() ?? throw new InvalidDataException("Missing launch profile."); reader.End();
            var result = await accounts.ResolveAsync(index, profile, token).ConfigureAwait(false);
            writer.XsrError(result.Error); if (result.IsSuccess) writer.MinecraftLaunchIdentity(result.Value);
        }
        return writer.Finish();
    }
}
