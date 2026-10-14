using Nexa.Xsr;

namespace Nexa.Services.Themes;

public sealed partial class ThemePackageService
{
    private sealed class DirectoryWatch(FileSystemWatcher watcher, CancellationTokenSource stop)
    {
        internal FileSystemWatcher Watcher { get; } = watcher;
        internal CancellationTokenSource Stop { get; } = stop;
        internal Timer? Debounce { get; set; }
        internal long Revision { get; set; }
        internal string? Error { get; set; }
        internal Guid? Candidate { get; set; }
        internal bool Running { get; set; }
        internal bool Dirty { get; set; }
    }
    private readonly Dictionary<string, DirectoryWatch> _watches = new(StringComparer.Ordinal);
    public event Action<ThemeWatchSnapshot>? WatchChanged;

    public ValueTask<XsrResult> WatchAsync(ThemeWatchCommand command, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate)
        {
            if (!command.Enabled) { StopWatch(command.SourceIdentity); return ValueTask.FromResult(XsrResult.Success()); }
            var source = FindSource(command.SourceIdentity);
            if (_disposed || source is null || source.Kind != ThemeSourceKind.Directory || source.Origin is null)
                return ValueTask.FromResult(Reject("Only an admitted development-directory source can be watched."));
            if (_watches.ContainsKey(command.SourceIdentity)) return ValueTask.FromResult(XsrResult.Success());
            try
            {
                ThemePaths.NoLinks(source.Origin);
                var watcher = new FileSystemWatcher(source.Origin) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size, InternalBufferSize = 8192 };
                var watch = new DirectoryWatch(watcher, new()); _watches.Add(command.SourceIdentity, watch);
                void Changed(object? sender, FileSystemEventArgs args) => ScheduleWatch(command.SourceIdentity, watch);
                watcher.Changed += Changed; watcher.Created += Changed; watcher.Deleted += Changed; watcher.Renamed += (sender, args) => ScheduleWatch(command.SourceIdentity, watch);
                watcher.Error += (sender, args) => { lock (_gate) { if (_watches.TryGetValue(command.SourceIdentity, out var current) && ReferenceEquals(current, watch)) { watch.Error = "Directory watcher overflowed; full source revalidation is required."; ScheduleWatch(command.SourceIdentity, watch); } } };
                watch.Debounce = new Timer(_ => _ = RefreshWatchAsync(command.SourceIdentity, watch), null, Timeout.Infinite, Timeout.Infinite);
                watcher.EnableRaisingEvents = true; return ValueTask.FromResult(XsrResult.Success());
            }
            catch (Exception error) when (Recoverable(error)) { StopWatch(command.SourceIdentity); return ValueTask.FromResult(Reject(error.Message)); }
        }
    }
    public ValueTask<XsrResult<ThemeWatchSnapshot>> WatchStatusAsync(ThemeWatchQuery query, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); lock (_gate) return ValueTask.FromResult(_watches.TryGetValue(query.SourceIdentity, out var watch)
            ? XsrResult.Success(new ThemeWatchSnapshot(query.SourceIdentity, watch.Revision, watch.Error, watch.Candidate))
            : Reject<ThemeWatchSnapshot>("Theme source is not being watched."));
    }
    private void ScheduleWatch(string identity, DirectoryWatch watch)
    {
        lock (_gate)
        {
            if (_disposed || !_watches.TryGetValue(identity, out var current) || !ReferenceEquals(current, watch)) return;
            watch.Dirty = true; watch.Debounce?.Change(300, Timeout.Infinite);
        }
    }
    private async Task RefreshWatchAsync(string identity, DirectoryWatch watch)
    {
        string origin; ThemeWatchSnapshot? notification = null;
        lock (_gate)
        {
            var installed = FindSource(identity);
            if (_disposed || !_watches.TryGetValue(identity, out var current) || !ReferenceEquals(current, watch) || installed?.Origin is null) return;
            if (watch.Running) { watch.Dirty = true; return; }
            watch.Running = true; watch.Dirty = false; origin = installed.Origin;
        }
        try
        {
            var result = await PrepareAsync(new(origin, ThemeSourceKind.Directory, identity), watch.Stop.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed || !_watches.TryGetValue(identity, out var current) || !ReferenceEquals(current, watch)) { if (result.IsSuccess) RetireCandidate(result.Value.CandidateId); return; }
                if (result.IsSuccess)
                {
                    if (watch.Candidate is { } old && old != _previewCandidate && _candidates.TryGetValue(old, out var previous) && previous.Candidate.Generation != _selection?.Generation) RetireCandidate(old);
                    watch.Candidate = result.Value.CandidateId; watch.Error = null;
                }
                else watch.Error = result.Error!.Message;
                watch.Revision++;
                notification = new(identity, watch.Revision, watch.Error, watch.Candidate);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (Recoverable(error)) { lock (_gate) { watch.Error = error.Message; watch.Revision++; notification = new(identity, watch.Revision, watch.Error, watch.Candidate); } }
        finally
        {
            lock (_gate)
            {
                watch.Running = false;
                if (watch.Dirty && !_disposed && _watches.TryGetValue(identity, out var current) && ReferenceEquals(current, watch)) watch.Debounce?.Change(300, Timeout.Infinite);
                if (watch.Stop.IsCancellationRequested) watch.Stop.Dispose();
            }
        }
        if (notification is not null)
        {
            Action<ThemeWatchSnapshot>? observer;
            lock (_gate) observer = !_disposed && _watches.TryGetValue(identity, out var current) && ReferenceEquals(current, watch) ? WatchChanged : null;
            observer?.Invoke(notification);
        }
    }
    private void StopWatch(string identity)
    {
        if (!_watches.Remove(identity, out var watch)) return; watch.Stop.Cancel(); watch.Debounce?.Dispose(); watch.Watcher.Dispose();
        if (!watch.Running) watch.Stop.Dispose();
        if (watch.Candidate is { } candidate && candidate != _previewCandidate && _candidates.TryGetValue(candidate, out var prepared) && prepared.Candidate.Generation != _selection?.Generation) RetireCandidate(candidate);
        // A running callback owns the cancellation token until it finishes; cancellation prevents reattachment.
    }
}
