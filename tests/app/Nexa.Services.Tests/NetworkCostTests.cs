using Nexa.Platform;
using Nexa.Services.Downloads;
using Nexa.Services.Foundation;
using Nexa.Services.Scheduling;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

internal static partial class Program
{
    private static async ValueTask NetworkCostIsCachedAndExplicitForegroundRemainsAvailable()
    {
        AssertEqual(PlatformNetworkCost.Metered, PlatformNetworkCostProbe.Parse("yes (guessed)\n"));
        AssertEqual(PlatformNetworkCost.Unknown, PlatformNetworkCostProbe.Parse("wifi"));
        AssertEqual(PlatformNetworkCost.Unmetered, PlatformNetworkCostProbe.FromWindowsCost(1));
        AssertEqual(PlatformNetworkCost.Metered, PlatformNetworkCostProbe.FromWindowsCost(2));
        AssertEqual(PlatformNetworkCost.Roaming, PlatformNetworkCostProbe.FromWindowsCost(0x40001));
        AssertFalse(FoundationNetworkCost.AllowsBackground(PlatformNetworkCost.Metered, "auto"));
        AssertTrue(FoundationNetworkCost.AllowsBackground(PlatformNetworkCost.Metered, "allow"));
        AssertFalse(FoundationNetworkCost.AllowsBackground(PlatformNetworkCost.Unmetered, "restrict"));
        AssertTrue(FoundationNetworkCost.AllowsBackground(PlatformNetworkCost.Unknown, "auto"));
        var probe = new CostProbe();
        await using var monitor = new FoundationNetworkCost(probe);
        await Task.WhenAll(monitor.StartAsync(), monitor.StartAsync());
        AssertEqual(1, probe.Calls); AssertEqual(PlatformNetworkCost.Metered, monitor.Current);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var native = await new PlatformNetworkCostProbe().ReadAsync(stop.Token);
        AssertTrue(Enum.IsDefined(native));
        Console.WriteLine("Native network cost: " + native);
    }

    private sealed class CostProbe : IPlatformNetworkCost
    {
        public int Calls;
        public ValueTask<PlatformNetworkCost> ReadAsync(CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult(PlatformNetworkCost.Metered); }
    }

    private static async ValueTask BackgroundNetworkPolicyIsRecheckedAfterWaiting()
    {
        string root = CreateTempDirectory();
        try
        {
            var builder = new XsrStateStoreBuilder(); DownloadService.DeclareState(builder);
            using var work = new WorkScheduler(new(1, 1, 1));
            using var held = await work.AcquireAsync(WorkPriority.Interactive, WorkResource.Http | WorkResource.Disk);
            bool allowed = true;
            using var downloads = new DownloadService(builder.Build()) { WorkScheduler = work, BackgroundDownloadsEnabled = () => allowed };
            Task<DownloadTransferResult> pending;
            using (work.UsePriority(WorkPriority.Background))
                pending = downloads.DownloadAsync(new DownloadRequest { Sources = ["memory"], DestinationPath = Path.Combine(root, "background"), ConnectionFactory = _ => new FakeConnection(1, [7]) });
            AssertFalse(pending.IsCompleted);
            allowed = false;
            held.Dispose();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            AssertFalse(result.Success); AssertFalse(File.Exists(Path.Combine(root, "background")));
            AssertTrue(work.Snapshot.Resources.All(r => r.Active == 0));
        }
        finally { Directory.Delete(root, true); }
    }
}
