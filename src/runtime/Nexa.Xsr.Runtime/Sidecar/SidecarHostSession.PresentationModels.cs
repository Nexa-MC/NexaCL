using System.Collections.ObjectModel;
using System.Text;
using Nexa.Sidecar.Protocol;

namespace Nexa.Xsr.Runtime;

public sealed partial class SidecarHostSession
{
    private static readonly UTF8Encoding ModelUtf8 = new(false, true);

    private static IReadOnlyDictionary<string, string>[] OwnPresentationModels(int count,
        IReadOnlyList<IReadOnlyDictionary<string, string>>? models)
    {
        if (models is not null && models.Count != count) throw new SidecarProtocolException("Model count must match the document set.");
        long bytes = 0;
        var owned = new IReadOnlyDictionary<string, string>[count];
        for (int i = 0; i < count; i++)
        {
            var source = models?[i];
            if (models is not null && source is null || source?.Count > 4096)
                throw new SidecarProtocolException("Invalid document model.");
            Dictionary<string, string> copy = new(StringComparer.Ordinal);
            foreach (var item in source ?? new Dictionary<string, string>())
            {
                if (string.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 256 || item.Key.Any(char.IsControl) || item.Value is null)
                    throw new SidecarProtocolException("Invalid document model field.");
                int size = ModelUtf8.GetByteCount(item.Value);
                bytes += ModelUtf8.GetByteCount(item.Key) + size;
                if (size > 512 * 1024 || bytes > 4 * 1024 * 1024) throw new SidecarProtocolException("Session model text budget exceeded.");
                if (!copy.TryAdd(item.Key, item.Value)) throw new SidecarProtocolException("Duplicate document model key.");
            }
            owned[i] = new ReadOnlyDictionary<string, string>(copy);
        }
        return owned;
    }

    /// <summary>Changes selected document models while retaining exposure and control identity.</summary>
    public async ValueTask UpdatePresentationModelsAsync(long expectedRevision,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> updates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count is < 1 or > 32) throw new SidecarProtocolException("Invalid model update count.");
        // Clone even the caller-owned outer map before the first asynchronous wait.
        var indexes = updates.Keys.ToArray();
        var owned = OwnPresentationModels(indexes.Length, indexes.Select(index => updates[index]).ToArray());
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        XsrNxmlModuleRuntime.Lease? changed = null;
        try
        {
            IReadOnlyDictionary<string, string>[] candidate;
            lock (_gate)
            {
                ValidatePresentationRevision(expectedRevision);
                candidate = _nxmlModels.Length == _nxmlDocuments.Length ? _nxmlModels.ToArray() : OwnPresentationModels(_nxmlDocuments.Length, null);
                for (int i = 0; i < indexes.Length; i++)
                {
                    if ((uint)indexes[i] >= (uint)candidate.Length) throw new SidecarProtocolException("Document index is outside this session's set.");
                    candidate[indexes[i]] = owned[i];
                }
                candidate = OwnPresentationModels(candidate.Length, candidate);
            }
            var compiled = NxmlModules!.Compile(_nxmlDocuments, this, candidate);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ValidatePresentationRevision(expectedRevision);
                var lease = _nxmlLease ?? throw new InvalidOperationException("Presentation has retired.");
                lease.UpdateModels(candidate, compiled);
                changed = lease; _nxmlModels = candidate; _nxmlCompiled = compiled; _presentationRevision++;
            }
        }
        finally
        {
            changed?.RequestRefresh();
            _lifecycleGate.Release();
            if (changed is not null) NxmlModules!.Publish();
        }
    }
}
