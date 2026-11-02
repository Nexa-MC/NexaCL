using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
#if NEXA_NATIVE_SHARED_PATCH_RUNTIME
using Nexa.Core.Patching;
#endif

namespace Nexa.Native.Instrumentation;

/// <summary>Module-local probes. No managed references or executable callbacks cross the ABI.</summary>
internal static unsafe class NativeInstrumentation
{
    private static readonly object Gate = new();
    private static readonly Dictionary<int, Probe> Probes = [];
    private static readonly Lazy<PatchTarget[]> Targets = new(() => Patch.Catalog.ToArray());
    private sealed class Probe
    {
        internal long Hits;
        internal IDisposable? Hook;
        internal void Enter(PatchFrame _) => Interlocked.Increment(ref Hits);
    }

    // Returns required UTF-8 bytes excluding a terminator; no partial writes on short buffers.
    // Index -1 returns the catalog count. Each entry is id + tab + status + tab + signature.
    [UnmanagedCallersOnly(EntryPoint = "nexa_instrumentation_target_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Target(int index, byte* output, int capacity)
    {
        try
        {
            var targets = Targets.Value;
            if (index == -1) return targets.Length;
            if ((uint)index >= (uint)targets.Length || capacity < 0) return -1;
            var target = targets[index];
            string value = target.Id + "\t" + (byte)target.Status + "\t" + target.Signature;
            int required = Encoding.UTF8.GetByteCount(value);
            if (output != null && capacity >= required)
                Encoding.UTF8.GetBytes(value, new Span<byte>(output, required));
            return required;
        }
        catch { return -1; }
    }

    // 1 enables an idempotent entry counter; 0 retires it. Invalid/infrastructure targets fail.
    [UnmanagedCallersOnly(EntryPoint = "nexa_instrumentation_probe_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static int Configure(int index, int enabled)
    {
        try
        {
            var targets = Targets.Value;
            if ((uint)index >= (uint)targets.Length || enabled is < 0 or > 1
                || targets[index].Status != PatchTargetStatus.Instrumented) return -1;
            lock (Gate)
            {
                if (enabled == 0)
                {
                    if (Probes.Remove(index, out var removed)) removed.Hook?.Dispose();
                }
                else if (!Probes.ContainsKey(index))
                {
                    var probe = new Probe();
                    probe.Hook = Patch.Install(targets[index], PatchPhase.Head, probe.Enter);
                    Probes.Add(index, probe);
                }
            }
            return 0;
        }
        catch { return -1; }
    }

    [UnmanagedCallersOnly(EntryPoint = "nexa_instrumentation_hits_v1", CallConvs = [typeof(CallConvCdecl)])]
    private static long Hits(int index)
    {
        try
        {
            lock (Gate) return Probes.TryGetValue(index, out var probe) ? Interlocked.Read(ref probe.Hits) : -1;
        }
        catch { return -1; }
    }
}
