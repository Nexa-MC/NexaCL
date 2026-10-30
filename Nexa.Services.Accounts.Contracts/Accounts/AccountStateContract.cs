using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Accounts;

public static class AccountStateContract
{
    public const string OwnerName = "Nexa.Services.Accounts";

    public static readonly XsrSemanticId ProfilesKey = XsrSemanticId.Parse("accounts.profiles");
    public static readonly XsrSemanticId SelectedKey = XsrSemanticId.Parse("accounts.selected");

    public static void DeclareState(XsrStateStoreBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Collection<LaunchProfileView, int>(ProfilesKey, OwnerName, static view => view.Index);
        builder.Cell<int>(SelectedKey, OwnerName);
    }
}
