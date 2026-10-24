using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Nexa.Core.Patching;
using Nexa.Sidecar.Protocol;

namespace Nexa.Xsr.Runtime.Tests;

internal static partial class Program
{
    private static readonly int[] AutomaticIteratorReplacement = [4, 6];
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int AutomaticSum(int first, int second) => first + second;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T AutomaticIdentity<T>(T first, T second) => first;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Span<int> AutomaticSpan(Span<int> first, Span<int> second) => first;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ref int AutomaticReference(ref int first) => ref first;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int AutomaticUnpatched(int value) => (value * 3) ^ 1;
    private class AutomaticParent { internal static int Calls; internal AutomaticParent() => Calls++; }
    private sealed class AutomaticConstruction(int value) : AutomaticParent { internal int Value { get; } = value; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> AutomaticAsync() { await Task.Yield(); return 42; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IEnumerable<int> AutomaticIterator() { yield return 1; yield return 2; }

    private static PatchTarget AutomaticTarget(string method) => Patch.Catalog.Single(target => target.Signature.Contains("::" + method + "``", StringComparison.Ordinal));
    private static byte[] AutomaticProgram(PatchPhase phase, ushort count, params byte[] instructions)
    {
        byte[] bytes = new byte[8 + instructions.Length]; "NFP3"u8.CopyTo(bytes); bytes[4] = 1; bytes[5] = (byte)phase;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), count); instructions.CopyTo(bytes, 8); return bytes;
    }
    private static byte[] AutomaticInteger(int value)
    {
        byte[] bytes = new byte[8]; bytes[0] = 3; bytes[1] = 2; bytes[2] = 4;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), value); return bytes;
    }
    private static SidecarRegistrationItem AutomaticItem(string name, PatchTarget target, byte[] program) =>
        new(SidecarRegistrationKind.FunctionPatch, name, 0, 0, program, SHA256.HashData(program), TargetSemanticId: target.Id);

    private static async ValueTask AutomaticSidecarPatchesOrdinaryMethods()
    {
        AssertEqual(3, AutomaticSum(1, 2));
        var sum = AutomaticTarget(nameof(AutomaticSum));
        AssertEqual(PatchTargetStatus.Instrumented, sum.Status);
        var declarations = new[]
        {
            AutomaticItem("auto.args", sum, AutomaticProgram(PatchPhase.Args, 3, [.. AutomaticInteger(10), 5, 0, 0, 8])),
            AutomaticItem("auto.return", sum, AutomaticProgram(PatchPhase.Return, 5, [2, .. AutomaticInteger(100), 4, 6, 8])),
        };
        var (session, peer) = await PatchSession(new(CaptionPoint), declarations,
            features: SidecarFeatures.All, automatic: new());
        using (session) using (peer)
        {
            AssertEqual(3, AutomaticSum(1, 2));
            await SnapshotAll(session, peer); await session.ActivateAsync(); await DataPlaneReceiveAsync(peer);
            AssertEqual(112, AutomaticSum(1, 2));
            await session.DeactivateAsync(); await DataPlaneReceiveAsync(peer); AssertEqual(3, AutomaticSum(1, 2));
            await session.ActivateAsync(); await DataPlaneReceiveAsync(peer); AssertEqual(112, AutomaticSum(1, 2));
            session.Dispose(); AssertEqual(3, AutomaticSum(1, 2));
        }
        var identity = AutomaticTarget(nameof(AutomaticIdentity));
        var span = AutomaticTarget(nameof(AutomaticSpan));
        var reference = AutomaticTarget(nameof(AutomaticReference));
        var (typed, plugin) = await PatchSession(new(CaptionPoint),
            [AutomaticItem("auto.generic", identity, AutomaticProgram(PatchPhase.Args, 3, [1, 1, 0, 5, 0, 0, 8])),
             AutomaticItem("auto.span", span, AutomaticProgram(PatchPhase.Replace, 4, [1, 1, 0, 6, 7, 8])),
             AutomaticItem("auto.reference", reference, AutomaticProgram(PatchPhase.Replace, 4, [9, 0, 0, 6, 7, 8]))],
            features: SidecarFeatures.All, automatic: new());
        using (typed) using (plugin)
        {
            await SnapshotAll(typed, plugin); await typed.ActivateAsync(); await DataPlaneReceiveAsync(plugin);
            AssertEqual(2, AutomaticIdentity(1, 2)); AssertEqual("second", AutomaticIdentity("first", "second"));
            var left = new[] { 1 }; var right = new[] { 2 };
            AssertEqual(2, AutomaticSpan(left, right)[0]);
            int first = 3; AutomaticReference(ref first) = 9; AssertEqual(9, first);
            typed.Dispose(); AssertEqual(1, AutomaticSpan(left, right)[0]);
        }
    }

    private static async ValueTask AutomaticPatchesRejectUnsafePrograms()
    {
        var sum = AutomaticTarget(nameof(AutomaticSum));
        byte[] valid = AutomaticProgram(PatchPhase.Replace, 4, [.. AutomaticInteger(9), 6, 7, 8]);
        foreach (var bytes in new[] { valid[..^1], AutomaticProgram(PatchPhase.Return, 2, [7, 8]), AutomaticProgram(PatchPhase.Args, 3, [9, 0, 0, 5, 0, 0, 8]) })
            await AssertThrowsAsync<SidecarProtocolException>(async () =>
            {
                var (session, peer) = await PatchSession(new(CaptionPoint), [AutomaticItem("auto.invalid", sum, bytes)],
                    features: SidecarFeatures.All, automatic: new()); session.Dispose(); peer.Dispose();
            });
        foreach (bool negotiate in new[] { false, true })
            await AssertThrowsAsync<SidecarProtocolException>(async () =>
            {
                var (session, peer) = await PatchSession(new(CaptionPoint), [AutomaticItem("auto.denied", sum, valid)],
                    features: negotiate ? SidecarFeatures.All : SidecarFeatures.Nxml,
                    automatic: negotiate ? new(_ => false) : new()); session.Dispose(); peer.Dispose();
            });
        AssertEqual(3, AutomaticSum(1, 2));
    }

    private static void AutomaticPatchGroupsAreAtomicAndNoopIsAllocationFree()
    {
        var sum = AutomaticTarget(nameof(AutomaticSum));
        using var batch = Patch.BeginBatch();
        batch.Add(sum, PatchPhase.Args, frame => frame.Arguments[0] = 10);
        batch.Add(sum, PatchPhase.Return, frame => frame.Result = (int)frame.Result! + 100);
        AssertEqual(3, AutomaticSum(1, 2)); batch.Commit(); AssertEqual(112, AutomaticSum(1, 2));
        batch.Dispose(); AssertEqual(3, AutomaticSum(1, 2));
        for (int index = 0; index < 10000; index++) _ = AutomaticSum(index, 2);
        long before = GC.GetAllocatedBytesForCurrentThread(); int result = 0;
        for (int index = 0; index < 10000; index++) result += AutomaticSum(index, 2);
        AssertEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before); AssertTrue(result > 0);
        using var otherActive = Patch.Install(sum, PatchPhase.Head, _ => { });
        for (int index = 0; index < 10000; index++) _ = AutomaticUnpatched(index);
        before = GC.GetAllocatedBytesForCurrentThread(); result = 0;
        for (int index = 0; index < 10000; index++) result += AutomaticUnpatched(index);
        AssertEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before); AssertTrue(result > 0);
    }

    private static async ValueTask AutomaticRetirementDiscardsInFlightChanges()
    {
        var sum = AutomaticTarget(nameof(AutomaticSum));
        using var entered = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim();
        using var lease = Patch.Install(sum, PatchPhase.Args, frame =>
        { frame.Arguments[0] = 99; entered.Set(); AssertTrue(finish.Wait(TimeSpan.FromSeconds(10))); });
        var invocation = Task.Run(() => AutomaticSum(1, 2));
        try { AssertTrue(entered.Wait(TimeSpan.FromSeconds(10))); lease.Dispose(); }
        finally { finish.Set(); }
        AssertEqual(3, await invocation); AssertEqual(3, AutomaticSum(1, 2));
    }

    private static void AutomaticFaultsAndGlobalBudgetsAreIsolated()
    {
        var sum = AutomaticTarget(nameof(AutomaticSum)); int attempts = 0;
        using (var fault = Patch.Install(sum, PatchPhase.Args, frame =>
        { attempts++; frame.Arguments[0] = 99; throw new InvalidOperationException("isolated"); }))
        { AssertEqual(3, AutomaticSum(1, 2)); AssertEqual(3, AutomaticSum(1, 2)); AssertEqual(1, attempts); }
        using (var pending = Patch.BeginBatch())
        {
            for (int index = 0; index < Patch.MaximumHooksPerTarget; index++) pending.Add(sum, PatchPhase.Head, _ => { });
            AssertThrows<InvalidOperationException>(() => pending.Add(sum, PatchPhase.Head, _ => { }));
            pending.Commit(); AssertEqual(3, AutomaticSum(1, 2));
        }
        using var replacement = Patch.Install(sum, PatchPhase.Return, frame => frame.Result = 7);
        AssertEqual(7, AutomaticSum(1, 2)); replacement.Dispose(); AssertEqual(3, AutomaticSum(1, 2));
    }

    private static async ValueTask AutomaticConstructorsAsyncAndIteratorsRetainSemantics()
    {
        var constructor = Patch.Catalog.Single(target => target.Signature.Contains("AutomaticConstruction::.ctor``0", StringComparison.Ordinal));
        int before = AutomaticParent.Calls;
        // Primary-constructor field initializers precede the base call and remain authoritative.
        using (var skipped = Patch.Install(constructor, PatchPhase.Replace, frame => frame.SkipOriginal = true))
        { AssertEqual(7, new AutomaticConstruction(7).Value); AssertEqual(before + 1, AutomaticParent.Calls); }
        AssertEqual(42, await AutomaticAsync());
        using (var replaced = Patch.Install(AutomaticTarget(nameof(AutomaticAsync)), PatchPhase.Replace, frame =>
        { frame.Result = Task.FromResult(15); frame.SkipOriginal = true; }))
            AssertEqual(15, await AutomaticAsync());
        AssertTrue(AutomaticIterator().SequenceEqual([1, 2]));
        using (var replaced = Patch.Install(AutomaticTarget(nameof(AutomaticIterator)), PatchPhase.Replace, frame =>
        { frame.Result = AutomaticIteratorReplacement; frame.SkipOriginal = true; }))
            AssertTrue(AutomaticIterator().SequenceEqual(AutomaticIteratorReplacement));
    }
}
