using System.Collections.Immutable;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;

namespace Nexa.Services.Composition.Plugins;

public sealed record PluginPlatformEffects(
    Func<string, int, string[], CancellationToken, Task<string?>> Pick,
    Action<string> Reveal,
    Action<Uri> OpenUri,
    Func<CancellationToken, Task<(string? Text, byte[]? Image)>> ReadClipboard,
    Func<string?, byte[]?, CancellationToken, Task> WriteClipboard,
    Func<CancellationToken, Task<(string Id, int Width, int Height, double Scale)[]>> Displays);

public sealed partial class PluginBusinessRoutes
{
    private PluginPlatformEffects? _platform;
    public void BindPlatform(PluginPlatformEffects platform) => _platform = platform;
    private PluginPlatformEffects Platform => _platform ?? throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
    private async ValueTask<FileRef> PickFile(HostInvocation c, FilePick request)
    {
        string? path = await Platform.Pick(c.Identity.PluginId + ": " + request.Title.MessageKey, (int)request.Kind, request.Extensions.ToArray(), c.Options.Cancellation).ConfigureAwait(false);
        if (path is null) throw new AbiHostException(AbiErrors.CANCELLED);
        return Application.Files.GrantPath(c.Identity, path, true);
    }
    private IEnumerable<HostCapabilityRoute> FileRoutes => [
        HostRoutes.Typed<FilePick, FileRef>("Files", "Pick", PickFile),
        HostRoutes.Typed<FileQuery, Page<FileStat>>("Files", "List", async (c, v) =>
        {
            var entries = await Application.Files.ListAsync(c.Identity, v.Directory, c.Options.Cancellation).ConfigureAwait(false);
            var stamp = await Application.Files.StatAsync(c.Identity, v.Directory, c.Options.Cancellation).ConfigureAwait(false);
            long revision = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stamp.Revision.Value)));
            return Page(c, v.Page, entries, "files:" + v.Directory.Token, revision);
        }),
        HostRoutes.Typed<FileWrite, FileStat>("Files", "WriteAtomic", async (c, v) =>
            await Application.Files.WriteAsync(c.Identity, v, Application.CopyBlob(c.Identity, v.Data), c.Options.Cancellation).ConfigureAwait(false)),
        HostRoutes.Typed<FileMove, MutationPlan>("Files", "PrepareMove", async (c, v) =>
        {
            string source = Application.Files.Resolve(c.Identity, v.File, true), target = Application.Files.Resolve(c.Identity, v.Destination, true);
            var before = await Application.Files.StatAsync(c.Identity, v.File, c.Options.Cancellation).ConfigureAwait(false);
            if (before.Revision != v.ExpectedRevision || source == target || File.Exists(target) || Directory.Exists(target)) throw new AbiHostException(AbiErrors.CONFLICT);
            async ValueTask Validate(CancellationToken token)
            {
                Application.Files.Resolve(c.Identity, v.File, true); Application.Files.Resolve(c.Identity, v.Destination, true);
                if ((await Application.Files.StatAsync(c.Identity, v.File, token).ConfigureAwait(false)).Revision != v.ExpectedRevision || File.Exists(target) || Directory.Exists(target)) throw new AbiHostException(AbiErrors.CONFLICT);
            }
            return Plan(c, "files/move", FileMove.Write(v), "Move " + Path.GetFileName(source) + " to " + target, Validate, async token =>
            {
                await Application.Files.MutationGate.WaitAsync(token).ConfigureAwait(false);
                try { await Validate(token).ConfigureAwait(false); if (before.IsDirectory) Directory.Move(source, target); else File.Move(source, target); }
                finally { Application.Files.MutationGate.Release(); }
                return Empty.Write(new());
            });
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Files", "Move", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "files/move", "nexa.files.write", Abi100Schemas.Empty))),
        HostRoutes.Typed<FileDelete, MutationPlan>("Files", "PrepareDelete", async (c, v) =>
        {
            string path = Application.Files.Resolve(c.Identity, v.File, true);
            var before = await Application.Files.StatAsync(c.Identity, v.File, c.Options.Cancellation).ConfigureAwait(false);
            if (before.Revision != v.ExpectedRevision || before.Revision.Value == "absent") throw new AbiHostException(AbiErrors.CONFLICT);
            async ValueTask Validate(CancellationToken token)
            {
                Application.Files.Resolve(c.Identity, v.File, true);
                if ((await Application.Files.StatAsync(c.Identity, v.File, token).ConfigureAwait(false)).Revision != v.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
            }
            return Plan(c, "files/delete", FileDelete.Write(v), "Delete " + path + (v.Recursive ? " recursively" : ""), Validate, async token =>
            {
                await Application.Files.MutationGate.WaitAsync(token).ConfigureAwait(false);
                try { await Validate(token).ConfigureAwait(false); if (before.IsDirectory) Directory.Delete(path, v.Recursive); else File.Delete(path); }
                finally { Application.Files.MutationGate.Release(); }
                return Empty.Write(new());
            });
        }),
        HostRoutes.Typed<PlanRequest, OperationRef>("Files", "Delete", (c, v) => ValueTask.FromResult(ExecutePlan(c, v, "files/delete", "nexa.files.write", Abi100Schemas.Empty))),
        HostRoutes.Typed<FileRef, SubscriptionRef>("Files", "Watch", async (c, v) =>
        {
            string stamp = Application.Files.ObservationStamp(c.Identity, v);
            var initial = await Application.Files.StatAsync(c.Identity, v, c.Options.Cancellation).ConfigureAwait(false);
            lock (_watchGate)
            {
                if (_fileWatches.Count >= 64) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                var sub = Services.CreateHostSubscription(c, "nexa.files/100", "nexa.files.read", FileStat.Write(initial), 64);
                _fileWatches.Add(sub.Token, (c.Identity, v, sub, initial, stamp)); return sub;
            }
        }),
        HostRoutes.Typed<FilePick, FileRef>("Platform", "PickFile", PickFile),
        HostRoutes.Typed<FileRef, Empty>("Platform", "RevealFile", (c, v) =>
        { string path = Application.Files.Resolve(c.Identity, v); Platform.Reveal(Directory.Exists(path) ? path : Path.GetDirectoryName(path)!); return ValueTask.FromResult(new Empty()); }),
        HostRoutes.Typed<UriRef, Empty>("Platform", "OpenUri", async (c, v) =>
        {
            if (!Uri.TryCreate(v.Value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            await Confirm(c, "platform/open-uri", "Open " + uri.Host, UriRef.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            Platform.OpenUri(uri); return new Empty();
        }),
        HostRoutes.Typed<Empty, ClipboardContent>("Platform", "ReadClipboard", async (c, _) =>
        {
            await Confirm(c, "platform/read-clipboard", "Read clipboard text and image", Empty.Write(new()), c.Options.Cancellation).ConfigureAwait(false);
            var clipboard = await Platform.ReadClipboard(c.Options.Cancellation).ConfigureAwait(false);
            if (clipboard.Text?.Length > 65536 || clipboard.Image?.Length > 16 * 1024 * 1024) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            return new(clipboard.Text, clipboard.Image is null ? null : Application.AddBlob(c.Identity, clipboard.Image));
        }),
        HostRoutes.Typed<ClipboardContent, Empty>("Platform", "WriteClipboard", async (c, v) =>
        {
            if (v.Text?.Length > 65536 || v.Image?.Size > 16 * 1024 * 1024) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            await Confirm(c, "platform/write-clipboard", "Replace clipboard content", ClipboardContent.Write(v), c.Options.Cancellation).ConfigureAwait(false);
            await Platform.WriteClipboard(v.Text, v.Image is null ? null : Application.CopyBlob(c.Identity, v.Image), c.Options.Cancellation).ConfigureAwait(false); return new Empty();
        })
    ];
    private readonly Dictionary<Guid, (PluginIdentity Owner, FileRef File, SubscriptionRef Subscription, FileStat Stat, string Stamp)> _fileWatches = [];
    private readonly CancellationTokenSource _resourceStop = new();
    private Task? _fileWatchWorker;
    private async Task WatchFilesAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(_resourceStop.Token).ConfigureAwait(false))
            {
                (PluginIdentity Owner, FileRef File, SubscriptionRef Subscription, FileStat Stat, string Stamp)[] watches;
                lock (_watchGate) watches = _fileWatches.Values.ToArray();
                foreach (var watch in watches)
                {
                    try
                    {
                        string stamp = Application.Files.ObservationStamp(watch.Owner, watch.File);
                        var current = stamp == watch.Stamp ? watch.Stat : await Application.Files.StatAsync(watch.Owner, watch.File, _resourceStop.Token).ConfigureAwait(false);
                        bool live = Services.PublishHostSubscription(watch.Owner, watch.Subscription, FileStat.Write(current));
                        lock (_watchGate)
                        {
                            if (live && _fileWatches.ContainsKey(watch.Subscription.Token)) _fileWatches[watch.Subscription.Token] = (watch.Owner, watch.File, watch.Subscription, current, stamp);
                            else _fileWatches.Remove(watch.Subscription.Token);
                        }
                    }
                    catch (Exception e) when (e is AbiHostException or IOException or UnauthorizedAccessException)
                    { lock (_watchGate) _fileWatches.Remove(watch.Subscription.Token); }
                }
            }
        }
        catch (OperationCanceledException) when (_resourceStop.IsCancellationRequested) { }
    }
}
