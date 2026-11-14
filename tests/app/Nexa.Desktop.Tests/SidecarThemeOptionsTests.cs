using Nexa.Services.Composition.Sidecars;
using Nexa.Services.Themes;
using Nexa.Sidecar.Host;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;
using Options = Nexa.Sidecar.Host.ThemeOptions.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarThemeOptionsAbi()
    {
        using var fixture = new ThemeLifecycleFixture();
        string theme = fixture.MakeTheme("sidecar-options", "Text#TitleMain{Foreground:#102030;}");
        string manifest = Path.Combine(theme, "theme.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"options\":[]", """
            "optionsVersion":2,"options":[{"id":"enabled","kind":"Boolean","default":false,"label":{"en-US":"Enabled"}}]
            """, StringComparison.Ordinal));
        AssertTrue(fixture.Run(fixture.Session.ImportAsync(theme, ThemeSourceKind.Directory, false, CancellationToken.None)).IsSuccess);
        fixture.Pump(() => !fixture.Session.Snapshot.Busy);
        string identity = fixture.Current().Selection!.SourceIdentity;
        var (session, peer) = ActivateCaptionPatch(new(), "", declarations: [], features: SidecarFeatures.All).GetAwaiter().GetResult();
        using (session) using (peer)
        using (var api = new SidecarThemeOptionsApi(_ => session, new NoopDispatchObserver()))
        {
            var caller = new SidecarHostCaller(session.PluginName, session.SessionId);
            var routes = api.Routes.ToDictionary(route => route.ContractId);
            AssertFalse(routes[2048].Authorize(caller with { SessionId = Guid.NewGuid() }));
            AssertFalse(routes[2050].Authorize(caller with { PackageName = "another-package" }));
            SidecarHostRequestClient? wire = null;
            using var requests = wire = new(async (frame, token) =>
            {
                var request = SidecarDataMessages.DecodeBinaryRequest(frame.Payload.Span);
                var route = routes[request.ContractId];
                var result = route.Authorize(caller) ? await route.Invoke(caller, request.Value, token)
                    : XsrResult.Failure<SidecarBinaryValue>(XsrRuntimeErrors.RouteNotFound());
                AssertTrue(wire!.TryAcceptReply(new(SidecarProtocol.Version, route.IsQuery ? SidecarMessageType.QueryResult : SidecarMessageType.CommandResult,
                    SidecarFrameTraits.None, frame.CorrelationId, SidecarDataMessages.EncodeBinaryResult(result.IsSuccess,
                        result.IsSuccess ? result.Value : new(SidecarWireCodecs.GeneratedDto, []), result.Error?.Code.Value))));
            });
            requests.Activate(SidecarFeatures.All);
            var client = new SidecarThemeOptionsClient(requests, new NoopDispatchObserver());
            AssertTrue(client.Commands.TryResolve(Options.SidecarThemeOptionsRoutes.Save, out var save));
            AssertTrue(client.Commands.TryResolve(Options.SidecarThemeOptionsRoutes.Reset, out var reset));
            AssertTrue(client.Commands.TryResolve(Options.SidecarThemeOptionsRoutes.Import, out var import));
            Dictionary<string, Options.ThemeOptionValue> Values(string value) => new() { ["enabled"] = new(Options.ThemeOptionKind.Boolean, value) };
            Task<XsrResult> Save(long revision, string value = "true", CancellationToken token = default) => client.Commands.Dispatch(save,
                new Options.ThemeOptionsSaveCommand(identity, Values(value), revision), cancellationToken: token).Completion;
            Task<XsrResult> Import(string id, int schema) => client.Commands.Dispatch(import,
                new Options.HostThemeOptionsImport(identity, fixture.Current().Revision, id, schema, Values("true"))).Completion;
            string CurrentValue() => fixture.Current().Options.Single(value => value.SourceIdentity == identity).Values["enabled"].Value;
            AssertFalse(fixture.Run(Save(fixture.Current().Revision)).IsSuccess);
            AssertTrue(fixture.Run(client.DiscoverAsync().AsTask()).IsSuccess);
            AssertEqual("xsr.unavailable", fixture.Run(Save(fixture.Current().Revision)).Error!.Code.Value);
            var binding = api.Bind(new(fixture.Session.SaveOptionsAsync, fixture.Session.ResetOptionsAsync,
                (input, token) => fixture.Session.ImportOptionsAsync(new(input.SourceIdentity, input.Values, input.ExpectedRevision), input.ThemeId, input.SchemaVersion, token)));
            long revision = fixture.Current().Revision;
            AssertTrue(fixture.Run(Save(revision)).IsSuccess); AssertEqual("true", CurrentValue());
            AssertFalse(fixture.Run(Save(revision, "false")).IsSuccess); AssertEqual("true", CurrentValue());
            AssertTrue(fixture.Run(client.Commands.Dispatch(reset, new Options.ThemeOptionsResetCommand(identity, fixture.Current().Revision)).Completion).IsSuccess);
            AssertEqual("false", CurrentValue());
            fixture.Pump(() => !fixture.Session.Snapshot.Busy);
            revision = fixture.Current().Revision;
            long generation = fixture.Appearance.Current!.Generation; int leases = fixture.LeaseCount;
            AssertFalse(fixture.Run(Import("another.theme", 2)).IsSuccess);
            AssertFalse(fixture.Run(Import("fixture.sidecar-options", 3)).IsSuccess);
            AssertEqual(revision, fixture.Current().Revision); AssertEqual(generation, fixture.Appearance.Current!.Generation);
            AssertEqual(leases, fixture.LeaseCount); AssertEqual("false", CurrentValue());
            AssertTrue(fixture.Run(Import("fixture.sidecar-options", 2)).IsSuccess); AssertEqual("true", CurrentValue());
            foreach (string invalid in new[] { "yes", "true\n", new string('x', 4097) })
                AssertFalse(fixture.Run(Save(fixture.Current().Revision, invalid)).IsSuccess);
            AssertFalse(fixture.Run(Save(-1)).IsSuccess);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            AssertFalse(fixture.Run(Save(fixture.Current().Revision, token: cancelled.Token)).IsSuccess);
            binding.Dispose();
            fixture.Restart(safeMode: true);
            binding = api.Bind(new(fixture.Session.SaveOptionsAsync, fixture.Session.ResetOptionsAsync,
                (input, token) => fixture.Session.ImportOptionsAsync(new(input.SourceIdentity, input.Values, input.ExpectedRevision), input.ThemeId, input.SchemaVersion, token)));
            AssertEqual("themes.safe_mode", fixture.Run(Save(fixture.Current().Revision)).Error!.Code.Value);
            binding.Dispose();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<XsrResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            binding = api.Bind(new((_, _) => { entered.TrySetResult(); return release.Task; }, (_, _) => Task.FromResult(XsrResult.Success()), (_, _) => Task.FromResult(XsrResult.Success())));
            var pending = Save(fixture.Current().Revision);
            fixture.Pump(() => entered.Task.IsCompleted); AssertFalse(pending.IsCompleted);
            binding.Dispose();
            AssertEqual("xsr.cancelled", fixture.Run(pending).Error!.Code.Value);
            release.TrySetResult(XsrResult.Success());
            AssertEqual("xsr.unavailable", fixture.Run(Save(fixture.Current().Revision)).Error!.Code.Value);
            session.Dispose(); AssertFalse(fixture.Run(client.DiscoverAsync().AsTask()).IsSuccess);
            AssertFalse(fixture.Run(Save(fixture.Current().Revision)).IsSuccess);
        }
    }
}
