using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;
using PluginSidecar.Packages;
using PluginSidecar.WasmRuntime;

namespace Nexa.Services.Composition.Plugins;

public sealed record PluginStatus(string Id, string Version, string State, string? Error, ImmutableArray<string> Permissions);
public sealed record PluginPage(PluginIdentity Owner, UIPage Page, string Title, string Source, AbiValue Model, Revision Revision);
public sealed record PluginPresentation(ImmutableArray<PluginStatus> Plugins, ImmutableArray<PluginPage> Pages);

/// <summary>Host-owned admission, lifecycle and presentation data. No renderer or guest objects cross this port.</summary>
public sealed partial class PluginApplication : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _loads = new(1, 1);
    private readonly Dictionary<string, PluginStatus> _status = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, byte[]>> _assets = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, (Guid Owner, string Plugin, string Path)> _files = [];
    private readonly Dictionary<QualifiedId, (PluginIdentity Owner, UIPage Page, string Source, RegistrationRef Registration)> _pages = [];
    private readonly Dictionary<QualifiedId, StateSnapshot> _models = [];
    private readonly Dictionary<ulong, PluginIdentity> _navigation = [];
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<IWasmPluginMachine>> _machine;
    private readonly Action<PluginIdentity, UINotification> _notify;
    private readonly Action<PluginIdentity, QualifiedId> _navigate;
    private readonly Action<string> _validatePage;
    private readonly Action<string> _validateComponent;
    private bool _closed;
    public PluginHostRuntime Runtime { get; }

    public PluginApplication(string storage, Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<IWasmPluginMachine>> machine,
        IEnumerable<HostCapabilityRoute> business, IActionConfirmation confirmation,
        Action<PluginIdentity, UINotification> notify, Action<PluginIdentity, QualifiedId> navigate, Action<string> validatePage, Action<string>? validateComponent = null)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(storage);
        else Directory.CreateDirectory(storage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _machine = machine; _notify = notify; _navigate = navigate; _validatePage = validatePage; _validateComponent = validateComponent ?? validatePage;
        Runtime = new(storage, d => Abi100Schemas.Validate(d.Schema, d.Body), Abi100Schemas.Validate,
            Routes.Concat(AdvancedUiRoutes).Concat(DrawingRoutes).Concat(business), new() { ActionConfirmation = confirmation });
    }

    public PluginPresentation Snapshot()
    {
        var definitions = Runtime.Definitions.Entries;
        lock (_gate)
        {
            var pages = _pages.Values.Where(p => definitions.Any(d => d.Registration.Handle == p.Registration.Handle && d.Owner == p.Owner))
                .Select(p => new PluginPage(p.Owner, p.Page, definitions.Where(d => _navigation.GetValueOrDefault(d.Registration.Handle) == p.Owner)
                    .OrderBy(d => d.Definition.Id.Value, StringComparer.Ordinal).Select(d => UINavigation.Read(d.Definition.Body)).FirstOrDefault(n => n.Page == p.Page.Id)?.Title.MessageKey ?? p.Page.Id.Value, p.Source, _models.GetValueOrDefault(p.Page.Id)?.Value ?? new AbiValue.Null(),
                    _models.GetValueOrDefault(p.Page.Id)?.Revision ?? new Revision("initial"))).ToImmutableArray();
            return new(_status.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToImmutableArray(), pages);
        }
    }

    /// <summary>Only verified Market bytes or an explicitly approved developer package may enter here.</summary>
    public async Task<PluginLoadReport> LoadAsync(VerifiedPluginPackage package, ImmutableArray<string> grants, CancellationToken token)
    {
        await _loads.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            var manifest = package.Manifest;
            if (Runtime.Active.Any(p => p.PluginId == manifest.PluginId)) throw new InvalidOperationException("Stop the active plugin before replacing it.");
            ClearAssets(manifest.PluginId);
            var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            try
            {
                foreach (var item in manifest.Payloads.Where(p => p.Path != manifest.WebAssemblyEntryPath))
                {
                    if (item.Size > (item.Path.EndsWith(".nxml", StringComparison.Ordinal) ? 256 * 1024 : 4 * 1024 * 1024) || assets.Count >= 32 || assets.Values.Sum(b => (long)b.Length) + item.Size > 16 * 1024 * 1024) throw new InvalidDataException("Plugin UI asset quota exceeded.");
                    assets.Add(item.Path, package.CopyPayload(item.Path));
                }
                lock (_gate)
                {
                    _assets.Add(manifest.PluginId, assets);
                    _status[manifest.PluginId] = new(manifest.PluginId, manifest.PluginVersion, "Loading", null, grants);
                }
                var result = (await Runtime.LoadAsync([package.CreateLoadSource(grants, _machine)], token).ConfigureAwait(false)).Single();
                lock (_gate) _status[manifest.PluginId] = new(manifest.PluginId, manifest.PluginVersion, result.State.ToString(), result.Error?.Code, grants);
                if (result.State != LoadState.Active) ClearAssets(manifest.PluginId);
                Reconcile();
                return result;
            }
            catch
            {
                lock (_gate) _status[manifest.PluginId] = new(manifest.PluginId, manifest.PluginVersion, "Failed", "plugin.load.failed", grants);
                ClearAssets(manifest.PluginId);
                foreach (var bytes in assets.Values) CryptographicOperations.ZeroMemory(bytes);
                throw;
            }
        }
        finally { _loads.Release(); }
    }

    public async Task StopAsync(string id, CancellationToken token = default)
    {
        await _loads.WaitAsync(token).ConfigureAwait(false);
        try { await Runtime.StopAsync(id, token).ConfigureAwait(false); Reconcile(); }
        finally { _loads.Release(); }
    }
    private void Reconcile()
    {
        var active = Runtime.Active.Select(p => p.PluginId).ToHashSet(StringComparer.Ordinal);
        string[] retired;
        lock (_gate) retired = _assets.Keys.Where(id => !active.Contains(id)).ToArray();
        foreach (var id in retired)
        {
            ClearAssets(id);
            lock (_gate) if (_status.TryGetValue(id, out var status) && status.State == "Active") _status[id] = status with { State = "Stopped" };
        }
    }
    private void ClearAssets(string id)
    {
        lock (_gate)
        {
            if (_assets.Remove(id, out var assets)) foreach (var bytes in assets.Values) CryptographicOperations.ZeroMemory(bytes);
            ClearAdvancedUi(id); ClearDrawing(id);
            foreach (var key in _navigation.Where(p => p.Value.PluginId == id).Select(p => p.Key).ToArray()) _navigation.Remove(key);
            foreach (var key in _files.Where(p => p.Value.Plugin == id).Select(p => p.Key).ToArray()) _files.Remove(key);
            foreach (var key in _pages.Where(p => p.Value.Owner.PluginId == id).Select(p => p.Key).ToArray()) { _pages.Remove(key); _models.Remove(key); }
        }
    }
    private IEnumerable<HostCapabilityRoute> Routes => [
        HostRoutes.Typed<PackageFileRequest, FileRef>("Files", "OpenPackageFile", (c, v) =>
        {
            lock (_gate)
            {
                if (!_assets.TryGetValue(c.Identity.PluginId, out var assets) || !assets.ContainsKey(v.Path)) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                var prior = _files.FirstOrDefault(p => p.Value.Owner == c.Identity.LoadInstanceId && p.Value.Path == v.Path);
                if (prior.Key != Guid.Empty) return ValueTask.FromResult(new FileRef(prior.Key, c.Identity.LoadInstanceId, "package"));
                if (_files.Count >= 1024) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                Guid id = Guid.NewGuid(); _files.Add(id, (c.Identity.LoadInstanceId, c.Identity.PluginId, v.Path));
                return ValueTask.FromResult(new FileRef(id, c.Identity.LoadInstanceId, "package"));
            }
        }),
        HostRoutes.Typed<UIPage, RegistrationRef>("UI", "RegisterPage", (c, v) =>
        {
            string source;
            lock (_gate)
            {
                if (v.EntryFile.LoadInstanceId != c.Identity.LoadInstanceId || v.EntryFile.Scope != "package" ||
                    !_files.TryGetValue(v.EntryFile.Token, out var file) || file.Owner != c.Identity.LoadInstanceId ||
                    !_assets.TryGetValue(file.Plugin, out var assets) || !assets.TryGetValue(file.Path, out var bytes)) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                source = new UTF8Encoding(false, true).GetString(bytes);
            }
            _validatePage(source);
            var reference = Runtime.Definitions.Stage(c, [new(v.Id, new("nexa:ui/pages"), Abi100Schemas.UIPage, UIPage.Write(v), [])]).Single();
            lock (_gate) _pages.Add(v.Id, (c.Identity, v, source, reference));
            return ValueTask.FromResult(reference);
        }),
        HostRoutes.Typed<UINavigation, RegistrationRef>("UI", "RegisterNavigation", (c, v) =>
        {
            lock (_gate)
                if (!_pages.TryGetValue(v.Page, out var page) || page.Owner != c.Identity) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            if (v.Icon is not null || !v.Title.Arguments.IsEmpty || v.Title.MessageKey.Length > 256) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            var reference = Runtime.Definitions.Stage(c, [new(v.Id, new("nexa:ui/navigation"), Abi100Schemas.UINavigation, UINavigation.Write(v), [new(v.Page, true)])]).Single();
            lock (_gate) _navigation.Add(reference.Handle, c.Identity);
            return ValueTask.FromResult(reference);
        }),
        HostRoutes.Typed<UIModelUpdate, StateSnapshot>("UI", "PublishModel", PublishModel),
        HostRoutes.Typed<UIModelUpdate, StateSnapshot>("UI", "UpdateModel", PublishModel),
        HostRoutes.Typed<UINavigateRequest, Empty>("UI", "Navigate", (c, v) =>
        {
            var page = Snapshot().Pages.FirstOrDefault(p => p.Owner == c.Identity && p.Page.Id == v.Page) ?? throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            if (v.Arguments is not AbiValue.Null) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            _navigate(c.Identity, page.Page.Id); return ValueTask.FromResult(new Empty());
        }),
        HostRoutes.Typed<UINotification, Empty>("UI", "Notify", Notify),
        HostRoutes.Typed<UINotification, Empty>("Platform", "Notify", Notify)
    ];
    private ValueTask<Empty> Notify(HostInvocation c, UINotification value)
    {
        if (!value.Title.Arguments.IsEmpty || !value.Body.Arguments.IsEmpty || value.Title.MessageKey.Length > 256 || value.Body.MessageKey.Length > 4096)
            throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
        if (value.Action is not null) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
        _notify(c.Identity, value); return ValueTask.FromResult(new Empty());
    }
    private ValueTask<StateSnapshot> PublishModel(HostInvocation c, UIModelUpdate value)
    {
        lock (_gate)
        {
            if (value.Value is not AbiValue.Map model || model.Value.Values.Any(v => v is not (AbiValue.Utf8String or AbiValue.Bool or AbiValue.Int64 or AbiValue.UInt64 or AbiValue.Float64 or AbiValue.Null)))
                throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            if (_dialogs.TryGetValue(value.View, out var dialog))
            {
                if (dialog.Owner != c.Identity) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                if (dialog.Page.Page.Schema != value.Schema || !Abi100Schemas.Validate(value.Schema, value.Value)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
                if (value.ExpectedRevision is { } dialogExpected && dialog.Page.Revision != dialogExpected) throw new AbiHostException(AbiErrors.CONFLICT);
                var updated = new StateSnapshot(value.Value, value.Schema, new(Guid.NewGuid().ToString("N")), DateTimeOffset.UtcNow, null);
                _dialogs[value.View] = dialog with { Page = dialog.Page with { Model = value.Value, Revision = updated.Revision } };
                return ValueTask.FromResult(updated);
            }
            var id = new QualifiedId(value.View.Value);
            if (!_pages.TryGetValue(id, out var page) || page.Owner != c.Identity) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            if (page.Page.Schema != value.Schema || !Abi100Schemas.Validate(value.Schema, value.Value)) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
            if (value.ExpectedRevision is { } expected && _models.GetValueOrDefault(id)?.Revision != expected) throw new AbiHostException(AbiErrors.CONFLICT);
            var snapshot = new StateSnapshot(value.Value, value.Schema, new(Guid.NewGuid().ToString("N")), DateTimeOffset.UtcNow, null);
            _models[id] = snapshot; return ValueTask.FromResult(snapshot);
        }
    }
    public async ValueTask DisposeAsync()
    {
        await _loads.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_closed) return; _closed = true;
            try { await Runtime.DisposeAsync().ConfigureAwait(false); }
            finally { string[] ids; lock (_gate) ids = _assets.Keys.ToArray(); foreach (var id in ids) ClearAssets(id); }
        }
        finally { _loads.Release(); }
    }
}
