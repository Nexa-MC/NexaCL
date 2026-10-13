using Nexa.Services.Composition;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Install;
using Nexa.Xsr;
using Nexa.Xsr.Renderer.Backend.Avalonia;
using Nexa.Xsr.Runtime;
using Nexa.Xsr.State;

namespace Nexa.Desktop.Ui;

/// <summary>Converts native paths to Service queries and user-confirmed commands.</summary>
internal sealed class MinecraftDropController : IDisposable
{
    private readonly AvaloniaUiPlatformActions _platform;
    private readonly MinecraftFolderImportRuntime _imports;
    private readonly XsrCommandRouter _library;
    private readonly XsrStateStore _store;
    private readonly DesktopFeedbackService _feedback;
    private readonly CancellationTokenSource _lifetime = new();
    private int _busy;
    private bool _disposed;
    private Guid _jarDialog;
    private readonly Queue<string> _fileActivations = new(16);
    private readonly object _activationGate = new();

    public MinecraftDropController(AvaloniaUiPlatformActions platform, MinecraftFolderImportRuntime imports,
        XsrCommandRouter library, XsrStateStore store, DesktopFeedbackService feedback)
    {
        _platform = platform; _imports = imports; _library = library; _store = store; _feedback = feedback;
        platform.FilesDropped += OnDrop;
    }

    private void OnDrop(IReadOnlyList<string> paths)
    {
        if (_disposed) return;
        if (paths.Count != 1) { _feedback.Error(ProductPresentationText.Raw("native.drop.single-item")); return; }
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        var snapshot = (MinecraftLibrarySnapshot?)_store.ReadAppliedValue(_store.Resolve(MinecraftLibraryContract.StateKey));
        _ = InspectAsync(paths[0], snapshot?.RootDirectory, snapshot?.SelectedInstanceId ?? "");
    }

    internal void OpenFile(string file)
    {
        if (_disposed) return;
        if (!DesktopActivation.TryFile(file, out string admitted)) { _feedback.Error(ProductPresentationText.Raw("native.drop.invalid-path")); return; }
        lock (_activationGate)
        {
            if (_fileActivations.Count >= 16) { _feedback.Error(ProductPresentationText.Raw("native.drop.pending-limit")); return; }
            _fileActivations.Enqueue(admitted);
        }
        PumpFileActivations();
    }

    private void PumpFileActivations()
    {
        _platform.PostToWindow(() =>
        {
            if (_disposed || Volatile.Read(ref _busy) != 0) return;
            string? file;
            lock (_activationGate) if (!_fileActivations.TryDequeue(out file)) return;
            OnDrop([file]);
        });
    }

    private void ReleaseBusy()
    {
        Interlocked.Exchange(ref _busy, 0);
        PumpFileActivations();
    }

    private async Task InspectAsync(string path, string? target, string instanceId)
    {
        bool awaitingChoice = false;
        try
        {
            if (!_imports.Queries.TryResolve(MinecraftFolderImportContract.Inspect, out var route)) return;
            var result = await _imports.Queries.QueryAsync<MinecraftFolderInspectQuery, MinecraftFolderInspection>(route, new(path), cancellationToken: _lifetime.Token).ConfigureAwait(false);
            if (_disposed) return;
            if (!result.IsSuccess) { _feedback.Error(result.Error?.Message ?? ProductPresentationText.Raw("native.drop.read-folder")); return; }
            var item = result.Value!;
            if (item.Kind is MinecraftFolderKind.Modpack or MinecraftFolderKind.Jar && target is null)
            { _feedback.Error(ProductPresentationText.Raw("native.drop.select-root")); return; }
            if (item.Modpack is { } pack && target is not null)
            {
                _jarDialog = _feedback.ShowDialog("modpack.install", NativeFormat("ModpackInstallTitle", ("name", pack.Name)),
                    NativeFormat("ModpackDetails", ("format", pack.Format), ("version", pack.Version), ("game", pack.Game),
                        ("loader", pack.Loader is null ? "" : NativeFormat("ModpackLoader", ("loader", pack.Loader.Value.ToString()), ("build", pack.Build ?? ""))),
                        ("root", target), ("instance", pack.InstanceId), ("required", pack.RequiredFiles.ToString(System.Globalization.CultureInfo.CurrentCulture)), ("optional", pack.OptionalFiles.ToString(System.Globalization.CultureInfo.CurrentCulture))),
                    ProductPresentationText.Raw("native.drop.modpack-install"), ProductPresentationText.Raw("native.common.cancel"), accepted =>
                    {
                        if (accepted) _ = ExecuteModpackAsync(pack, target, false);
                        else ReleaseBusy();
                    }, pack.OptionalFiles > 0 ? ProductPresentationText.Raw("native.drop.include-optional") : null, pack.OptionalFiles > 0 ? () =>
                    {
                        _feedback.DismissDialog(_jarDialog);
                        _ = ExecuteModpackAsync(pack, target, true);
                    }
                : null, localizeMessage: false, localizeTitle: false);
                awaitingChoice = true;
                return;
            }
            if (item.Jar is { } jar && target is not null)
            {
                _jarDialog = _feedback.ShowDialog("jar.use", NativeFormat("JarUseTitle", ("name", item.Name)),
                    string.IsNullOrEmpty(instanceId) ? ProductPresentationText.Term("native.drop.no-instance", _platform.LocalizeDesktopText)
                        : NativeFormat("JarTarget", ("instance", instanceId)),
                    ProductPresentationText.Raw("native.drop.add-mod"), ProductPresentationText.Raw("native.common.cancel"), accepted =>
                    {
                        if (accepted) _ = ExecuteJarAsync(jar, target, instanceId, LocalJarAction.Mod);
                        else ReleaseBusy();
                    }, ProductPresentationText.Raw("native.drop.other-uses"), () => ShowOtherJarUses(jar, target, instanceId), localizeMessage: false, localizeTitle: false);
                awaitingChoice = true;
                return;
            }
            if (item.Kind == MinecraftFolderKind.Unsupported)
            { _feedback.Error(ProductPresentationText.Raw("native.drop.unsupported")); return; }
            if (item.Kind == MinecraftFolderKind.Version && target is null) return;
            bool root = item.Kind == MinecraftFolderKind.GameRoot;
            _feedback.ShowDialog("folder.import", root ? ProductPresentationText.Raw("native.drop.root-title") : NativeFormat("FolderImportTitle", ("name", item.Name)),
                root ? item.Path : NativeFormat("FolderCopy", ("target", target!)),
                ProductPresentationText.Raw(root ? "native.drop.add-root" : "native.drop.import"), ProductPresentationText.Raw("native.common.cancel"), confirmed =>
                {
                    if (confirmed && !_disposed) _ = ExecuteAsync(item, target);
                    else ReleaseBusy();
                }, localizeMessage: false, localizeTitle: root);
            awaitingChoice = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { if (!_disposed) _feedback.Error(NativeFormat("InspectFailure", ("error", error.Message)), localizeMessage: false); }
        finally { if (!awaitingChoice) ReleaseBusy(); }
    }

    private async Task ExecuteModpackAsync(MinecraftModpackPreview pack, string root, bool includeOptional)
    {
        try
        {
            if (_disposed || !_imports.Commands.TryResolve(MinecraftModpackContract.Install, out var route)) return;
            var result = await _imports.Commands.Dispatch(route, new MinecraftModpackCommand(pack, root, includeOptional), cancellationToken: _lifetime.Token).Completion.ConfigureAwait(false);
            if (!_disposed && !result.IsSuccess && result.Error?.Code != XsrRuntimeErrors.Cancelled().Code)
                _feedback.Error(result.Error?.Message ?? ProductPresentationText.Raw("native.drop.modpack-incomplete"));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { if (!_disposed) _feedback.Error(NativeFormat("ModpackFailure", ("error", error.Message)), localizeMessage: false); }
        finally { ReleaseBusy(); }
    }

    private void ShowOtherJarUses(LocalJarArtifact jar, string root, string instanceId)
    {
        if (_disposed) return;
        _feedback.DismissDialog(_jarDialog);
        _jarDialog = _feedback.ShowDialog("jar.other", ProductPresentationText.Raw("native.drop.jar-purpose-title"),
            ProductPresentationText.Raw("native.drop.jar-purpose-message"),
            ProductPresentationText.Raw("native.drop.patch-core"), ProductPresentationText.Raw("native.common.cancel"), accepted =>
            {
                if (accepted) _ = ExecuteJarAsync(jar, root, instanceId, LocalJarAction.CorePatch);
                else ReleaseBusy();
            }, ProductPresentationText.Raw("native.drop.install-loader"), () =>
            {
                _feedback.DismissDialog(_jarDialog);
                _ = ExecuteJarAsync(jar, root, instanceId, LocalJarAction.Loader);
            });
    }

    private async Task ExecuteJarAsync(LocalJarArtifact jar, string root, string instanceId, LocalJarAction action)
    {
        try
        {
            if (_disposed || !_imports.Commands.TryResolve(MinecraftLocalJarContract.Import, out var route)) return;
            var result = await _imports.Commands.Dispatch(route, new MinecraftLocalJarCommand(jar, root, instanceId, action), cancellationToken: _lifetime.Token).Completion.ConfigureAwait(false);
            if (!_disposed && !result.IsSuccess && result.Error?.Code != XsrRuntimeErrors.Cancelled().Code)
                _feedback.Error(result.Error?.Message ?? ProductPresentationText.Raw("native.drop.jar-incomplete"));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { if (!_disposed) _feedback.Error(NativeFormat("JarFailure", ("error", error.Message)), localizeMessage: false); }
        finally { ReleaseBusy(); }
    }

    private async Task ExecuteAsync(MinecraftFolderInspection item, string? target)
    {
        try
        {
            XsrResult result;
            if (item.Kind == MinecraftFolderKind.GameRoot)
            {
                if (!_library.TryResolve(MinecraftLibraryRoutes.Directory, out var route)) return;
                result = await _library.Dispatch(route, new MinecraftLibraryDirectoryCommand(item.Path, true), cancellationToken: _lifetime.Token).Completion.ConfigureAwait(false);
            }
            else
            {
                if (!_imports.Commands.TryResolve(MinecraftFolderImportContract.Import, out var route)) return;
                result = await _imports.Commands.Dispatch(route, new MinecraftFolderImportCommand(item.Path, target!), cancellationToken: _lifetime.Token).Completion.ConfigureAwait(false);
            }
            if (!_disposed && !result.IsSuccess && result.Error?.Code != XsrRuntimeErrors.Cancelled().Code)
                _feedback.Error(result.Error?.Message ?? ProductPresentationText.Raw("native.drop.import-incomplete"));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { if (!_disposed) _feedback.Error(NativeFormat("ImportFailure", ("error", error.Message)), localizeMessage: false); }
        finally { ReleaseBusy(); }
    }

    private string NativeFormat(string name, params (string Key, string Value)[] data)
        => ProductPresentationText.Format("Native" + name, _platform.LocalizeDesktopText, data);

    public void Dispose()
    {
        _disposed = true;
        lock (_activationGate) _fileActivations.Clear();
        _platform.FilesDropped -= OnDrop;
        _lifetime.Cancel();
        // Pending queries/commands own cancellation registrations until completion.
    }
}
