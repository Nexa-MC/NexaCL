using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Nexa.Xsr.Renderer.Backend.Avalonia;

namespace Nexa.Xsr.Renderer.Backend.Avalonia.Tests;

internal static partial class Program
{
    private static void MediaFramesRejectUnboundedInputAndOwnedDecoderPauses()
        => MediaFramesRejectUnboundedInputAndOwnedDecoderPausesAsync().GetAwaiter().GetResult();

    private static async Task MediaFramesRejectUnboundedInputAndOwnedDecoderPausesAsync()
    {
        VerifyWindowsMediaDelegateAbiAndNativeFailureClassification();
        byte[] oversized = new byte[16];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(oversized, 0);
        BinaryPrimitives.WriteUInt32BigEndian(oversized.AsSpan(8), uint.MaxValue);
        bool rejected = false;
        try { await AvaloniaUiLocalMediaPlayer.ReadFrameAsync(new MemoryStream(oversized), default); }
        catch (InvalidDataException) { rejected = true; }
        AssertTrue(rejected);
        AssertTrue(await AvaloniaUiLocalMediaPlayer.ReadFrameAsync(new MemoryStream(), default) is null);
        string directory = Path.Combine(Path.GetTempPath(), "nexa-local-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "local video.mp4");
            var start = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=blue:s=64x64:d=2", "-c:v", "mpeg4", "-threads", "1", "-y", path }) start.ArgumentList.Add(argument);
            Process? generator;
            try { generator = Process.Start(start); }
            catch (System.ComponentModel.Win32Exception) { Console.WriteLine("Optional ffmpeg decoder smoke: DependencyMissing."); return; }
            using (generator)
            {
                Task<string> stderr = generator!.StandardError.ReadToEndAsync();
                await generator.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                string errors = await stderr;
                if (generator.ExitCode != 0) throw new InvalidOperationException(errors);
            }
            var poster = await AvaloniaUiLocalMediaPlayer.ValidateThemeMediaAsync(path, video: true);
            AssertTrue(poster is { Width: > 0 and <= 960, Height: > 0 and <= 540 });
            string invalidPath = Path.Combine(directory, "invalid.mp4");
            await File.WriteAllTextAsync(invalidPath, "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000\nhttps://example.invalid/unadmitted\n");
            bool invalidMedia = false;
            try { await AvaloniaUiLocalMediaPlayer.ValidateThemeMediaAsync(invalidPath, video: true); }
            catch (InvalidDataException) { invalidMedia = true; }
            AssertTrue(invalidMedia);
            var firstFrame = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondFrame = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool acknowledgedSecond = false, staleAfterAcknowledgement = false;
            await using var player = new AvaloniaUiLocalMediaPlayer(true, image =>
            { AssertTrue(image.Width <= 960); AssertTrue(image.Height <= 540); firstFrame.TrySetResult(true); }, stampedFrame: frame =>
            {
                AssertEqual(path, frame.Path);
                if (frame.Generation == 8) secondFrame.TrySetResult(true);
                if (Volatile.Read(ref acknowledgedSecond) && frame.Generation == 7) Volatile.Write(ref staleAfterAcknowledgement, true);
            });
            await player.ConfigureGenerationAsync(7, path, 0, true);
            await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(10));
            AssertEqual(AvaloniaUiMediaStatus.Playing, player.State.Status);
            AssertEqual(7L, player.State.Generation);
            await player.ConfigureGenerationAsync(8, path, 0, true);
            Volatile.Write(ref acknowledgedSecond, true);
            await secondFrame.Task.WaitAsync(TimeSpan.FromSeconds(10));
            AssertFalse(Volatile.Read(ref staleAfterAcknowledgement)); AssertEqual(8L, player.State.Generation);
            await player.ConfigureGenerationAsync(8, path, 0, false);
            AssertEqual(AvaloniaUiMediaStatus.Paused, player.State.Status);
            await player.ConfigureAsync("", 0, false);
            AssertEqual(AvaloniaUiMediaStatus.Stopped, player.State.Status);
        }
        finally { Directory.Delete(directory, true); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SmtcButtonArguments { internal nint Table; internal int Button; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int ReadSmtcButton(nint self, int* button)
    { *button = ((SmtcButtonArguments*)self)->Button; return 0; }

    private static unsafe void VerifyWindowsMediaDelegateAbiAndNativeFailureClassification()
    {
        string? invoked = null;
        nint handler = AvaloniaUiWindowsMediaSession.CreateHandler(command => invoked = command);
        nint arguments = Marshal.AllocHGlobal(sizeof(SmtcButtonArguments));
        nint tableMemory = Marshal.AllocHGlobal(7 * sizeof(nint));
        try
        {
            nint* table = *(nint**)handler;
            var query = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)table[0];
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)table[2];
            var invoke = (delegate* unmanaged[Stdcall]<nint, nint, nint, int>)table[3];
            // Directly query the shipped unmanaged delegate with the Windows SDK IID,
            // rather than checking a copied production constant.
            Guid sdkHandler = new("0557e996-7b23-5bae-aa81-ea0d671143a4");
            nint requested = 0;
            AssertEqual(0, query(handler, &sdkHandler, &requested));
            AssertEqual(handler, requested); AssertEqual(1U, release(requested));
            Guid incorrectHandler = new("78b4539f-77de-5251-858b-cf9ecfe0f952");
            AssertEqual(unchecked((int)0x80004002), query(handler, &incorrectHandler, &requested));
            AssertEqual((nint)0, requested);
            foreach (Guid supported in new[] { new Guid("00000000-0000-0000-c000-000000000046"), new Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90") })
            {
                Guid id = supported;
                AssertEqual(0, query(handler, &id, &requested));
                AssertEqual(handler, requested); AssertEqual(1U, release(requested));
            }
            ((nint*)tableMemory)[6] = (nint)(delegate* unmanaged[Stdcall]<nint, int*, int>)&ReadSmtcButton;
            foreach (var (button, expected) in new[] { (0, "play"), (1, "pause"), (2, "stop"), (6, "next") })
            {
                *(SmtcButtonArguments*)arguments = new() { Table = tableMemory, Button = button };
                AssertEqual(0, invoke(handler, 0, arguments)); AssertEqual(expected, invoked);
            }
            AssertEqual(0U, release(handler)); handler = 0;
        }
        finally
        {
            if (handler != 0) _ = ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)handler)[2])(handler);
            Marshal.FreeHGlobal(arguments); Marshal.FreeHGlobal(tableMemory);
        }
        AvaloniaUiWindowsMediaSession.Check(0); AvaloniaUiWindowsMediaSession.Check(1);
        bool preserved = false;
        try { AvaloniaUiWindowsMediaSession.Check(unchecked((int)0x80004002)); }
        catch (AvaloniaUiWindowsMediaSession.NativeCallException error) { preserved = error.HResult == unchecked((int)0x80004002); }
        AssertTrue(preserved);
    }

    private static async Task VerifyNativeWindowsMediaControlsReadinessAsync(nint window)
    {
        var reports = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await using var session = new AvaloniaUiWindowsMediaSession(window, () => new(AvaloniaUiMediaStatus.Paused),
            () => false, _ => { }, reports.Enqueue);
        int result = await session.InitializationResult.WaitAsync(TimeSpan.FromSeconds(5));
        // A CI desktop may lack the optional SMTC runtime. Interface rejection is an
        // ABI regression, not an admissible dependency fallback.
        AssertTrue(result != unchecked((int)0x80004002));
        if (result < 0)
        {
            AssertTrue(reports.Any(report => report.Contains("InitializationFailed", StringComparison.Ordinal)
                || report.Contains("DependencyMissing", StringComparison.Ordinal)));
            Console.WriteLine($"SMTC native availability fallback: HRESULT 0x{result:X8}.");
        }
        else { AssertEqual(0, result); Console.WriteLine("PASS: actual Windows SMTC registers its typed delegate and initializes display metadata."); }
    }

    private static void MprisNativeBusPropertiesAndControlsUseRealAdapter()
        => MprisNativeBusPropertiesAndControlsUseRealAdapterAsync().GetAwaiter().GetResult();
    private static async Task MprisNativeBusPropertiesAndControlsUseRealAdapterAsync()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
        { Console.WriteLine("MPRIS native smoke requires dbus-run-session."); return; }
        var invoked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        double savedVolume = -1;
        var errors = new List<string>();
        await using var session = new AvaloniaUiMprisSession(() => new(AvaloniaUiMediaStatus.Playing), () => .37,
            (command, value) => { if (value is { } volume) savedVolume = volume; else invoked.TrySetResult(command); },
            message => { lock (errors) errors.Add(message); });
        string[] callPrefix = ["call", "--session", "--dest", "org.mpris.MediaPlayer2.nexacl", "--object-path", "/org/mpris/MediaPlayer2", "--timeout", "3", "--method"];
        async Task<(int ExitCode, string Output)> Call(params string[] tail)
        {
            var start = new ProcessStartInfo("gdbus") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in callPrefix.Concat(tail)) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            return (process.ExitCode, await stdout + await stderr);
        }
        (int ExitCode, string Output) playback = (-1, "");
        for (int attempt = 0; attempt < 20; attempt++)
        {
            playback = await Call("org.freedesktop.DBus.Properties.Get", "org.mpris.MediaPlayer2.Player", "PlaybackStatus");
            if (playback.ExitCode == 0) break;
            await Task.Delay(50);
        }
        AssertEqual(0, playback.ExitCode); AssertTrue(playback.Output.Contains("Playing", StringComparison.Ordinal));
        var all = await Call("org.freedesktop.DBus.Properties.GetAll", "org.mpris.MediaPlayer2.Player");
        AssertEqual(0, all.ExitCode); AssertTrue(all.Output.Contains("CanSeek", StringComparison.Ordinal));
        AssertEqual(0, (await Call("org.mpris.MediaPlayer2.Player.PlayPause")).ExitCode);
        AssertEqual("play-pause", await invoked.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        AssertEqual(0, (await Call("org.freedesktop.DBus.Properties.Set", "org.mpris.MediaPlayer2.Player", "Volume", "<0.6>")).ExitCode);
        AssertEqual(.6, savedVolume);
        lock (errors) AssertEqual(0, errors.Count);
    }
}
