using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Native.Minecraft;

internal sealed class NativeMinecraftLibraryOwner : IAsyncDisposable
{
    private readonly XsrStateStore _store;
    private readonly IMinecraftInstanceSource _source;
    private readonly IMinecraftLibraryPersistence _persistence;
    private readonly SemaphoreSlim _mutation = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _scanStop;
    private MinecraftLibraryConfiguration _configuration;
    private string _revision;
    private readonly XsrError? _configurationError;
    private MinecraftLibrarySnapshot _snapshot;
    private long _generation;
    private readonly object _scanGate = new();
    private readonly HashSet<Task> _scans = [];
    private bool _closed;
    private static StringComparer Comparer => Nexa.Core.PathIdentity.Comparer;
    internal NativeMinecraftLibraryOwner(XsrStateStore store, IMinecraftInstanceSource source, IMinecraftLibraryPersistence persistence,
        string fallback, MinecraftLibraryStoredConfiguration saved)
    {
        _store = store; _source = source; _persistence = persistence; _revision = saved.Revision;
        _configurationError = saved.Error;
        try { _configuration = Normalize(saved.Configuration ?? new(1, fallback, [new(fallback)])); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException)
        { _configuration = new(1, NormalizePath(fallback), [new(NormalizePath(fallback))]); _configurationError = InvalidConfiguration(); }
        _snapshot = new(0, _configuration.Directories, _configuration.ActiveDirectory, [], "", false, _configurationError); Publish(_snapshot);
    }
    private static string NormalizePath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute Minecraft directory is required.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    private static MinecraftLibraryConfiguration Normalize(MinecraftLibraryConfiguration value)
    {
        if (value.SchemaVersion != 1 || value.Directories.Count is 0 or > 1024 || value.Directories.Any(item => item is null)) throw new InvalidDataException("Unsupported library configuration.");
        var directories = value.Directories.Select(item => new MinecraftLibraryDirectory(NormalizePath(item.Path), item.SelectedInstanceId ?? "", item.Name?.Trim() ?? "")).DistinctBy(item => item.Path, Comparer).ToArray();
        string active;
        try { active = NormalizePath(value.ActiveDirectory); } catch (ArgumentException) { active = directories[0].Path; }
        active = directories.FirstOrDefault(item => Comparer.Equals(item.Path, active))?.Path ?? directories[0].Path;
        return new(1, active, Array.AsReadOnly(directories));
    }
    private static XsrError InvalidConfiguration() => new(XsrErrorKind.Rejected, XsrSemanticId.Parse("settings.invalid_value"), "The saved library document is invalid or unsupported.");
    private async Task<XsrResult> SaveAsync(MinecraftLibraryConfiguration configuration, CancellationToken token)
    {
        if (_configurationError is { } error) return XsrResult.Failure(error);
        var saved = await _persistence.SaveAsync(new(_revision, configuration), token).ConfigureAwait(false);
        if (!saved.IsSuccess) return XsrResult.Failure(saved.Error!);
        _configuration = configuration; _revision = saved.Value!.Revision; return XsrResult.Success();
    }
    private void Publish(MinecraftLibrarySnapshot snapshot)
    { _snapshot = snapshot with { Revision = checked(_snapshot.Revision + 1) }; _store.Publish(_store.Resolve(MinecraftLibraryContract.StateKey), _snapshot); }
    private Task<XsrResult> BeginScan(CancellationToken token)
    {
        _scanStop?.Cancel(); _scanStop?.Dispose(); _scanStop = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        long generation = ++_generation; string root = _configuration.ActiveDirectory;
        bool same = Comparer.Equals(root, _snapshot.RootDirectory);
        var shown = same ? _snapshot.Instances : [];
        Publish(new(_snapshot.Revision, _configuration.Directories, root, shown, same ? _snapshot.SelectedInstanceId : "", true) { IsProvisional = shown.Count > 0 });
        var scan = ScanAsync(root, generation, _scanStop.Token);
        lock (_scanGate) _scans.Add(scan);
        _ = scan.ContinueWith(task => { _ = task.Exception; lock (_scanGate) _scans.Remove(task); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return scan;
    }
    internal async Task<XsrResult> RefreshAsync(CancellationToken token)
    {
        Task<XsrResult> scan;
        await _mutation.WaitAsync(token).ConfigureAwait(false);
        try { ObjectDisposedException.ThrowIf(_closed, this); scan = BeginScan(token); }
        finally { _mutation.Release(); }
        return await scan.ConfigureAwait(false);
    }
    private async Task<XsrResult> ScanAsync(string root, long generation, CancellationToken token)
    {
        await Task.Yield();
        IReadOnlyList<MinecraftInstanceDescriptor> instances = []; XsrError? error = _configurationError;
        try
        {
            if (_source is IMinecraftInstanceSnapshotSource persisted && await persisted.LoadSnapshotAsync(root, token).ConfigureAwait(false) is { } cached)
            {
                await _mutation.WaitAsync(token).ConfigureAwait(false);
                try { if (!_closed && generation == _generation && _snapshot.Instances.Count == 0) Publish(_snapshot with { Instances = cached.Instances, IsProvisional = true }); }
                finally { _mutation.Release(); }
            }
            if (!Directory.Exists(root)) throw new IOException("The Minecraft directory is unavailable.");
            instances = await _source.DiscoverAsync(root, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { error = XsrRuntimeErrors.Cancelled(); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = MinecraftErrors.InvalidRequest("the directory could not be scanned."); }
        await _mutation.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_closed || generation != _generation) return XsrResult.Failure(XsrRuntimeErrors.Cancelled());
            if (token.IsCancellationRequested) error = XsrRuntimeErrors.Cancelled();
            string remembered = _configuration.Directories.First(item => Comparer.Equals(item.Path, root)).SelectedInstanceId;
            string selected = instances.Any(item => item.Id == remembered) ? remembered : instances.Count > 0 ? instances[0].Id : "";
            if (error is null && selected.Length > 0 && selected != remembered)
            {
                var saved = await SaveAsync(Remember(root, selected), token).ConfigureAwait(false);
                if (!saved.IsSuccess) error = saved.Error;
            }
            if (error is not null)
            { Publish(_snapshot with { IsLoading = false, Error = error, IsProvisional = _snapshot.Instances.Count > 0 }); return XsrResult.Failure(error); }
            Publish(new(_snapshot.Revision, _configuration.Directories, root, Array.AsReadOnly(instances.ToArray()), selected, false)); return XsrResult.Success();
        }
        finally { _mutation.Release(); }
    }
    private MinecraftLibraryConfiguration Remember(string root, string id) => _configuration with
    { Directories = Array.AsReadOnly(_configuration.Directories.Select(item => Comparer.Equals(item.Path, root) ? item with { SelectedInstanceId = id } : item).ToArray()) };
    internal async Task<XsrResult> ChangeDirectoryAsync(MinecraftLibraryDirectoryCommand command, CancellationToken token)
    {
        string root;
        try { root = NormalizePath(command.Path); } catch (ArgumentException error) { return XsrResult.Failure(MinecraftErrors.InvalidRequest(error.Message)); }
        Task<XsrResult> scan;
        await _mutation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            var existing = _configuration.Directories.FirstOrDefault(item => Comparer.Equals(item.Path, root));
            if (existing is null && (!command.Add || !Directory.Exists(root))) return XsrResult.Failure(MinecraftErrors.InvalidRequest("the directory is not registered or unavailable."));
            var configuration = _configuration with { ActiveDirectory = existing?.Path ?? root, Directories = existing is null ? [.. _configuration.Directories, new(root)] : _configuration.Directories };
            var saved = await SaveAsync(configuration, token).ConfigureAwait(false); if (!saved.IsSuccess) return saved;
            scan = BeginScan(token);
        }
        finally { _mutation.Release(); }
        return await scan.ConfigureAwait(false);
    }
    internal async Task<XsrResult> ForgetAsync(MinecraftLibraryForgetCommand command, CancellationToken token)
    {
        string path;
        try { path = NormalizePath(command.Path); } catch (ArgumentException error) { return XsrResult.Failure(MinecraftErrors.InvalidRequest(error.Message)); }
        Task<XsrResult>? scan = null;
        await _mutation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            var remaining = _configuration.Directories.Where(item => !Comparer.Equals(item.Path, path)).ToArray();
            if (remaining.Length == 0 || remaining.Length == _configuration.Directories.Count) return XsrResult.Failure(MinecraftErrors.InvalidRequest("keep at least one registered directory."));
            bool refresh = Comparer.Equals(_configuration.ActiveDirectory, path);
            var saved = await SaveAsync(_configuration with { Directories = remaining, ActiveDirectory = refresh ? remaining[0].Path : _configuration.ActiveDirectory }, token).ConfigureAwait(false);
            if (!saved.IsSuccess) return saved;
            if (refresh) scan = BeginScan(token); else Publish(_snapshot with { Directories = remaining });
        }
        finally { _mutation.Release(); }
        return scan is null ? XsrResult.Success() : await scan.ConfigureAwait(false);
    }
    internal async Task<XsrResult> SelectAsync(MinecraftLibrarySelectCommand command, CancellationToken token)
    {
        await _mutation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_snapshot.IsProvisional || _snapshot.IsLoading || !Comparer.Equals(command.RootDirectory, _configuration.ActiveDirectory) || !_snapshot.Instances.Any(item => item.Id == command.InstanceId))
                return XsrResult.Failure(MinecraftErrors.InstanceNotFound(command.InstanceId));
            var saved = await SaveAsync(Remember(command.RootDirectory, command.InstanceId), token).ConfigureAwait(false);
            if (saved.IsSuccess) Publish(_snapshot with { Directories = _configuration.Directories, SelectedInstanceId = command.InstanceId, Error = null }); return saved;
        }
        finally { _mutation.Release(); }
    }
    internal async Task<XsrResult> RenameDirectoryAsync(MinecraftLibraryRenameCommand command, CancellationToken token)
    {
        await _mutation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            var directory = _configuration.Directories.FirstOrDefault(item => Comparer.Equals(item.Path, command.Path)); string name = command.Name.Trim();
            if (directory is null || directory.IsOfficial || name.Length > 80 || name.Any(char.IsControl)) return XsrResult.Failure(MinecraftErrors.InvalidRequest("the directory name cannot be changed."));
            var saved = await SaveAsync(_configuration with { Directories = _configuration.Directories.Select(item => item == directory ? item with { Name = name } : item).ToArray() }, token).ConfigureAwait(false);
            if (saved.IsSuccess) Publish(_snapshot with { Directories = _configuration.Directories }); return saved;
        }
        finally { _mutation.Release(); }
    }
    internal async Task<XsrResult> RememberRenamedAsync(string root, string previous, string current, CancellationToken token)
    {
        await _mutation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (!MinecraftVersionPaths.IsSafeReference(current)) return XsrResult.Failure(MinecraftErrors.InvalidRequest("版本名称无效。"));
            if (!_configuration.Directories.Any(item => Comparer.Equals(item.Path, root) && item.SelectedInstanceId == previous)) return XsrResult.Success();
            var saved = await SaveAsync(Remember(root, current), token).ConfigureAwait(false);
            if (saved.IsSuccess && Comparer.Equals(root, _configuration.ActiveDirectory)) { ++_generation; _scanStop?.Cancel(); }
            return saved;
        }
        finally { _mutation.Release(); }
    }
    internal async Task<XsrResult> DeleteAsync(MinecraftLibraryDeleteCommand command, CancellationToken token)
    {
        using var recovery = await InstanceRecoveryOperationGate.EnterOperationAsync(Path.GetFullPath(command.RootDirectory), token).ConfigureAwait(false);
        Task<XsrResult> scan;
        await _mutation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_snapshot.IsProvisional || _snapshot.IsLoading || !Comparer.Equals(command.RootDirectory, _configuration.ActiveDirectory)
                || !MinecraftVersionPaths.IsSafeReference(command.InstanceId) || !_snapshot.Instances.Any(item => item.Id == command.InstanceId)) return XsrResult.Failure(MinecraftErrors.InvalidRequest("版本路径无效。"));
            string source = Path.Combine(command.RootDirectory, "versions", command.InstanceId);
            if (_store.ReadCollection<MinecraftProcessSnapshot>(_store.Resolve(MinecraftProcessStateComposition.SessionsKey), token).Items.Any(item => item.State is MinecraftProcessState.Created or MinecraftProcessState.Running
                && (Comparer.Equals(item.InstanceDirectory, source) || Comparer.Equals(item.GameDirectory, source)))) return XsrResult.Failure(MinecraftErrors.InvalidRequest("请先结束此版本的游戏进程。"));
            string recycle = Path.Combine(command.RootDirectory, ".recycle");
            for (string? path = source; path is not null; path = Path.GetDirectoryName(path)) if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return XsrResult.Failure(MinecraftErrors.InvalidRequest("不能移除链接目录中的版本。"));
            if (Directory.Exists(recycle) && (File.GetAttributes(recycle) & FileAttributes.ReparsePoint) != 0) return XsrResult.Failure(MinecraftErrors.InvalidRequest("不能移除链接目录中的版本。"));
            Directory.CreateDirectory(recycle); token.ThrowIfCancellationRequested(); Directory.Move(source, Path.Combine(recycle, command.InstanceId + "-" + Guid.NewGuid().ToString("N")));
            scan = BeginScan(token);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return XsrResult.Failure(MinecraftErrors.InvalidRequest(error.Message)); }
        finally { _mutation.Release(); }
        return await scan.ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        await _mutation.WaitAsync().ConfigureAwait(false);
        try { if (_closed) return; _closed = true; ++_generation; _lifetime.Cancel(); _scanStop?.Cancel(); }
        finally { _mutation.Release(); }
        Task[] scans; lock (_scanGate) scans = _scans.ToArray();
        try { await Task.WhenAll(scans).ConfigureAwait(false); } catch (OperationCanceledException) { }
        _scanStop?.Dispose(); _lifetime.Dispose(); _mutation.Dispose();
    }
}
