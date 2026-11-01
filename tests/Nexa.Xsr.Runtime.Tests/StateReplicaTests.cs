using Nexa.Xsr.State;

namespace Nexa.Xsr.Runtime.Tests;

internal static partial class Program
{
    private readonly record struct CollectionItem(int Id, string Name);

    private static void StateReplicasRetainRevisionsAndOwnership()
    {
        XsrStateStoreBuilder builder = new();
        var cellKey = Nexa.Xsr.XsrSemanticId.Parse("replica.cell");
        var listKey = Nexa.Xsr.XsrSemanticId.Parse("replica.list");
        var derivedKey = Nexa.Xsr.XsrSemanticId.Parse("replica.derived");
        builder.Cell<int>(cellKey, "Remote");
        builder.Collection<CollectionItem, int>(listKey, "Remote", item => item.Id);
        builder.Derived<int>(derivedKey, "Local", [cellKey], (store, token) => store.Read<int>(store.Resolve(cellKey), token).Value * 2);
        builder.Replica(cellKey);
        builder.Replica(listKey);
        AssertThrows<InvalidOperationException>(() => builder.Replica(derivedKey));
        AssertThrows<InvalidOperationException>(() => builder.Replica(Nexa.Xsr.XsrSemanticId.Parse("unknown.replica")));
        var state = builder.Build();
        AssertThrows<InvalidOperationException>(() => builder.Replica(cellKey));
        var cell = state.Resolve(cellKey);
        var list = state.Resolve(listKey);
        var derived = state.Resolve(derivedKey);
        List<XsrStateChange> changes = [];
        state.Changed += changes.Add;
        AssertTrue(state.Replicate(cell, 50, XsrStateAvailability.Available, true, 7));
        AssertEqual(50L, state.Read<int>(cell).Revision);
        AssertEqual(14, state.Read<int>(derived).Value);
        AssertFalse(state.Replicate(cell, 49, XsrStateAvailability.Available, true, 999));
        AssertFalse(state.Replicate(cell, 50, XsrStateAvailability.Available, true, 999));
        AssertEqual(7, state.Read<int>(cell).Value);
        AssertTrue(state.Replicate(cell, 52, XsrStateAvailability.Stale, true, 8));
        AssertEqual(XsrStateAvailability.Stale, state.Read<int>(derived).Availability);
        AssertEqual(14, state.Read<int>(derived).Value);
        AssertThrows<InvalidOperationException>(() => state.Publish(cell, 1));
        AssertThrows<InvalidOperationException>(() => state.PublishCoalesced(cell, 1));
        AssertThrows<InvalidOperationException>(() => state.MarkAvailability(cell, XsrStateAvailability.Unavailable));
        AssertThrows<InvalidOperationException>(() => state.Replicate(derived, 1, XsrStateAvailability.Available, true, 1));
        AssertThrows<ArgumentOutOfRangeException>(() => state.Replicate(cell, 0, XsrStateAvailability.Available, true, 1));
        AssertThrows<ArgumentException>(() => state.Replicate(cell, 53, XsrStateAvailability.Available, false, 0));
        AssertThrows<OperationCanceledException>(() => state.Replicate(cell, 53, XsrStateAvailability.Available, true, 1, new(true)));
        AssertTrue(state.Replicate(cell, 53, XsrStateAvailability.Available, true, 8));
        AssertEqual(16, state.Read<int>(derived).Value);
        CollectionItem[] items = [new(2, "b"), new(1, "a")];
        AssertTrue(state.ReplicateCollection<CollectionItem, int>(list, 20, XsrStateAvailability.Available, items));
        items[0] = new(2, "changed");
        var snapshot = state.ReadCollection<CollectionItem>(list);
        AssertEqual(1, snapshot.Items[0].Id);
        AssertEqual("b", snapshot.Items[1].Name);
        int count = changes.Count;
        AssertThrows<ArgumentException>(() => state.ReplicateCollection<CollectionItem, int>(list, 21,
            XsrStateAvailability.Available, [new(1, "a"), new(1, "duplicate")]));
        AssertEqual(count, changes.Count);
        AssertEqual(20L, state.ReadCollection<CollectionItem>(list).Revision);
        AssertThrows<InvalidOperationException>(() => state.PublishDelta(list, new XsrCollectionDelta<CollectionItem, int>(20, [], [])));
        AssertTrue(state.ReplicateCollection<CollectionItem, int>(list, 25, XsrStateAvailability.Unavailable, []));
        AssertEqual(2, snapshot.Items.Count);
        AssertEqual(25L, state.ReadCollection<CollectionItem>(list).Revision);
        AssertEqual(53L, state.Read<int>(cell).Revision);
        state.Changed -= changes.Add;
        Parallel.For(54, 300, revision => state.Replicate(cell, revision, XsrStateAvailability.Available, true, revision));
        AssertEqual(299L, state.Read<int>(cell).Revision);
        AssertEqual(299, state.Read<int>(cell).Value);
    }
}
