using System.Globalization;
using System.Text;
using Nexa.Sidecar.Protocol;
using Nexa.Xsr.State;

namespace Nexa.Xsr.Runtime;

/// <summary>The artifact is local compiled presentation data; it never crosses the process boundary.</summary>
public sealed record XsrNxmlCompiledDocument(object Artifact, IReadOnlyList<XsrSemanticId> Commands);
public enum XsrNxmlCommandRoute { Inactive, Host, Sidecar }
public sealed record XsrNxmlModuleView(Guid Exposure, SidecarNxmlDocument Source, XsrNxmlCompiledDocument Presentation);
public sealed class XsrNxmlModuleSnapshot
{
    public IReadOnlyList<XsrNxmlModuleView> Modules { get; }
    internal XsrNxmlModuleSnapshot(XsrNxmlModuleView[] modules) => Modules = Array.AsReadOnly(modules);
}

/// <summary>
/// Owns global NXML registration, compilation, state projection and asynchronous command dispatch.
/// The one shared compiler is supplied at composition; there are no per-page or per-control grants.
/// </summary>
public sealed class XsrNxmlModuleRuntime
{
    private readonly object _gate = new();
    private readonly XsrStateStore _store;
    private readonly XsrStateId _state;
    private readonly Func<SidecarNxmlDocument, IReadOnlyDictionary<string, string>, XsrNxmlCompiledDocument> _compile;
    private readonly List<Lease> _leases = [];
    private XsrNxmlModuleSnapshot? _pending;
    private bool _publishing;

    public XsrNxmlModuleRuntime(XsrStateStore store, XsrStateId state,
        Func<SidecarNxmlDocument, IReadOnlyDictionary<string, string>, XsrNxmlCompiledDocument> compile)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _state = state;
        _ = store.Read<XsrNxmlModuleSnapshot>(state);
        _compile = compile ?? throw new ArgumentNullException(nameof(compile));
    }

    internal static SidecarNxmlDocument[] Prepare(IEnumerable<SidecarRegistrationItem> declarations, SidecarFeatures features)
    {
        List<SidecarNxmlDocument> documents = [];
        long bytes = 0;
        foreach (var entry in declarations)
        {
            if (entry.Kind != SidecarRegistrationKind.UiModule || !SidecarNxmlDocument.HasSchema(entry.Payload ?? [])) continue;
            if (!features.HasFlag(SidecarFeatures.Nxml) || entry.Flags != 0 || entry.CodecId != 0)
                throw new SidecarProtocolException("NXML modules require negotiated support and zero flags/codec.");
            bytes += entry.Payload!.Length;
            if (documents.Count >= 32 || bytes > 4 * 1024 * 1024) throw new SidecarProtocolException("Session NXML source budget exceeded.");
            documents.Add(SidecarNxmlDocument.Decode(entry.Payload));
        }
        return documents.ToArray();
    }

    internal XsrNxmlCompiledDocument[] Compile(SidecarNxmlDocument[] documents, SidecarHostSession session)
    {
        if (documents.Length == 0) return [];
        var data = ReadData(session);
        return documents.Select(document => _compile(document, data)).ToArray();
    }

    internal Lease Activate(SidecarNxmlDocument[] documents, XsrNxmlCompiledDocument[] compiled, SidecarHostSession session)
    {
        var lease = new Lease(this, documents, compiled, session);
        lock (_gate)
        {
            if (_leases.Sum(item => item.Documents.Length) + documents.Length > 128)
                throw new SidecarProtocolException("Active NXML document budget exceeded.");
            _leases.Add(lease); QueueLocked();
        }
        lease.Attach();
        return lease;
    }

    private void QueueLocked() => _pending = new(_leases.SelectMany(lease => lease.Documents.Select((document, index) =>
        new XsrNxmlModuleView(lease.Exposures[index], document, lease.Compiled[index]))).ToArray());

    internal void Publish()
    {
        lock (_gate) { if (_publishing) return; _publishing = true; }
        try
        {
            while (true)
            {
                XsrNxmlModuleSnapshot? snapshot;
                lock (_gate) { snapshot = _pending; _pending = null; if (snapshot is null) { _publishing = false; return; } }
                _store.Publish(_state, snapshot);
            }
        }
        catch { lock (_gate) _publishing = false; throw; }
    }

    public void RejectPresentation(Guid exposure)
    {
        Lease? lease;
        lock (_gate) lease = _leases.FirstOrDefault(item => item.Exposures.Contains(exposure));
        lease?.Dispose();
    }

    public XsrNxmlCommandRoute ReadCommandRoute(Guid exposure, XsrSemanticId command)
    {
        lock (_gate)
        {
            var lease = _leases.FirstOrDefault(item => item.Exposures.Contains(exposure));
            if (lease is null || lease.Retired) return XsrNxmlCommandRoute.Inactive;
            int index = Array.IndexOf(lease.Exposures, exposure);
            if (!lease.Compiled[index].Commands.Contains(command)) return XsrNxmlCommandRoute.Inactive;
            return lease.Session.Registration?.TryResolve(SidecarRegistrationKind.Command, command) is not null
                ? XsrNxmlCommandRoute.Sidecar : XsrNxmlCommandRoute.Host;
        }
    }

    public async ValueTask<XsrResult> DispatchAsync(Guid exposure, XsrSemanticId command, string argument,
        CancellationToken cancellationToken = default)
    {
        Lease? lease; int index;
        lock (_gate)
        {
            lease = _leases.FirstOrDefault(item => item.Exposures.Contains(exposure));
            index = lease is null ? -1 : Array.IndexOf(lease.Exposures, exposure);
            if (lease is null || lease.Retired || !lease.Compiled[index].Commands.Contains(command))
                return XsrResult.Failure(XsrRuntimeErrors.ContractMismatch());
        }
        var registered = lease.Session.Registration?.TryResolve(SidecarRegistrationKind.Command, command);
        if (registered is null || argument.Length > 32768) return XsrResult.Failure(XsrRuntimeErrors.ContractMismatch());
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Stop.Token);
        if (stop.IsCancellationRequested) return XsrResult.Failure(XsrRuntimeErrors.Cancelled());
        if (!lease.Session.NegotiatedFeatures.HasFlag(SidecarFeatures.BinaryPayloads))
            return await lease.Session.SendCommandByIdAsync(registered.ContractId, argument, TimeSpan.FromSeconds(10), stop.Token).ConfigureAwait(false);
        byte[] payload;
        try
        {
            payload = registered.CodecId switch
            {
                SidecarWireCodecs.Utf8String => Encoding.UTF8.GetBytes(argument),
                SidecarWireCodecs.Bool => [(byte)(bool.Parse(argument) ? 1 : 0)],
                SidecarWireCodecs.I32 => SidecarWireCodecs.Encode(registered.CodecId, int.Parse(argument, CultureInfo.InvariantCulture)),
                SidecarWireCodecs.I64 => SidecarWireCodecs.Encode(registered.CodecId, long.Parse(argument, CultureInfo.InvariantCulture)),
                SidecarWireCodecs.F64 => SidecarWireCodecs.Encode(registered.CodecId, double.Parse(argument, CultureInfo.InvariantCulture)),
                _ => throw new FormatException("UI command requires a supported scalar value."),
            };
        }
        catch (FormatException) { return XsrResult.Failure(XsrRuntimeErrors.ContractMismatch()); }
        var outcome = await lease.Session.SendBinaryCommandByIdAsync(registered.ContractId,
            new(registered.CodecId, payload), TimeSpan.FromSeconds(10), stop.Token).ConfigureAwait(false);
        return outcome.IsSuccess ? XsrResult.Success() : XsrResult.Failure(outcome.Error!);
    }

    private static Dictionary<string, string> ReadData(SidecarHostSession session)
    {
        var mirror = session.Mirror ?? throw new SidecarProtocolException("NXML state mirror is missing.");
        Dictionary<string, string> data = new(StringComparer.Ordinal);
        foreach (var entry in session.Registration!.Entries.Where(entry => entry.Kind == SidecarRegistrationKind.State))
        {
            var id = mirror.TryResolve(entry.SemanticId)!.Value;
            var text = entry.CodecId switch
            {
                SidecarWireCodecs.Utf8String => Text(mirror.Store.Read<string>(id)),
                SidecarWireCodecs.Bool => Text(mirror.Store.Read<bool>(id)),
                SidecarWireCodecs.I32 => Text(mirror.Store.Read<int>(id)),
                SidecarWireCodecs.I64 => Text(mirror.Store.Read<long>(id)),
                SidecarWireCodecs.F64 => Text(mirror.Store.Read<double>(id)),
                _ => null,
            };
            if (text is not null) data.Add(entry.SemanticId.Value, text);
        }
        return data;

        // Ready retains the coherent prior values while deactivated; activation restores availability
        // before publishing presentation. Recompilation must not revert to the initial snapshot.
        static string Text<T>(XsrStateValue<T> value) => value.HasValue
            ? Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "" : "";
    }

    internal sealed class Lease(XsrNxmlModuleRuntime owner, SidecarNxmlDocument[] documents,
        XsrNxmlCompiledDocument[] compiled, SidecarHostSession session) : IDisposable
    {
        internal SidecarNxmlDocument[] Documents { get; } = documents;
        internal XsrNxmlCompiledDocument[] Compiled { get; private set; } = compiled;
        internal Guid[] Exposures { get; } = documents.Select(_ => Guid.NewGuid()).ToArray();
        internal SidecarHostSession Session { get; } = session;
        internal CancellationTokenSource Stop { get; } = new();
        private int _retired, _worker;
        private long _version;
        internal bool Retired => Volatile.Read(ref _retired) != 0;
        internal void Attach() { if (Session.Mirror is { } mirror) mirror.Store.Changed += OnChanged; }
        private void OnChanged(XsrStateChange change)
        {
            Interlocked.Increment(ref _version);
            if (!Retired && Interlocked.CompareExchange(ref _worker, 1, 0) == 0) _ = Task.Run(Refresh);
        }
        private void Refresh()
        {
            long version = -1;
            try
            {
                while (!Retired)
                {
                    version = Volatile.Read(ref _version);
                    var next = owner.Compile(Documents, Session);
                    lock (owner._gate)
                    {
                        if (Retired) return;
                        if (version != Volatile.Read(ref _version)) continue;
                        Compiled = next; owner.QueueLocked();
                    }
                    owner.Publish();
                    if (version == Volatile.Read(ref _version)) return;
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { Dispose(); }
            finally
            {
                Volatile.Write(ref _worker, 0);
                if (!Retired && version != Volatile.Read(ref _version)
                    && Interlocked.CompareExchange(ref _worker, 1, 0) == 0) _ = Task.Run(Refresh);
            }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _retired, 1) != 0) return;
            if (Session.Mirror is { } mirror) mirror.Store.Changed -= OnChanged;
            Stop.Cancel();
            lock (owner._gate) { owner._leases.Remove(this); owner.QueueLocked(); }
            owner.Publish();
        }
    }
}
