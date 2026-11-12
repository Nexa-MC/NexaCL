using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Settings;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private readonly Dictionary<ulong, (PluginIdentity Owner, RegistrationRef Reference)> _settingOverlays = [];
    private IEnumerable<HostCapabilityRoute> OverlayRoutes => [
        HostRoutes.Typed<SettingsOverlay, RegistrationRef>("Settings", "BeginTemporaryOverlay", async (c, v) =>
        {
            string instance = SettingsTarget(c, v.Target) ?? throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var before = await ReadProfiles(instance, c.Options.Cancellation).ConfigureAwait(false);
            if (before.TemporaryId is not null || v.Values.Any(s => s.Revision != PolicyRevision(c, before.Revision))) throw new AbiHostException(AbiErrors.CONFLICT);
            if (v.Values.Length is < 1 or > 256 || v.Values.Select(s => s.Id).Distinct().Count() != v.Values.Length) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var values = v.Values.ToDictionary(s => SettingKey(s.Id), s => ParseOverride(s.Value), StringComparer.Ordinal);
            await Confirm(c, "settings/temporary", "Apply temporary instance settings", SettingsOverlay.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            string id = "plugin-" + Guid.NewGuid().ToString("N");
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(c.Options.Cancellation);
            RegistrationRef? registration = null;
            registration = Services.RegisterHostCleanup(c, "nexa.settings.write", async token =>
            {
                lifetime.Cancel();
                try
                {
                    if (await finished.Task.WaitAsync(token).ConfigureAwait(false))
                    {
                        for (int attempt = 0; ; attempt++)
                        {
                            var current = await ReadProfiles(instance, token).ConfigureAwait(false);
                            if (current.TemporaryId != id) break;
                            try
                            {
                                await Command(foundationCommands, SettingsLaunchProfileContract.EndTemporary, new SettingsTemporaryLaunchEndCommand(instance, current.Revision), token).ConfigureAwait(false);
                                break;
                            }
                            catch (AbiHostException error) when (error.Code == AbiErrors.CONFLICT && attempt < 7) { }
                        }
                    }
                }
                finally
                {
                    lifetime.Dispose();
                    if (registration is not null) lock (_watchGate) _settingOverlays.Remove(registration.Handle);
                }
            });
            try
            {
                lock (_watchGate) _settingOverlays.Add(registration.Handle, (c.Identity, registration));
                await Command(foundationCommands, SettingsLaunchProfileContract.BeginTemporary,
                    new SettingsTemporaryLaunchBeginCommand(instance, id, values, new(), before.Revision), lifetime.Token).ConfigureAwait(false);
                finished.TrySetResult(true); return registration;
            }
            catch
            {
                // Inspect the owner after a cancelled dispatch: it may already have committed.
                finished.TrySetResult(true);
                try { await Services.ReleaseHostCleanup(c with { Options = c.Options with { Cancellation = default } }, "nexa.settings.write", registration).ConfigureAwait(false); }
                catch (AbiHostException) { /* Retirement owns cleanup if the load has already stopped. */ }
                throw;
            }
        }),
        HostRoutes.Typed<RegistrationRequest, Empty>("Settings", "EndTemporaryOverlay", async (c, v) =>
        {
            lock (_watchGate)
                if (!_settingOverlays.TryGetValue(v.Registration.Handle, out var entry) || entry.Owner != c.Identity || entry.Reference != v.Registration) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            await Services.ReleaseHostCleanup(c, "nexa.settings.write", v.Registration).ConfigureAwait(false); return new Empty();
        })
    ];
}
