using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private readonly Dictionary<ulong, (PluginIdentity Owner, RegistrationRef Reference)> _themePreviews = [];
    private IEnumerable<HostCapabilityRoute> ThemeActivationRoutes => [
        HostRoutes.Typed<EntityRequest, ThemePreparation>("Themes", "Prepare", async (c, v) =>
        {
            var theme = await FindTheme(c, v.Id).ConfigureAwait(false); var current = await ThemeCurrent(c.Options.Cancellation).ConfigureAwait(false);
            async ValueTask Validate(CancellationToken token)
            {
                var latest = await FindTheme(c with { Options = c.Options with { Cancellation = token } }, v.Id).ConfigureAwait(false);
                if (latest.SourceRevision != theme.SourceRevision || (await ThemeCurrent(token).ConfigureAwait(false)).Revision != current.Revision) throw new AbiHostException(AbiErrors.CONFLICT);
            }
            var plan = Plan(c, "themes/activate", EntityRequest.Write(v), "Activate theme " + theme.Manifest.Id, Validate, async token =>
            {
                var activated = await ThemeEffects.Activate(theme.SourceIdentity, theme.SourceRevision, current.Revision, false, token).ConfigureAwait(false);
                if (!activated.IsSuccess) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
                return ThemeSummary.Write(ProjectTheme(c, theme, await ThemeCurrent(token).ConfigureAwait(false)));
            }, async (previewCaller, token) =>
            {
                var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
                var finished = new TaskCompletionSource<long?>(TaskCreationOptions.RunContinuationsAsynchronously);
                RegistrationRef? registration = null;
                registration = Services.RegisterHostCleanup(previewCaller, "nexa.themes.write", async cleanupToken =>
                {
                    lifetime.Cancel();
                    try
                    {
                        if (await finished.Task.WaitAsync(cleanupToken).ConfigureAwait(false) is { } generation)
                            Require(await ThemeEffects.Revert(generation, cleanupToken).ConfigureAwait(false));
                    }
                    finally { lifetime.Dispose(); if (registration is not null) lock (_watchGate) _themePreviews.Remove(registration.Handle); }
                });
                try
                {
                    lock (_watchGate) _themePreviews.Add(registration.Handle, (c.Identity, registration));
                    var activated = await ThemeEffects.Activate(theme.SourceIdentity, theme.SourceRevision, current.Revision, true, lifetime.Token).ConfigureAwait(false);
                    if (!activated.IsSuccess) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
                    finished.TrySetResult(activated.Value); return registration;
                }
                catch
                {
                    finished.TrySetResult(null);
                    try { await Services.ReleaseHostCleanup(previewCaller with { Options = previewCaller.Options with { Cancellation = default } }, "nexa.themes.write", registration).ConfigureAwait(false); }
                    catch (AbiHostException) { /* Load retirement may already own this cleanup. */ }
                    throw;
                }
            });
            return new(v.Id, plan, []);
        }),
        HostRoutes.Typed<PlanRequest, RegistrationRef>("Themes", "Preview", async (c, v) =>
        {
            PendingPlan plan;
            lock (_planGate)
            {
                if (!_plans.TryGetValue(v.Plan, out plan!) || plan.Owner != c.Identity || plan.Kind != "themes/activate" || plan.Preview is null) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                if (plan.Public.Revision != v.ExpectedRevision || plan.Expires < DateTimeOffset.UtcNow) throw new AbiHostException(AbiErrors.CONFLICT);
            }
            await plan.Validate(c.Options.Cancellation).ConfigureAwait(false);
            await Confirm(c, "themes/preview", plan.Public.Summary.MessageKey, MutationPlan.Write(plan.Public), c.Options.Cancellation).ConfigureAwait(false);
            await plan.Validate(c.Options.Cancellation).ConfigureAwait(false);
            return await plan.Preview(c, c.Options.Cancellation).ConfigureAwait(false);
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Themes", "Commit", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "themes/activate", "nexa.themes.write", Abi100Schemas.ThemeSummary))),
        HostRoutes.Typed<RegistrationRequest, Empty>("Themes", "Revert", async (c, v) =>
        {
            lock (_watchGate)
                if (!_themePreviews.TryGetValue(v.Registration.Handle, out var preview) || preview.Owner != c.Identity || preview.Reference != v.Registration) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            await Services.ReleaseHostCleanup(c, "nexa.themes.write", v.Registration).ConfigureAwait(false); return new Empty();
        })
    ];
}
