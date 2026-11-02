using System.Security.Cryptography;
using System.Text;

namespace Nexa.Desktop;

/// <summary>Durable completion receipts; UI refreshes and session GUIDs never define a new guide.</summary>
internal sealed class DesktopOnboardingHistory(string directory, bool deferred = false)
{
    private readonly Dictionary<string, bool> _known = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private bool _batchPending;
    internal static string Identity(string package, string page, string revision) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(package + "\n" + page + "\n" + revision)));
    internal bool IsComplete(string identity)
    {
        if (_known.TryGetValue(identity, out bool completed)) return completed;
        return _known[identity] = File.Exists(Path.Combine(directory, identity + ".done"));
    }
    internal bool HasPendingBatch => _batchPending || File.Exists(Path.Combine(directory, "batch.pending"));
    internal void BeginBatch()
    { if (deferred) _batchPending = true; else WriteReceipt("batch.pending"); }
    internal void FinishBatch()
    { _batchPending = false; if (!deferred) File.Delete(Path.Combine(directory, "batch.pending")); }
    internal void Complete(string identity)
    {
        if (deferred) _pending.Add(identity);
        else WriteReceipt(identity + ".done");
        _known[identity] = true;
    }
    internal void ChangeDeferredDirectory(string selectedDirectory)
    {
        if (!deferred) throw new InvalidOperationException("Only uncommitted first-run receipts can change directory.");
        directory = selectedDirectory;
        _known.Clear();
        foreach (string identity in _pending) _known[identity] = true;
    }
    internal void Flush()
    {
        foreach (string identity in _pending) WriteReceipt(identity + ".done");
        if (deferred && !_batchPending && File.Exists(Path.Combine(directory, "batch.pending")))
            File.Delete(Path.Combine(directory, "batch.pending"));
        _pending.Clear();
    }
    private void WriteReceipt(string name)
    {
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, name), temporary = target + "." + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write("completed\n"u8); stream.Flush(flushToDisk: true); }
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
