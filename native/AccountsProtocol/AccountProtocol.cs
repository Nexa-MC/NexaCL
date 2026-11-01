using Nexa.Native.Services.Wire;
using Nexa.Services.Accounts;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Native.Accounts.Wire;

internal static class AccountProtocol
{
    internal static string Identity => "nexa-accounts/1/" + ServiceWireSchema.Fingerprint + "/" + AccountRouteSchema.Fingerprint;
    internal static readonly XsrSemanticId[] Keys = [AccountStateContract.ProfilesKey, AccountStateContract.SelectedKey,
        AccountOnboardingState.Login, AccountOnboardingState.Characters, AccountOnboardingState.Imports, AccountSkinContract.SkinsKey];

    internal static ServiceWireWriter Request(int operation)
    {
        var writer = new ServiceWireWriter();
        writer.Int32(1); writer.String(Identity); writer.Int32(operation); return writer;
    }
    internal static ServiceWireReader Read(ReadOnlyMemory<byte> bytes, out int operation)
    {
        var reader = new ServiceWireReader(bytes);
        if (reader.Int32() != 1 || reader.String() != Identity)
        { reader.Dispose(); throw new InvalidDataException("Incompatible account service protocol."); }
        operation = reader.Int32(); return reader;
    }
    internal static void DeclareReplicas(XsrStateStoreBuilder builder)
    { foreach (var key in Keys) builder.Replica(key); }
}
