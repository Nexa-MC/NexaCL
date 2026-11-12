using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Accounts;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private XsrQueryRouter? _accountQueries;
    public void BindAccounts(XsrQueryRouter? queries) => _accountQueries = queries;
    private async ValueTask<AccountWardrobeSnapshot> AccountWardrobe(HostInvocation c, EntityId account, CancellationToken token)
    {
        var before = FindAccount(c, account);
        var wardrobe = await Query<AccountWardrobeQuery, AccountWardrobeSnapshot>(_accountQueries, AccountWardrobeContract.Read, new(), token).ConfigureAwait(false);
        var after = FindAccount(c, account);
        if (before.Revision != after.Revision || wardrobe.Profile.Index != after.Profile.Index || wardrobe.Profile.Uuid != after.Profile.Uuid || wardrobe.Profile.Kind != after.Profile.Kind)
            throw new AbiHostException(AbiErrors.CONFLICT);
        return wardrobe;
    }
    private static UriRef? PublicTexture(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0
        ? new(uri.AbsoluteUri) : null;
    private byte[] SkinBytes(HostInvocation c, SkinRequest request)
    {
        if (request.Model is not ("classic" or "slim")) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        var bytes = Application.CopyBlob(c.Identity, request.Image);
        if (bytes.Length is 0 or > 1048576) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
        return bytes;
    }
    private IEnumerable<HostCapabilityRoute> AccountAppearanceRoutes => [
        HostRoutes.Typed<EntityRequest, Wardrobe>("Accounts", "ReadWardrobe", async (c, v) =>
        {
            var wardrobe = await AccountWardrobe(c, v.Id, c.Options.Cancellation).ConfigureAwait(false);
            if (wardrobe.Capes.Count > 256) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            if (wardrobe.CapeState is AccountWardrobeCapeState.Loading or AccountWardrobeCapeState.LoadFailed) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            return new(v.Id, PublicTexture(wardrobe.Current?.SkinAddress ?? wardrobe.Profile.SkinAddress),
                wardrobe.Capes.Select(p => new Cape(p.Id, p.Name, PublicTexture(p.TextureAddress))).ToImmutableArray(), wardrobe.CanUploadSkin);
        }),
        HostRoutes.Typed<SkinRequest, BlobRef>("Accounts", "PreviewSkin", async (c, v) =>
        {
            var wardrobe = await AccountWardrobe(c, v.Account, c.Options.Cancellation).ConfigureAwait(false);
            var bytes = SkinBytes(c, v);
            string directory = Directory.CreateTempSubdirectory("nexa-plugin-skin-").FullName;
            try
            {
                string path = Path.Combine(directory, "skin.png");
                await File.WriteAllBytesAsync(path, bytes, c.Options.Cancellation).ConfigureAwait(false);
                var preview = await Query<AccountWardrobeSkinQuery, AccountWardrobeSkinPreview>(_accountQueries, AccountWardrobeContract.ValidateSkin,
                    new(wardrobe.Identity, path, v.Model == "slim"), c.Options.Cancellation).ConfigureAwait(false);
                return Application.AddBlob(c.Identity, preview.Image.Bytes.ToArray());
            }
            finally { Directory.Delete(directory, true); }
        }),
        HostRoutes.Typed<SkinRequest, OperationRef>("Accounts", "UploadSkin", async (c, v) =>
        {
            var wardrobe = await AccountWardrobe(c, v.Account, c.Options.Cancellation).ConfigureAwait(false);
            if (!wardrobe.CanUploadSkin) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            var bytes = SkinBytes(c, v);
            return Services.StartHostOperation(c, "nexa.accounts.write", Abi100Schemas.Empty, async token =>
            {
                await Confirm(c, "accounts/upload-skin", "Change the skin of " + wardrobe.Profile.Username, SkinRequest.Write(v), token).ConfigureAwait(false);
                FindAccount(c, v.Account);
                await Command(accountCommands, AccountWardrobeContract.UploadSkin, new AccountWardrobeUploadSkinCommand(wardrobe.Identity, bytes, "skin.png", v.Model == "slim"), token).ConfigureAwait(false);
                return AbiReply<AbiValue>.Ok(Empty.Write(new()));
            });
        }),
        HostRoutes.Typed<CapeSelection, OperationRef>("Accounts", "SetCape", async (c, v) =>
        {
            var wardrobe = await AccountWardrobe(c, v.Account, c.Options.Cancellation).ConfigureAwait(false);
            if (!wardrobe.CanChooseCape) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            if (v.CapeId is { } cape && !wardrobe.Capes.Any(p => p.Id == cape)) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            return Services.StartHostOperation(c, "nexa.accounts.write", Abi100Schemas.Empty, async token =>
            {
                await Confirm(c, "accounts/set-cape", "Change the cape of " + wardrobe.Profile.Username, CapeSelection.Write(v), token).ConfigureAwait(false);
                FindAccount(c, v.Account);
                await Command(accountCommands, AccountWardrobeContract.SetCape, new AccountWardrobeSetCapeCommand(wardrobe.Identity, v.CapeId), token).ConfigureAwait(false);
                return AbiReply<AbiValue>.Ok(Empty.Write(new()));
            });
        })
    ];
}
