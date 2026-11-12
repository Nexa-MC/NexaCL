using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Sdk.Abi100.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static async Task ExercisePluginDownloads(Func<string, string, AbiValue, ValueTask<AbiReply<AbiValue>>> invoke,
        Func<string, FileRef> grant, string directory)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        byte[] bytes = Encoding.UTF8.GetBytes(new string('a', 65536));
        string path = Path.Combine(directory, "plugin-download.bin");
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        string address = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/artifact";
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                string? line; do { line = await reader.ReadLineAsync(timeout.Token); } while (!string.IsNullOrEmpty(line));
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n"), timeout.Token);
                if (attempt == 0)
                {
                    await stream.WriteAsync(bytes.AsMemory(0, 128), timeout.Token); firstStarted.TrySetResult(true);
                    await releaseFirst.Task.WaitAsync(timeout.Token);
                }
                else await stream.WriteAsync(bytes, timeout.Token);
            }
        }, timeout.Token);
        try
        {
            var plan = new DownloadPlan([new(new(address), null, null)], grant(path), bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
            AssertTrue((await invoke("Downloads", "Resolve", DownloadPlan.Write(plan))).Success);
            var start = await invoke("Downloads", "Start", DownloadPlan.Write(plan)); AssertTrue(start.Success); var op = OperationRef.Read(start.Value);
            await firstStarted.Task.WaitAsync(timeout.Token);
            AssertTrue((await invoke("Downloads", "Pause", OperationRequest.Write(new(op.Id)))).Success);
            releaseFirst.TrySetResult(true);
            var paused = await invoke("Downloads", "Read", EntityRequest.Write(new(op.Id))); AssertTrue(paused.Success);
            AssertEqual(OperationState.Pending, DownloadState.Read(paused.Value).State); AssertFalse(File.Exists(path));
            AssertTrue((await invoke("Downloads", "Resume", OperationRequest.Write(new(op.Id)))).Success);
            AssertEqual(OperationState.Completed, await Wait(op));
            AssertTrue((await File.ReadAllBytesAsync(path, timeout.Token)).SequenceEqual(bytes));
            AssertFalse((await invoke("Downloads", "Resolve", DownloadPlan.Write(plan))).Success);
            AssertTrue(IntegrityReport.Read((await invoke("Downloads", "Verify", EntityRequest.Write(new(op.Id)))).Value).Valid);
            await File.WriteAllTextAsync(path, "changed", timeout.Token);
            AssertFalse(IntegrityReport.Read((await invoke("Downloads", "Verify", EntityRequest.Write(new(op.Id)))).Value).Valid);
            string rejected = Path.Combine(directory, "bad-download.bin");
            var bad = await invoke("Downloads", "Start", DownloadPlan.Write(plan with { Target = grant(rejected), Sha256 = new string('0', 64) })); AssertTrue(bad.Success);
            AssertEqual(OperationState.Failed, await Wait(OperationRef.Read(bad.Value))); AssertFalse(File.Exists(rejected));
            AssertFalse((await invoke("Downloads", "Cancel", OperationRequest.Write(new(new("forged"))))).Success);
            await server.WaitAsync(timeout.Token);
            AssertFalse(Directory.EnumerateFiles(directory, ".nexa-plugin-download-*").Any());
        }
        finally { releaseFirst.TrySetResult(true); await timeout.CancelAsync(); try { await server; } catch (OperationCanceledException) { } }
        async Task<OperationState> Wait(OperationRef op)
        {
            while (true)
            {
                var result = await invoke("Tasks", "Read", EntityRequest.Write(new(op.Id))); AssertTrue(result.Success);
                var state = OperationRecord.Read(result.Value).State;
                if (state is OperationState.Completed or OperationState.Failed or OperationState.Cancelled or OperationState.OutcomeUnknown) return state;
                await Task.Delay(5, timeout.Token);
            }
        }
    }
}
