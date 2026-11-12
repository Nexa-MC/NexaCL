using System.IO.Compression;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Sdk.Abi100.Data;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static async Task ExercisePluginContentTransfer(Func<string, string, AbiValue, ValueTask<AbiReply<AbiValue>>> invoke,
        Func<string, FileRef> grant, EntityId instance, string root)
    {
        string destination = Path.Combine(root, "plugin-content-export.zip");
        var file = grant(destination);
        var transfer = new ContentTransfer(instance, file, new("nexa:format/zip"), []);
        var response = await invoke("Minecraft.Content", "Export", ContentTransfer.Write(transfer)); AssertTrue(response.Success);
        var operation = OperationRef.Read(response.Value);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var result = await invoke("Tasks", "Read", EntityRequest.Write(new(operation.Id))); AssertTrue(result.Success);
            var record = OperationRecord.Read(result.Value);
            if (record.State == OperationState.Completed) break;
            if (record.State is OperationState.Failed or OperationState.Cancelled) throw new InvalidOperationException("Content export: " + record.Failure?.Code);
            await Task.Delay(5, timeout.Token);
        }
        using (var zip = ZipFile.OpenRead(destination))
        {
            AssertTrue(zip.Entries.Count > 0);
            AssertFalse(zip.Entries.Any(e => e.FullName.Contains(".nexa-plugin-", StringComparison.Ordinal)));
        }
        AssertFalse((await invoke("Minecraft.Content", "Export", ContentTransfer.Write(transfer))).Success);
        AssertFalse((await invoke("Minecraft.Content", "Export", ContentTransfer.Write(transfer with { File = grant(Path.Combine(root, "invalid-export.zip")), Selection = [new("forged")] }))).Success);
        AssertFalse(Directory.EnumerateFiles(root, ".nexa-plugin-export-*").Any());
    }
}
