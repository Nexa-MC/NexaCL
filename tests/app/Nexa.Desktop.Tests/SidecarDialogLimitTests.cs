using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static void SidecarDialogLimits() => SidecarDialogLimitsAsync().GetAwaiter().GetResult();
    private static async Task SidecarDialogLimitsAsync()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store);
        List<string> warnings = [];
        using var view = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add);
        List<SidecarHostSession> sessions = [];
        List<SidecarConnection> peers = [];
        try
        {
            foreach (var group in new[] { ("First", 9), ("Second", 8), ("Third", 1) })
            {
                var declarations = Enumerable.Range(0, group.Item2).Select(index =>
                {
                    string key = group.Item1 + index;
                    return PageDeclaration("test.limit." + key, "#" + key, SidecarNxmlOperation.Dialog,
                        "<Page Key=\"" + key + "\"><Text Content=\"Bounded modal\" /></Page>");
                }).ToArray();
                var (session, peer) = await ActivateCaptionPatch(new(), "", declarations: declarations,
                    features: SidecarFeatures.All, nxml: adapter);
                sessions.Add(session); peers.Add(peer);
            }
            fixture.Shell.Renderer.ReducedMotion = true;
            void Frame() => fixture.Shell.Render(new(1000, 750));
            int OpenCount() => fixture.Shell.Stage.Overlays.Count(node => fixture.Shell.Tree.GetComponent<XsrUiOverlayLayer>(node)?.IsModal == true);
            async Task Change(int session, int document, bool open, bool accepted = true)
            {
                var task = adapter.ChangeDialogAsync(sessions[session], 0, document, open, default).AsTask();
                AssertEqual(1, adapter.PendingDialogControls); Frame();
                try { await task; AssertTrue(accepted); }
                catch (InvalidOperationException) when (!accepted) { }
            }
            Frame();
            for (int index = 0; index < 8; index++) await Change(0, index, true);
            AssertEqual(8, OpenCount());
            await Change(0, 8, true, accepted: false);
            // Idempotent open remains valid at the per-session limit.
            await Change(0, 0, true); AssertEqual(8, OpenCount());
            for (int index = 0; index < 8; index++) await Change(1, index, true);
            AssertEqual(16, OpenCount());
            await Change(2, 0, true, accepted: false);
            await Change(0, 0, false);
            await Change(2, 0, true); AssertEqual(16, OpenCount());
            sessions[1].Dispose(); Frame(); AssertEqual(8, OpenCount());
            await Change(0, 8, true); AssertEqual(9, OpenCount());
            AssertEqual(0, warnings.Count);
        }
        finally
        {
            foreach (var session in sessions) session.Dispose();
            foreach (var peer in peers) peer.Dispose();
        }
    }
}
