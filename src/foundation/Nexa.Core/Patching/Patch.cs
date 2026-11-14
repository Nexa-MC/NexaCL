using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;

#if NEXA_NATIVE_ENGINE
namespace Nexa.Native.Instrumentation;
#else
namespace Nexa.Core.Patching;
#endif

public enum PatchPhase : byte { Head = 1, Args = 2, Tail = 3, Return = 4, Replace = 5 }
public enum PatchTargetStatus : byte { Instrumented, Abstract, RuntimeProvided, Infrastructure }
[Flags]
public enum PatchTypeTraits : byte { None = 0, Value = 1, ByReference = 2, StackOnly = 4, Out = 8, Address = 16 }
public sealed record PatchParameter(string Name, string Type, PatchTypeTraits Flags);
public sealed record PatchTarget(string Id, string Signature, string ResultType, PatchTypeTraits ResultFlags,
    IReadOnlyList<PatchParameter> Parameters, PatchTargetStatus Status, int Slot)
{
    internal PatchAssembly? Owner { get; set; }
}

/// <summary>A local typed reference; CLR values never become Sidecar payloads.</summary>
public sealed record PatchReference(int Argument);

/// <summary>Assembly-local sites emitted by the compiler. A null bitmap is the unpatched fast path.</summary>
public sealed class PatchAssembly
{
    // Public solely for generated IL. Published arrays are immutable.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Fixed generated-IL fast-path ABI; published arrays are immutable.")]
    public byte[]? Enabled;
    private readonly Lazy<ReadOnlyCollection<PatchTarget>> _targets;
    private PatchHook[][]? _hooks;
    internal PatchAssembly(string name, string catalog)
    {
        Name = name;
        _targets = new(() => ReadCatalog(catalog));
    }
    public string Name { get; }
    public IReadOnlyList<PatchTarget> Targets => _targets.Value;
    internal PatchHook[] Hooks(int slot) => Volatile.Read(ref _hooks) is { } table ? table[slot] : [];

    internal void Update(int slot, PatchHook hook, bool remove)
    {
        var old = Volatile.Read(ref _hooks);
        var table = old is null ? new PatchHook[Targets.Count(target => target.Status == PatchTargetStatus.Instrumented)][] : (PatchHook[][])old.Clone();
        for (int index = 0; index < table.Length; index++) table[index] ??= [];
        table[slot] = remove ? table[slot].Where(item => item != hook).ToArray() : [.. table[slot], hook];
        byte[]? flags = table.Any(items => items.Length != 0) ? table.Select(items => items.Length == 0 ? (byte)0 : (byte)1).ToArray() : null;
        Volatile.Write(ref _hooks, table);
        Volatile.Write(ref Enabled, flags);
    }

    private ReadOnlyCollection<PatchTarget> ReadCatalog(string encoded)
    {
        byte[] compressed = Convert.FromBase64String(encoded);
        using var source = new MemoryStream(compressed, writable: false);
        using var stream = new BrotliStream(source, CompressionMode.Decompress);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadUInt32() != 0x3143504E) throw new InvalidDataException("Unsupported compiled patch catalog.");
        int count = reader.ReadInt32();
        if (count is < 0 or > 1_000_000) throw new InvalidDataException("Invalid patch catalog count.");
        PatchTarget[] targets = new PatchTarget[count];
        for (int index = 0; index < count; index++)
        {
            string id = reader.ReadString(), signature = reader.ReadString(), result = reader.ReadString();
            var resultFlags = (PatchTypeTraits)reader.ReadByte();
            var status = (PatchTargetStatus)reader.ReadByte(); int slot = reader.ReadInt32();
            int arguments = reader.ReadUInt16();
            PatchParameter[] parameters = new PatchParameter[arguments];
            for (int parameter = 0; parameter < arguments; parameter++)
                parameters[parameter] = new(reader.ReadString(), reader.ReadString(), (PatchTypeTraits)reader.ReadByte());
            targets[index] = new(id, signature, result, resultFlags, Array.AsReadOnly(parameters), status, slot) { Owner = this };
        }
        return Array.AsReadOnly(targets);
    }
}

internal sealed class PatchHook(PatchTarget target, PatchPhase phase, Action<PatchFrame> apply, PatchBatch batch)
{
    private int _faulted;
    internal PatchTarget Target { get; } = target;
    internal PatchPhase Phase { get; } = phase;
    internal Action<PatchFrame> Apply { get; } = apply;
    internal bool Active => batch.Active && Volatile.Read(ref _faulted) == 0;
    internal void Fault() => Interlocked.Exchange(ref _faulted, 1);
}

public sealed class PatchBatch : IDisposable
{
    private readonly object _gate = new();
    private readonly List<PatchHook> _hooks = [];
    private int _state;
    internal bool Active => Volatile.Read(ref _state) == 1;
    public void Add(PatchTarget target, PatchPhase phase, Action<PatchFrame> apply)
    {
        lock (_gate)
        {
            if (_state != 0) throw new InvalidOperationException("Patch batch is already committed or retired.");
            var hook = Patch.Add(target, phase, apply, this); _hooks.Add(hook);
        }
    }
    public void Commit()
    {
        lock (_gate)
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) throw new InvalidOperationException("Patch batch is not pending.");
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _state, 2) == 2) return;
            foreach (var hook in _hooks) Patch.Remove(hook);
            _hooks.Clear();
        }
    }
}

/// <summary>Build-injected local interception. No reflection, runtime code generation or IPC.</summary>
public static class Patch
{
    public const int MaximumHooks = 4096;
    public const int MaximumHooksPerTarget = 64;
    private static readonly object Gate = new();
    private static readonly List<PatchAssembly> Assemblies = [];
    private static int _hookCount;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2211:Non-constant fields should not be visible", Justification = "Generated IL reads the thread-local recursion guard without method dispatch.")]
    [ThreadStatic] public static bool Evaluating;
    public static object DefaultValue { get; } = new();
    public static object OriginalReturn { get; } = new PatchReference(-1);

    public static PatchAssembly Register(string assembly, string catalog)
    {
        var sites = new PatchAssembly(assembly, catalog);
        lock (Gate) Assemblies.Add(sites);
        return sites;
    }
    public static IReadOnlyList<PatchTarget> Catalog
    {
        get
        {
            PatchAssembly[] assemblies; lock (Gate) assemblies = Assemblies.ToArray();
            return assemblies.SelectMany(assembly => assembly.Targets).OrderBy(target => target.Id, StringComparer.Ordinal).ToArray();
        }
    }
    public static PatchTarget? Find(string id)
    {
        PatchAssembly[] assemblies; lock (Gate) assemblies = Assemblies.ToArray();
        return assemblies.SelectMany(assembly => assembly.Targets).FirstOrDefault(target => target.Id == id);
    }
    public static IDisposable Install(PatchTarget target, PatchPhase phase, Action<PatchFrame> apply)
    {
        var batch = BeginBatch();
        try { batch.Add(target, phase, apply); batch.Commit(); return batch; }
        catch { batch.Dispose(); throw; }
    }
    public static PatchBatch BeginBatch() => new();
    internal static PatchHook Add(PatchTarget target, PatchPhase phase, Action<PatchFrame> apply, PatchBatch batch)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(apply);
        if (target.Status != PatchTargetStatus.Instrumented || target.Owner is null || !Enum.IsDefined(phase))
            throw new ArgumentException("The target has no instrumented managed entry.", nameof(target));
        var hook = new PatchHook(target, phase, apply, batch);
        lock (Gate)
        {
            if (_hookCount >= MaximumHooks || target.Owner.Hooks(target.Slot).Length >= MaximumHooksPerTarget)
                throw new InvalidOperationException("Automatic function activation budget exceeded.");
            target.Owner.Update(target.Slot, hook, remove: false);
            _hookCount++;
        }
        return hook;
    }
    internal static void Remove(PatchHook hook)
    {
        lock (Gate)
        {
            hook.Target.Owner!.Update(hook.Target.Slot, hook, remove: true);
            _hookCount--;
        }
    }

    public static object Reference(int argument) => new PatchReference(argument);
    public static PatchFrame? Enter(PatchAssembly assembly, int slot, object? receiver, object?[] arguments)
    {
        if (Evaluating) return null;
        var hooks = assembly.Hooks(slot);
        if (hooks.Length == 0 || !hooks.Any(hook => hook.Active)) return null;
        var frame = new PatchFrame(hooks, receiver, arguments);
        frame.Run(PatchPhase.Head); frame.Run(PatchPhase.Args); frame.Run(PatchPhase.Replace);
        return frame;
    }
}

/// <summary>Ephemeral invocation data. Opaque values stay local; stack-only values use typed compiler references.</summary>
public sealed class PatchFrame
{
    private readonly PatchHook[] _hooks;
    internal PatchFrame(PatchHook[] hooks, object? receiver, object?[] arguments)
    { _hooks = hooks; Receiver = receiver; Arguments = arguments; }
    public PatchTarget Target => _hooks[0].Target;
    public object? Receiver { get; }
    public object?[] Arguments { get; }
    public object? Result { get; set; } = Patch.DefaultValue;
    public bool SkipOriginal { get; set; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? Argument(int index) => Arguments[index];
    public int ArgumentSource(int index) => Arguments[index] is PatchReference reference ? reference.Argument : ReferenceEquals(Arguments[index], Patch.DefaultValue) ? -2 : -3;
    public int ReturnSource() => Result is PatchReference reference ? reference.Argument : ReferenceEquals(Result, Patch.DefaultValue) ? -2 : -3;
    public bool ShouldSkip()
    {
        if (!SkipOriginal) return false;
        if (Target.ResultFlags.HasFlag(PatchTypeTraits.ByReference)
            && (Result is not PatchReference reference || reference.Argument < 0
                || reference.Argument >= Target.Parameters.Count || Target.Parameters[reference.Argument].Type != Target.ResultType))
            return false;
        return true;
    }
    public void Complete(object? result)
    { Result = result; Run(PatchPhase.Tail); Run(PatchPhase.Return); }

    internal void Run(PatchPhase phase)
    {
        bool prior = Patch.Evaluating; Patch.Evaluating = true;
        try
        {
            foreach (var hook in _hooks)
            {
                if (hook.Phase != phase || !hook.Active) continue;
                var before = (object?[])Arguments.Clone(); var result = Result; bool skip = SkipOriginal;
                try
                {
                    hook.Apply(this);
                    if (hook.Active) continue;
                }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { hook.Fault(); }
                before.CopyTo(Arguments, 0); Result = result; SkipOriginal = skip;
            }
        }
        finally { Patch.Evaluating = prior; }
    }
}
