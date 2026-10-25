using System.Diagnostics;
using System.Security.Cryptography;
using Nexa.Desktop.Ui;
using Nexa.Sidecar.Protocol;
using Nexa.Sidecar.Transport;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Runtime;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    // Explicit test-only preview. Normal product discovery still requires the release signature.
    private static async Task<int> RunNativeSidecarPreview(string executable)
    {
        if (Environment.GetEnvironmentVariable("NEXA_COUNTRY") is null) Environment.SetEnvironmentVariable("NEXA_COUNTRY", "CN");
        using var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var lifetimeStop = new CancellationTokenSource();
        await using var startup = await AvaloniaUiStartupSession.StartWithAppearanceAsync([], disableHardwareAcceleration: true,
            new AvaloniaUiStartupAppearance(XsrUiThemeMode.Light, ReducedMotion: false), cancellationToken: startupTimeout.Token);
        LaunchPageFixture? fixture = null;
        DesktopAppearanceSession? appearance = null;
        IDisposable? edge = null;
        DesktopSidecarNxml? adapter = null;
        var actions = new AvaloniaUiPlatformActions();
        actions.SetDesktopPolicy(new(TrayEnabled: false));
        using var listener = SidecarIpcListener.Bind("nsc-native-preview-" + Guid.NewGuid().ToString("N"));
        Process? process = null;
        SidecarHostSession? session = null;
        Task? receive = null;
        try
        {
            await startup.InvokeAsync(() =>
            {
                fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
                var f = fixture;
                appearance = new(f.Shell, f.Store, actions, XsrUiThemeMode.Light);
                f.Shell.Render(new(1024, 700));
                adapter = new(f.Store);
                edge = adapter.Attach(f.Shell, f.Intents, f.Store, Console.Error.WriteLine, f.Shell.Stage.Navigation.Current);
                f.Store.Publish(f.Store.Resolve(FirstRunState.NavigationVisible), false);
                f.Intents.IntentReceived += (_, args) =>
                {
                    if (args.Intent.Command.Value == "ui.sidecar.onboarding.continue")
                        f.Store.Publish(f.Store.Resolve(FirstRunState.NavigationVisible), true);
                };
            }, startupTimeout.Token);
            var accepted = listener.AcceptAsync(startupTimeout.Token).AsTask();
            var start = new ProcessStartInfo(Path.GetFullPath(executable))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true };
            start.ArgumentList.Add("--nexa-sidecar"); start.ArgumentList.Add("--endpoint"); start.ArgumentList.Add(listener.Endpoint);
            process = Process.Start(start) ?? throw new IOException("Could not start the NSC preview.");
            byte[] challenge = RandomNumberGenerator.GetBytes(32);
            await process.StandardInput.BaseStream.WriteAsync(challenge, startupTimeout.Token);
            process.StandardInput.Close();
            var stream = await accepted;
            await SidecarBootstrap.AuthenticateAsync(stream, challenge, startupTimeout.Token);
            session = new(new SidecarConnection(stream), "NSC development preview") { NxmlModules = adapter!.Runtime };
            await session.HandshakeAsync(startupTimeout.Token);
            await session.AcceptRegistrationAsync(startupTimeout.Token);
            await session.AcceptStateSnapshotAsync(startupTimeout.Token);
            await session.ActivateAsync(startupTimeout.Token);
            receive = session.RunReceiveLoopAsync(lifetimeStop.Token).AsTask();
            await startup.InvokeAsync(() => fixture!.Shell.Render(new(1024, 700)), startupTimeout.Token);
            await startup.PrepareShellAsync(fixture!.Shell, actions, startupTimeout.Token);
            await startup.WarmUpShellAsync(fixture.Shell, startupTimeout.Token);
            startupTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
            Console.WriteLine("NSC development preview: real binary peer, real authentication service, temporary Host workspace.");
            return await Task.Run(() => AvaloniaUiShellHost.Run(startup, fixture.Shell, actions));
        }
        finally
        {
            lifetimeStop.Cancel(); session?.Dispose();
            if (receive is not null)
                try { await receive; } catch (OperationCanceledException) { }
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                    catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                }
                process.Dispose();
            }
            edge?.Dispose(); appearance?.Dispose(); fixture?.Dispose();
        }
    }
}
