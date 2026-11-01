namespace Nexa.Xsr.State;

public sealed partial class XsrStateStore
{
    /// <summary>Applies a complete decoded cell snapshot, preserving the producer revision.
    /// Returns false for an already observed or older revision. The adapter owns decoding and copying.</summary>
    public bool Replicate<TValue>(XsrStateId stateId, long revision, XsrStateAvailability availability,
        bool hasValue, TValue value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateReplicaVersion(revision, availability);
        AdmitOrdinaryMutation();
        XsrStateNode node = RequireNode(stateId);
        RequireReplica(node);
        if (node is not XsrStateCellNode<TValue> cell) throw MismatchedContract(stateId, node, typeof(TValue));
        bool applied = cell.Replicate(revision, availability, hasValue, value, NextChangeStamp(), out var change);
        Notify(change);
        return applied;
    }

    /// <summary>Applies a complete collection snapshot with unique keys and canonical ordering.
    /// Source revision and availability are retained, so expected-revision commands remain valid.</summary>
    public bool ReplicateCollection<TItem, TKey>(XsrStateId stateId, long revision, XsrStateAvailability availability,
        IReadOnlyList<TItem> items, CancellationToken cancellationToken = default) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateReplicaVersion(revision, availability);
        AdmitOrdinaryMutation();
        XsrStateNode node = RequireNode(stateId);
        RequireReplica(node);
        if (node is not XsrStateCollectionNode<TItem, TKey> collection) throw MismatchedContract(stateId, node, typeof(TItem));
        bool applied = collection.Replicate(revision, availability, items, NextChangeStamp(), out var change);
        Notify(change);
        return applied;
    }

    private static void ValidateReplicaVersion(long revision, XsrStateAvailability availability)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        if (!Enum.IsDefined(availability)) throw new ArgumentOutOfRangeException(nameof(availability));
    }

    private static void RequireReplica(XsrStateNode node)
    {
        if (!node.IsReplica) throw new InvalidOperationException($"State '{node.SemanticId}' is owned by a local writer.");
    }

    private static void RequireLocalWriter(XsrStateNode node)
    {
        if (node.IsReplica) throw new InvalidOperationException($"State '{node.SemanticId}' is owned by a replica producer.");
    }
}

internal sealed partial class XsrStateCellNode<TValue>
{
    internal bool Replicate(long revision, XsrStateAvailability availability, bool hasValue, TValue value,
        long stamp, out XsrStateChange? change)
    {
        lock (Gate)
        {
            change = null;
            if (revision <= CurrentRevisionLocked) return false;
            if (!hasValue && availability == XsrStateAvailability.Available)
                throw new ArgumentException("Available cell replicas require a value.");
            _value = value;
            _hasValue = hasValue;
            ReplicateLocked(revision, stamp, availability);
            change = new(new(RuntimeId), SemanticId, Kind, revision, availability, XsrStateChangeReason.ValuePublished);
            return true;
        }
    }
}

internal sealed partial class XsrStateCollectionNode<TItem, TKey> where TKey : notnull
{
    internal bool Replicate(long revision, XsrStateAvailability availability, IReadOnlyList<TItem> items,
        long stamp, out XsrStateChange? change)
    {
        lock (Gate)
        {
            change = null;
            if (revision <= CurrentRevisionLocked) return false;
            TItem[] owned = new TItem[items.Count];
            HashSet<TKey> keys = [];
            for (int index = 0; index < owned.Length; index++)
            {
                TItem item = items[index];
                TKey key = _keySelector(item);
                if (key is null || !keys.Add(key)) throw new ArgumentException("Replica collection keys must be non-null and unique.", nameof(items));
                owned[index] = item;
            }
            // Stable sort preserves producer order when distinct keys compare equal.
            _items = [.. owned.OrderBy(_keySelector, _comparer)];
            _snapshot = null;
            ReplicateLocked(revision, stamp, availability);
            change = new(new(RuntimeId), SemanticId, Kind, revision, availability, XsrStateChangeReason.CollectionDeltaApplied);
            return true;
        }
    }
}
