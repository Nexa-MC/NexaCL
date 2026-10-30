using System.Security.Cryptography;
using System.Text;
using Nexa.Xsr;
using Nexa.Xsr.State;
namespace Nexa.Services.Accounts;

public static class AccountSkinContract
{
    public const string OwnerName = "Nexa.Services.AccountSkins";

    public static readonly XsrSemanticId SkinsKey = XsrSemanticId.Parse("accounts.skins");
    public static readonly XsrSemanticId RefreshRoute = XsrSemanticId.Parse("accounts.skins.refresh");
    public static void DeclareState(XsrStateStoreBuilder builder) =>
        builder.Collection<AccountSkinSnapshot, string>(SkinsKey, OwnerName, static item => item.ProfileKey);

    public static string ProfileKey(LaunchProfileView profile) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{profile.Kind}\n{profile.Uuid}\n{profile.AuthServer}\n{profile.SkinAddress}\n{profile.Username}")));
}
