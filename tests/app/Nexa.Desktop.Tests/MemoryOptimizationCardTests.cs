using Nexa.Services.Memory;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Tests;

internal static partial class Program
{
    private static readonly string[] MemoryContrastKeys = ["MemoryTitle", "MemoryUsage", "MemoryAutomatic", "MemoryThresholdLabel", "MemoryDeep", "MemoryDetail"];
    private static void MemoryOptimizationCardRoutesSettingsAndManualCleanup()
    {
        var port = new CardMemoryPort();
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]), memoryPort: port);
        fixture.Shell.Renderer.ReducedMotion = true;
        Emit(fixture.Intents, "ui.launch.widget.memory");
        var scene = fixture.Shell.Render(new(1024, 700));
        AssertEqual(3, fixture.Shell.Tree.GetComponent<XsrUiPager>(FindEntity(fixture.Shell, "LaunchWidgetPager"))!.PageIndex);
        AssertTrue(HasKey(fixture.Shell, scene, "MemoryClean"));
        AssertTrue(HasKey(fixture.Shell, scene, "MemoryDeep"));
        AssertTrue(FindByKey(fixture.Shell, scene, "MemoryClean").IsClickable);
        Emit(fixture.Intents, "ui.launch.memory.automatic");
        Emit(fixture.Intents, "ui.launch.memory.deep");
        for (int frame = 0; frame < 50; frame++)
        {
            fixture.Shell.Render(new(1024, 700));
            if (fixture.Foundation.Host.Settings.GetRawValue(MemoryOptimizationContract.DeepSetting).Value == "true") break;
            Thread.Sleep(10);
        }
        AssertEqual("true", fixture.Foundation.Host.Settings.GetRawValue(MemoryOptimizationContract.AutomaticSetting).Value);
        AssertEqual("true", fixture.Foundation.Host.Settings.GetRawValue(MemoryOptimizationContract.DeepSetting).Value);
        fixture.Shell.Renderer.SetRangeValue(FindEntity(fixture.Shell, "MemoryThreshold"), 87);
        Emit(fixture.Intents, "ui.launch.memory.threshold");
        fixture.Shell.Render(new(1024, 700));
        AssertEqual("87", fixture.Foundation.Host.Settings.GetRawValue(MemoryOptimizationContract.ThresholdSetting).Value);
        Emit(fixture.Intents, "ui.launch.memory.clean");
        for (int frame = 0; frame < 100 && port.DeepRequests == 0; frame++) { Thread.Sleep(10); fixture.Shell.Render(new(1024, 700)); }
        AssertEqual(1, port.DeepRequests);
        AssertEqual(0, port.OrdinaryRequests);
        for (int frame = 0; frame < 20; frame++) { fixture.Shell.Render(new(1024, 700)); Thread.Sleep(5); }
        scene = fixture.Shell.Render(new(1024, 700));
        AssertTrue(FindByKey(fixture.Shell, scene, "MemoryDetail").Text!.Contains('3'));
        using var persisted = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.TemporaryDirectory, "settings.json")));
        AssertTrue(persisted.RootElement.GetProperty("booleanOptions").GetProperty(MemoryOptimizationContract.DeepSetting).GetBoolean());
        AssertEqual(87, persisted.RootElement.GetProperty("integerOptions").GetProperty(MemoryOptimizationContract.ThresholdSetting).GetInt32());
        foreach (bool dark in MemoryPreviewThemes)
        {
            fixture.Shell.Renderer.ColorScheme = new(dark);
            foreach (var size in MemoryPreviewSizes)
            {
                scene = fixture.Shell.Render(size);
                var pager = FindByKey(fixture.Shell, scene, "LaunchWidgetPager").Rect;
                var deep = FindByKey(fixture.Shell, scene, "MemoryDeep").Rect;
                AssertTrue(deep.Y >= pager.Y && deep.Y + deep.Height <= pager.Y + pager.Height);
                AssertMemoryCardContrast(fixture.Shell, scene);
            }
        }
    }

    private static void AssertMemoryCardContrast(XsrUiShell shell, XsrUiScene scene)
    {
        var background = FindByKey(shell, scene, "CardAbout").VisualStyle.Background;
        foreach (string key in MemoryContrastKeys)
            AssertTrue(AppearanceContrast(FindByKey(shell, scene, key).VisualStyle.Foreground, background) >= 4.5);
        var button = FindByKey(shell, scene, "MemoryClean").VisualStyle;
        AssertTrue(AppearanceContrast(button.Foreground, button.Background) >= 4.5);
        AssertTrue(AppearanceContrast(button.Foreground, button.Hover) >= 4.5);
    }

    private static void MemoryOptimizationCardUnsupportedPlatformIsExplicit()
    {
        using var fixture = new LaunchPageFixture(new ImmediateInstanceSource([]));
        fixture.Shell.Renderer.ReducedMotion = true;
        Emit(fixture.Intents, "ui.launch.widget.memory");
        var scene = fixture.Shell.Render(new(900, 600));
        AssertFalse(FindByKey(fixture.Shell, scene, "MemoryClean").IsClickable);
        AssertTrue(FindByKey(fixture.Shell, scene, "MemoryUsage").Text!.Contains("Windows", StringComparison.Ordinal));
        Emit(fixture.Intents, "ui.launch.memory.automatic");
        AssertEqual("false", fixture.Foundation.Host.Settings.GetRawValue(MemoryOptimizationContract.AutomaticSetting).Value);
    }

    private sealed class CardMemoryPort : IElevatedMemoryCleanupPort
    {
        public bool IsSupported => true;
        public int OrdinaryRequests, DeepRequests;
        public SystemMemorySample Sample() => new(8L << 30, 2L << 30);
        public IReadOnlyList<MemoryProcess> CaptureProcesses() => [new(40, 0, "app.exe", 1, false)];
        public bool TryTrim(MemoryProcess process, out long workingSetReductionBytes) { OrdinaryRequests++; workingSetReductionBytes = 1048576; return true; }
        public Task<MemoryCleanupResult> CleanElevatedAsync(IReadOnlySet<int> protectedIds, CancellationToken token)
        { Interlocked.Increment(ref DeepRequests); return Task.FromResult(new MemoryCleanupResult(3, 2, 1, 1048576, 0)); }
    }
}
