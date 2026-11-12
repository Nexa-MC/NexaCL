using System.Globalization;
using Nexa.Native.Minecraft.Services;
using Nexa.Native.Services.Wire;
using Nexa.Services.Tasks;

namespace Nexa.Native.Minecraft.Client;

public sealed partial class NativeMinecraftClient : IExternalTaskCenterSource
{
    public string Prefix => "native-minecraft:" + _context.ToString(CultureInfo.InvariantCulture) + ":";
    private Action<long, IReadOnlyList<TaskCenterEntry>>? _tasks;
    private long _taskRevision;
    private IReadOnlyList<TaskCenterEntry> _taskSnapshot = [];
    public void AttachTasks(Action<long, IReadOnlyList<TaskCenterEntry>> publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        long revision; IReadOnlyList<TaskCenterEntry> entries;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_tasks is not null) throw new InvalidOperationException("Minecraft task source is already attached.");
            _tasks = publish;
            revision = _taskRevision; entries = _taskSnapshot;
        }
        // The consumer rejects older revisions if a newer publication overtakes this replay.
        if (revision > 0) publish(revision, entries);
    }
    private byte[] ApplyTaskState(ServiceWireReader reader, long revision, int count)
    {
        TaskCenterEntry[] entries = new TaskCenterEntry[count];
        for (int index = 0; index < count; index++) entries[index] = reader.TaskCenterEntry() ?? throw new InvalidDataException("Missing native task.");
        reader.End();
        Action<long, IReadOnlyList<TaskCenterEntry>>? publish;
        IReadOnlyList<TaskCenterEntry> snapshot = Array.AsReadOnly(entries);
        lock (_gate)
        {
            if (revision <= _taskRevision) return [];
            _taskRevision = revision; _taskSnapshot = snapshot; publish = _tasks;
        }
        publish?.Invoke(revision, snapshot); return [];
    }
    public async Task<bool> RetryAsync(string taskId, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.TaskRetry, writer => writer.String(taskId), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); bool result = reader.Boolean(); reader.End(); return result;
    }
    public async Task<TaskCenterCancelResult> CancelAsync(string taskId, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.TaskCancel, writer => writer.String(taskId), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); int result = reader.Int32(); reader.End();
        if (!Enum.IsDefined(typeof(TaskCenterCancelResult), result)) throw new InvalidDataException("Invalid native task cancellation result.");
        return (TaskCenterCancelResult)result;
    }
    public async Task<bool> DismissAsync(string taskId, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.TaskDismiss, writer => writer.String(taskId), token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); bool result = reader.Boolean(); reader.End(); return result;
    }
    public async Task<int> ClearFinishedAsync(CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.TaskClear, _ => { }, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); int result = reader.Int32(); reader.End();
        if (result < 0) throw new InvalidDataException("Invalid native task cleanup count."); return result;
    }
    public async Task<bool> DismissFinishedAsync(IReadOnlyList<TaskCenterEntry> expected, CancellationToken token = default)
    {
        byte[] bytes = await InvokeAsync(MinecraftServiceProtocol.TaskDismissFinished, writer =>
        { writer.Count(expected.Count); foreach (var entry in expected) writer.TaskCenterEntry(entry); }, token).ConfigureAwait(false);
        using var reader = new ServiceWireReader(bytes); bool result = reader.Boolean(); reader.End(); return result;
    }
}
