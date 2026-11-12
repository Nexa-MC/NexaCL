using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Themes;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private readonly Dictionary<Guid, (PluginIdentity Owner, ThemeResource Resource)> _themeResources = [];
    private IEnumerable<HostCapabilityRoute> ThemeResourceRoutes => [
        HostRoutes.Typed<ThemeOptionChange, OperationRef>("Themes", "MigrateOptions", (c, v) => ValueTask.FromResult(
            Services.StartHostOperation(c, "nexa.themes.write", Abi100Schemas.ThemeOptions, async token =>
                AbiReply<AbiValue>.Ok(ThemeOptions.Write(await SaveThemeOptions(c with { Options = c.Options with { Cancellation = token } }, v).ConfigureAwait(false)))))),
        HostRoutes.Typed<EntityRequest, Page<ThemeResource>>("Themes", "ListResources", async (c, v) =>
        {
            var theme = await FindTheme(c, v.Id).ConfigureAwait(false);
            var assets = await Query<ThemePackagedResourcesQuery, ThemePackagedResources>(foundationQueries, ThemesContract.PackagedResources,
                new(theme.SourceIdentity, theme.SourceRevision), c.Options.Cancellation).ConfigureAwait(false);
            var result = ImmutableArray.CreateBuilder<ThemeResource>();
            try
            {
                lock (_planGate)
                    foreach (var asset in assets.Items)
                    {
                        var blob = Application.AddBlob(c.Identity, asset.Bytes);
                        var resource = new ThemeResource(v.Id, asset.Name, blob);
                        _themeResources.Add(blob.Token, (c.Identity, resource)); result.Add(resource);
                    }
                return new(result.ToImmutable(), null, new(Token(c, "theme-resources:" + theme.SourceRevision)));
            }
            catch
            {
                lock (_planGate) foreach (var resource in result) { _themeResources.Remove(resource.Data.Token); Application.ReleaseBlob(c.Identity, resource.Data); }
                throw;
            }
        }),
        HostRoutes.Typed<ThemeResource, BlobRef>("Themes", "ReadResource", (c, v) =>
        {
            lock (_planGate)
            {
                if (!_themeResources.TryGetValue(v.Data.Token, out var entry) || entry.Owner != c.Identity || entry.Resource != v) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                return ValueTask.FromResult(v.Data);
            }
        }),
        HostRoutes.Typed<BlobRef, Empty>("Themes", "ReleaseResource", (c, v) =>
        {
            lock (_planGate)
            {
                if (!_themeResources.TryGetValue(v.Token, out var entry) || entry.Owner != c.Identity || entry.Resource.Data != v) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                Application.ReleaseBlob(c.Identity, v); _themeResources.Remove(v.Token); return ValueTask.FromResult(new Empty());
            }
        })
    ];
}
