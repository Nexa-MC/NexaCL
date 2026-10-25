using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using System.Diagnostics;
using System.Security.Cryptography;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static async Task SidecarProcessFixtureRegistersInertly(string executable)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store);
        using var listener = SidecarIpcListener.Bind("sidecar-contract-" + Guid.NewGuid().ToString("N"));
        var accepted = listener.AcceptAsync(stop.Token).AsTask();
        var start = new ProcessStartInfo(Path.GetFullPath(executable))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--nexa-sidecar"); start.ArgumentList.Add("--endpoint"); start.ArgumentList.Add(listener.Endpoint);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the Sidecar fixture.");
        try
        {
            byte[] challenge = RandomNumberGenerator.GetBytes(32);
            await process.StandardInput.BaseStream.WriteAsync(challenge, stop.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await using var stream = await accepted.ConfigureAwait(false);
            await SidecarBootstrap.AuthenticateAsync(stream, challenge, stop.Token).ConfigureAwait(false);
            using var connection = new SidecarConnection(stream);
            using var session = new SidecarHostSession(connection, "NativeFixture") { NxmlModules = adapter.Runtime };
            await session.HandshakeAsync(stop.Token); await session.AcceptRegistrationAsync(stop.Token); await session.AcceptStateSnapshotAsync(stop.Token);
            AssertEqual(SidecarSessionState.Ready, session.State);
            AssertTrue(session.NegotiatedFeatures.HasFlag(SidecarFeatures.Pages));
            AssertTrue(session.Registration!.Entries.Count(entry => entry.Kind == SidecarRegistrationKind.UiModule) >= 3);
            await connection.SendAsync(new(SidecarProtocol.Version, SidecarMessageType.Shutdown, SidecarFrameTraits.None, SidecarCorrelationId.Create(), ReadOnlyMemory<byte>.Empty), stop.Token);
            await process.WaitForExitAsync(stop.Token).ConfigureAwait(false);
            AssertEqual(0, process.ExitCode);
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().ConfigureAwait(false); } }
    }

    private static void SidecarNxmlFixtureCompilesAndNavigates(string directory)
    {
        static byte[][] Read(string path)
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            List<byte[]> records = [];
            while (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                int length = reader.ReadInt32();
                if (length is < 1 or > 1024 * 1024 || length > reader.BaseStream.Length - reader.BaseStream.Position)
                    throw new InvalidDataException("Invalid Sidecar fixture record.");
                records.Add(reader.ReadBytes(length));
                if (records.Count > 256) throw new InvalidDataException("Fixture exceeds declaration budget.");
            }
            return records.ToArray();
        }
        var declarations = Read(Path.Combine(directory, "declarations.bin")).Select(bytes => SidecarRegistration.DecodeItem(bytes)).ToArray();
        var states = Read(Path.Combine(directory, "states.bin")).Select(bytes => SidecarStateSnapshot.DecodeItem(bytes)).ToArray();
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        var adapter = new DesktopSidecarNxml(fixture.Store);
        var fallback = fixture.Shell.Stage.Navigation.Current;
        List<string> warnings = [];
        using var edge = adapter.Attach(fixture.Shell, fixture.Intents, fixture.Store, warnings.Add, fallback);
        var (session, peer) = ActivateCaptionPatch(new(), "", declarations: declarations, features: SidecarFeatures.All,
            initialState: states, nxml: adapter).GetAwaiter().GetResult();
        using (session) using (peer)
        {
            fixture.Shell.Renderer.ReducedMotion = true;
            var scene = fixture.Shell.Render(new(1024, 600));
            Console.WriteLine("First-run extension: " + fixture.Shell.Tree.Name(fixture.Shell.Stage.Navigation.Current));
            var next = scene.Nodes.Single(node => fixture.Shell.Tree.GetComponent<XsrUiCommandBinding>(node.Entity)?.Command.Value == "ui.sidecar.onboarding.continue");
            AssertTrue(next.Rect.Y >= XsrUiShell.TitleBarHeight && next.Rect.Y + next.Rect.Height <= 600);
            AssertTrue(fixture.Shell.Renderer.Activate(next.Entity));
            scene = fixture.Shell.Render(new(1024, 760));
            var open = scene.Nodes.Single(node => fixture.Shell.Tree.GetComponent<XsrUiCommandBinding>(node.Entity)?.Command.Value == "ui.sidecar.page.open");
            var toggle = scene.Nodes.Single(node => node.Entity == fixture.Shell.NavigationToggle);
            AssertTrue(open.Rect.Y + open.Rect.Height <= toggle.Rect.Y);
            AssertTrue(toggle.Rect.Y > 650);
            AssertTrue(fixture.Shell.Tree.GetComponent<XsrUiVisualStyle>(open.Entity)!.NavigationLayout);
            AssertEqual("lucide/user", fixture.Shell.Tree.GetComponent<XsrUiImage>(open.Entity)!.Source);
            AssertEqual("未登录", open.Text);
            AssertFalse(scene.Nodes.Any(node => node.Text == "市场"));
            AssertTrue(fixture.Shell.Renderer.Activate(open.Entity));
            fixture.Shell.Render(new(1024, 760));
            var root = fixture.Shell.Stage.Navigation.Current;
            Console.WriteLine("Registered page: " + fixture.Shell.Tree.Name(root));
            AssertEqual(0, fixture.Shell.Tree.Children(root).Count);
            AssertEqual(new XsrUiColor(255, 255, 255, 255), fixture.Shell.Tree.GetComponent<XsrUiVisualStyle>(root)!.Background);
            var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Store.Changed += change =>
            {
                if (change.Id != adapter.State) return;
                var modules = fixture.Store.Read<XsrNxmlModuleSnapshot>(adapter.State).Value!;
                if (modules.Modules.Any(module => module.Presentation.Artifact is Nexa.Xsr.Renderer.Nxml.NxmlPreparedDocument prepared
                    && prepared.Root.Root.Key == "NexaMcMarketNavigation" && prepared.Root.Root.IsVisible)) refreshed.TrySetResult();
            };
            var mirror = session.Mirror!.Store;
            mirror.Publish(mirror.Resolve(XsrSemanticId.Parse("nsc.account.name")), "Fixture User");
            mirror.Publish(mirror.Resolve(XsrSemanticId.Parse("nsc.account.signed_in")), "true");
            refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            scene = fixture.Shell.Render(new(1024, 760));
            var market = scene.Nodes.Single(node => node.Text == "市场");
            var settings = scene.Nodes.Single(node => node.Entity == fixture.Shell.NavigationEntities[XsrSemanticId.Parse("navigation.settings")]);
            AssertTrue(market.Rect.Y + market.Rect.Height <= settings.Rect.Y);
            AssertTrue(scene.Nodes.Any(node => node.Text == "Fixture User"));
            AssertTrue(fixture.Shell.Renderer.Activate(market.Entity));
            fixture.Shell.Render(new(1024, 760));
            AssertEqual("NexaMcMarketPage", fixture.Shell.Tree.Name(fixture.Shell.Stage.Navigation.Current));
            Console.WriteLine("Authenticated market appears above Settings and opens its registered page.");
            session.Dispose(); fixture.Shell.Render(new(1024, 760));
            AssertFalse(fixture.Shell.Tree.IsAlive(root)); AssertEqual(0, warnings.Count);
        }
    }
}
