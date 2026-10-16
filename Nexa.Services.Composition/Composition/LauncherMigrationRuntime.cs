using Nexa.Services.Logging;
using Nexa.Services.Minecraft.Install;
using Nexa.Services.Minecraft.Management;
using Nexa.Services.Setup;
using Nexa.Services.Tasks;
using Nexa.Xsr;
using Nexa.Xsr.Runtime;

namespace Nexa.Services.Composition;

/// <summary>Sealed migration routes over the existing installer and task center.</summary>
public static class LauncherMigrationRuntime
{
    public const string TaskPrefix = "launcher-migrate:";

    public static LauncherMigrationService CreateService(
        Func<LauncherMigrationInstallRequest, CancellationToken, Task>? install = null, Func<bool>? isIdle = null) =>
        new(install, isIdle, new ReceiptAuthority());

    private sealed class ReceiptAuthority : ILauncherMigrationReceiptAuthority
    {
        public Task AuthorizeAsync(string receiptPath, ReadOnlyMemory<byte> bytes, CancellationToken token) =>
            RecoveryRecordAuthority.AuthorizeAsync(receiptPath, bytes, token);

        public void Verify(string receiptPath, ReadOnlyMemory<byte> bytes) =>
            RecoveryRecordAuthority.Verify(receiptPath, bytes.Span);
    }

    public static IAsyncDisposable Register(XsrCommandRouterBuilder commands, XsrQueryRouterBuilder queries,
        LauncherMigrationService service, TaskCenterService tasks, Func<LauncherMigrationReceipt, bool>? canUndo = null)
    {
        var lifetime = new RouteLifetime();
        queries.Register<LauncherMigrationDiscoveryQuery, LauncherMigrationDiscoveryResult>(LauncherMigrationContract.Discover,
            (query, token) => QueryOwnedAsync(lifetime, cancellation => service.DiscoverSourcesAsync(query, cancellation), token));
        queries.Register<LauncherMigrationQuery, LauncherMigrationPreview>(LauncherMigrationContract.Preview,
            (query, token) => QueryOwnedAsync(lifetime, cancellation => service.PreviewAsync(query, cancellation), token));
        queries.Register<LauncherMigrationUndoQuery, LauncherMigrationReceipt>(LauncherMigrationContract.ReadReceipt,
            (query, token) => QueryOwnedAsync(lifetime, cancellation => service.ReadReceiptAsync(query.DestinationDirectory, cancellation), token));
        commands.Register<LauncherMigrationApplyCommand>(LauncherMigrationContract.Apply,
            (command, token) => ExecuteOwnedAsync(lifetime, tasks, "从其他启动器迁移", async (progress, cancellation) =>
            { await service.ApplyAsync(command, progress, cancellation).ConfigureAwait(false); }, token));
        commands.Register<LauncherMigrationUndoCommand>(LauncherMigrationContract.Undo,
            (command, token) => ExecuteOwnedAsync(lifetime, tasks, "撤销实例迁移",
                (_, cancellation) =>
                {
                    if (canUndo?.Invoke(command.Receipt) == false)
                        throw new IOException("请先保留至少一个不属于此次迁移的游戏目录，再撤销迁移。");
                    return service.UndoAsync(command.Receipt, cancellation);
                }, token));
        return lifetime;
    }

    private static async ValueTask<XsrResult<T>> QueryOwnedAsync<T>(RouteLifetime lifetime,
        Func<CancellationToken, Task<T>> action, CancellationToken token)
    {
        using var operation = lifetime.TryEnter(token);
        if (operation is null) return XsrResult.Failure<T>(XsrRuntimeErrors.Cancelled());
        return await QueryAsync(() => action(operation.Token), operation.Token).ConfigureAwait(false);
    }

    private static async ValueTask<XsrResult> ExecuteOwnedAsync(RouteLifetime lifetime, TaskCenterService tasks,
        string title, Func<IProgress<LauncherMigrationProgress>, CancellationToken, Task> action, CancellationToken token)
    {
        using var operation = lifetime.TryEnter(token);
        if (operation is null) return XsrResult.Failure(XsrRuntimeErrors.Cancelled());
        return await ExecuteAsync(tasks, title, action, operation.Token).ConfigureAwait(false);
    }

    public static async Task InstallAsync(MinecraftInstallService installer,
        LauncherMigrationInstallRequest request, CancellationToken token)
    {
        InstallLoader? loader = null;
        if (request.Loader is { Length: > 0 } value)
        {
            if (!Enum.TryParse<InstallLoader>(value, out var parsed) || !Enum.IsDefined(parsed))
                throw new InvalidDataException("无法转换此实例的加载器。");
            loader = parsed;
        }
        var result = await installer.InstallAsync(new(request.DestinationRoot, request.GameVersion,
            loader, request.LoaderBuild, InstanceName: request.InstanceId)
        { InheritVanilla = false, DefaultIsolationMode = "all" }, token).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            token.ThrowIfCancellationRequested();
            throw new IOException(result.Error?.Message ?? "无法安装迁入实例所需的游戏和加载器。");
        }
        await MinecraftInstallService.PrepareMigrationRootAsync(request.DestinationRoot, token).ConfigureAwait(false);
    }

    private static async ValueTask<XsrResult<T>> QueryAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        try { return XsrResult.Success(await Task.Run(action, token).ConfigureAwait(false)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return XsrResult.Failure<T>(XsrRuntimeErrors.Cancelled()); }
        catch (Exception error) when (Expected(error)) { return XsrResult.Failure<T>(Failure(error)); }
    }

    private static async ValueTask<XsrResult> ExecuteAsync(TaskCenterService tasks, string title,
        Func<IProgress<LauncherMigrationProgress>, CancellationToken, Task> action, CancellationToken token)
    {
        using var task = tasks.Begin(new(TaskPrefix + Guid.NewGuid().ToString("N"), title, ["迁移"]));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, task.CancellationToken);
        try
        {
            await Task.Run(() => action(new MigrationProgress(task), linked.Token), linked.Token).ConfigureAwait(false);
            task.Complete(); return XsrResult.Success();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { task.Canceled(); return XsrResult.Failure(XsrRuntimeErrors.Cancelled()); }
        catch (Exception error) when (Expected(error))
        { var failure = Failure(error); task.Fail(failure.Message); return XsrResult.Failure(failure); }
    }

    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException
        or InvalidOperationException or FormatException or System.Text.Json.JsonException or HttpRequestException
        or TimeoutException or OperationCanceledException or System.Security.SecurityException;

    private static XsrError Failure(Exception error) => new(XsrErrorKind.Rejected,
        XsrSemanticId.Parse("setup.migration.launcher.rejected"), LogRedactor.Redact(error.Message));

    private sealed class MigrationProgress(ITaskCenterTask task) : IProgress<LauncherMigrationProgress>
    {
        public void Report(LauncherMigrationProgress value) => task.Report("迁移",
            value.InstanceName.Length == 0 ? value.Stage : value.Stage + " · " + value.InstanceName,
            value.TotalBytes == 0 ? 0 : Math.Clamp((double)value.CompletedBytes / value.TotalBytes, 0, 1), 0, 0, 0);
    }

    private sealed class RouteLifetime : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private bool _closing;

        internal Operation? TryEnter(CancellationToken caller)
        {
            lock (_gate)
            {
                if (_closing) return null;
                var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _stop.Token);
                _active++;
                return new(this, linked);
            }
        }

        public ValueTask DisposeAsync()
        {
            bool first;
            lock (_gate)
            {
                first = !_closing;
                _closing = true;
                if (_active == 0) _drained.TrySetResult();
            }
            if (first) _ = CompleteShutdownAsync();
            return new(_closed.Task);
        }

        private async Task CompleteShutdownAsync()
        {
            Exception? failure = null;
            try { await _stop.CancelAsync().ConfigureAwait(false); }
            catch (Exception error) { failure = error; }
            await _drained.Task.ConfigureAwait(false);
            _stop.Dispose();
            if (failure is null) _closed.TrySetResult();
            else _closed.TrySetException(failure);
        }

        private void Exit()
        {
            lock (_gate)
                if (--_active == 0 && _closing) _drained.TrySetResult();
        }

        internal sealed class Operation(RouteLifetime owner, CancellationTokenSource linked) : IDisposable
        {
            private RouteLifetime? _owner = owner;
            internal CancellationToken Token => linked.Token;
            public void Dispose()
            {
                var current = Interlocked.Exchange(ref _owner, null);
                if (current is null) return;
                linked.Dispose();
                current.Exit();
            }
        }
    }
}
