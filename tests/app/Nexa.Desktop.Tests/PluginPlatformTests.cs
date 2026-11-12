using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Composition.Plugins;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static async Task ExercisePluginPlatform(Func<string, string, AbiValue, ValueTask<AbiReply<AbiValue>>> invoke, PluginBusinessRoutes business)
    {
        (string? Text, byte[]? Image) clipboard = (new string('x', 4096), null);
        business.BindPlatform(new((_, _, _, _) => Task.FromResult<string?>(null), _ => { }, _ => { }, _ => Task.FromResult(clipboard),
            (text, image, _) => { clipboard = (text, image); return Task.CompletedTask; }, _ => Task.FromResult(new[] { ("primary", 1920, 1080, 1.25) })));
        AssertTrue((await invoke("Platform", "Describe", Empty.Write(new()))).Success);
        var read = await invoke("Platform", "ReadClipboard", Empty.Write(new())); AssertTrue(read.Success); AssertEqual(4096, ClipboardContent.Read(read.Value).Text!.Length);
        AssertTrue((await invoke("Platform", "WriteClipboard", ClipboardContent.Write(new("", null)))).Success); AssertEqual("", clipboard.Text);
        AssertTrue((await invoke("Platform", "WriteClipboard", ClipboardContent.Write(new(null, null)))).Success); AssertEqual<string?>(null, clipboard.Text);
        var displays = await invoke("Platform", "QueryDisplay", Empty.Write(new())); AssertTrue(displays.Success);
        var display = Page<DisplayInfo>.Read(displays.Value).Items.Single(); AssertEqual(1920L, display.Width); AssertEqual(1.25, display.Scale);
        AssertFalse((await invoke("Platform", "WriteClipboard", DataCodec.Map(("text", new AbiValue.Utf8String(new string('x', 65537))), ("image", new AbiValue.Null())))).Success);
    }
}
