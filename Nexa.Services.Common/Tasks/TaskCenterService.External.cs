using Nexa.Xsr.State;

namespace Nexa.Services.Tasks;

public sealed partial class TaskCenterService
{
    private sealed class ExternalPartition(IExternalTaskCenterSource source)
    {
        internal IExternalTaskCenterSource Source { get; } = source;
        internal long Revision;
        internal IReadOnlyList<TaskCenterEntry> Entries = [];
        internal bool Queued;
    }
    private readonly Dictionary<string, ExternalPartition> _externalSources = new(StringComparer.Ordinal);

    public void AttachExternalSource(IExternalTaskCenterSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string prefix = source.Prefix;
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length > 128 || !prefix.EndsWith(':'))
            throw new ArgumentException("Task partitions require a bounded prefix ending in a colon.", nameof(source));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_externalSources.Count >= 8 || _externalSources.Keys.Any(existing => existing.StartsWith(prefix, StringComparison.Ordinal)
                || prefix.StartsWith(existing, StringComparison.Ordinal)) || _registrations.Keys.Any(id => id.StartsWith(prefix, StringComparison.Ordinal)))
                throw new InvalidOperationException("Task partition conflicts with existing ownership or exceeds its budget.");
            _externalSources.Add(prefix, new(source));
        }
    }
    public bool PublishExternalSnapshot(IExternalTaskCenterSource source, long revision, IReadOnlyList<TaskCenterEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        if (entries.Count > 512) throw new InvalidDataException("External task partition budget exceeded.");
        HashSet<string> ids = new(StringComparer.Ordinal);
        var captured = entries.Select(entry =>
        {
            if (string.IsNullOrWhiteSpace(entry.TaskId) || entry.TaskId.Length > 512 || !ids.Add(entry.TaskId)
                || entry.AttemptGeneration <= 0 || entry.Revision <= 0)
                throw new InvalidDataException("Invalid external task identity or attempt stamp.");
            return entry with { TaskId = source.Prefix + entry.TaskId, Steps = entry.Steps?.ToArray() };
        }).ToArray();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_externalSources.TryGetValue(source.Prefix, out var partition) || !ReferenceEquals(partition.Source, source))
                throw new InvalidOperationException("External task source is not attached.");
            if (revision <= partition.Revision) return false;
            partition.Revision = revision; partition.Entries = captured;
            if (!partition.Queued)
            {
                partition.Queued = true;
                _publications.Enqueue(new(0, revision, "", null, source.Prefix));
            }
        }
        DrainPublications(); return true;
    }
    private void ApplyExternalPartition(string prefix)
    {
        IReadOnlyList<TaskCenterEntry> entries;
        lock (_gate)
        {
            var partition = _externalSources[prefix]; entries = partition.Entries; partition.Queued = false;
        }
        lock (_stateGate)
        {
            for (int attempt = 0; attempt < MaxStateConflicts; attempt++)
            {
                var snapshot = _store.ReadCollection<TaskCenterEntry>(_entriesId);
                var others = snapshot.Items.Where(entry => !entry.TaskId.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                var removed = snapshot.Items.Where(entry => entry.TaskId.StartsWith(prefix, StringComparison.Ordinal)).Select(entry => entry.TaskId).Except(entries.Select(entry => entry.TaskId), StringComparer.Ordinal).ToArray();
                if (_store.PublishDelta(_entriesId, new XsrCollectionDelta<TaskCenterEntry, string>(snapshot.Revision, entries, removed)).IsApplied)
                { PublishSummary([.. others, .. entries]); return; }
            }
        }
        throw new InvalidOperationException("External task partition could not publish its state.");
    }
    private IExternalTaskCenterSource? ExternalOwner(string taskId)
    {
        lock (_gate) return _externalSources.Values.FirstOrDefault(partition => taskId.StartsWith(partition.Source.Prefix, StringComparison.Ordinal))?.Source;
    }
    public Task<bool> RequestRetryAsync(string taskId, CancellationToken token = default) => ExternalOwner(taskId) is { } source
        ? source.RetryAsync(taskId[source.Prefix.Length..], token) : Task.FromResult(RequestRetry(taskId));
    public Task<TaskCenterCancelResult> RequestCancelAsync(string taskId, CancellationToken token = default) => ExternalOwner(taskId) is { } source
        ? source.CancelAsync(taskId[source.Prefix.Length..], token) : Task.FromResult(RequestCancel(taskId));

    public async Task<bool> DismissFinishedAsync(IReadOnlyList<TaskCenterEntry> expected, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        IExternalTaskCenterSource[] sources; lock (_gate) sources = _externalSources.Values.Select(partition => partition.Source).ToArray();
        foreach (var source in sources)
        {
            var captured = expected.Where(entry => entry.TaskId.StartsWith(source.Prefix, StringComparison.Ordinal))
                .Select(entry => entry with { TaskId = entry.TaskId[source.Prefix.Length..] }).ToArray();
            if (!await source.DismissFinishedAsync(captured, token).ConfigureAwait(false)) return false;
        }
        return DismissFinished(expected.Where(entry => !sources.Any(source => entry.TaskId.StartsWith(source.Prefix, StringComparison.Ordinal))).ToArray());
    }
}
