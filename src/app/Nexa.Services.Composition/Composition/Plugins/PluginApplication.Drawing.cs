using System.Collections.Immutable;
using System.Security.Cryptography;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100;
using Nexa.Plugin.Sdk.Abi100.Data;

namespace Nexa.Services.Composition.Plugins;

public sealed record PluginDrawing(PluginIdentity Owner, QualifiedId Name, DrawingSurfaceRef Surface, double Width, double Height, ImmutableArray<DrawCommand> Commands);
public sealed partial class PluginApplication
{
    private static readonly System.Buffers.SearchValues<char> HexDigits = System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");
    private readonly Dictionary<EntityId, PluginDrawing> _drawings = [];
    private readonly Dictionary<Guid, (PluginIdentity Owner, BlobRef Reference, byte[] Bytes)> _blobs = [];
    private sealed class InputBinding(PluginIdentity owner, InputSubscription request, SubscriptionRef subscription)
    {
        internal readonly PluginIdentity Owner = owner;
        internal readonly InputSubscription Request = request;
        internal readonly SubscriptionRef Subscription = subscription;
        internal AbiValue? Latest;
        internal bool Running, Gap;
    }
    private readonly Dictionary<Guid, InputBinding> _drawingInputs = [];
    public ImmutableArray<PluginDrawing> Drawings() { lock (_gate) return _drawings.Values.ToImmutableArray(); }
    public byte[] CopyBlob(PluginIdentity owner, BlobRef reference)
    {
        lock (_gate)
        {
            if (!_blobs.TryGetValue(reference.Token, out var blob) || blob.Owner != owner || blob.Reference != reference) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
            return blob.Bytes.ToArray();
        }
    }
    public BlobRef AddBlob(PluginIdentity owner, byte[] bytes)
    {
        lock (_gate)
        {
            if (!Runtime.Active.Contains(owner)) throw new AbiHostException(AbiErrors.PLUGIN_STOPPED);
            if (_blobs.Count >= 128 || _blobs.Values.Sum(b => (long)b.Bytes.Length) + bytes.Length > 32 * 1024 * 1024) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            var reference = new BlobRef(Guid.NewGuid(), owner.LoadInstanceId, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            _blobs.Add(reference.Token, (owner, reference, bytes.ToArray())); return reference;
        }
    }
    private void ClearDrawing(string id)
    {
        foreach (var key in _drawings.Where(p => p.Value.Owner.PluginId == id).Select(p => p.Key).ToArray()) _drawings.Remove(key);
        foreach (var key in _drawingInputs.Where(p => p.Value.Owner.PluginId == id).Select(p => p.Key).ToArray()) _drawingInputs.Remove(key);
        foreach (var key in _blobs.Where(p => p.Value.Owner.PluginId == id).Select(p => p.Key).ToArray())
        { CryptographicOperations.ZeroMemory(_blobs[key].Bytes); _blobs.Remove(key); }
    }
    private byte[] FileBytes(HostInvocation c, FileRef reference)
    {
        if (reference.Scope != "package" || reference.LoadInstanceId != c.Identity.LoadInstanceId || !_files.TryGetValue(reference.Token, out var file) || file.Owner != c.Identity.LoadInstanceId ||
            !_assets.TryGetValue(file.Plugin, out var assets) || !assets.TryGetValue(file.Path, out var bytes)) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
        return bytes;
    }
    private IEnumerable<HostCapabilityRoute> DrawingRoutes => [
        HostRoutes.Typed<FileRef, FileStat>("Files", "Stat", async (c, v) =>
        {
            if (v.Scope != "package") return await Files.StatAsync(c.Identity, v, c.Options.Cancellation).ConfigureAwait(false);
            lock (_gate)
            {
                var bytes = FileBytes(c, v); var path = _files[v.Token].Path;
                return new FileStat(v, Path.GetFileName(path), false, bytes.Length, DateTimeOffset.UnixEpoch, new(Convert.ToHexString(SHA256.HashData(bytes))));
            }
        }),
        HostRoutes.Typed<FileRead, BlobRef>("Files", "Read", async (c, v) =>
        {
            if (v.File.Scope != "package") return AddBlob(c.Identity, await Files.ReadAsync(c.Identity, v, c.Options.Cancellation).ConfigureAwait(false));
            lock (_gate)
            {
                var bytes = FileBytes(c, v.File);
                if (v.Offset < 0 || v.Length is < 0 or > 4 * 1024 * 1024 || v.Offset > bytes.Length || v.Length > bytes.Length - v.Offset) throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
                if (_blobs.Count >= 128 || _blobs.Values.Sum(b => (long)b.Bytes.Length) + v.Length > 32 * 1024 * 1024) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                byte[] data = bytes.AsSpan((int)v.Offset, (int)v.Length).ToArray();
                var blob = new BlobRef(Guid.NewGuid(), c.Identity.LoadInstanceId, data.Length, Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());
                _blobs.Add(blob.Token, (c.Identity, blob, data)); return blob;
            }
        }),
        HostRoutes.Typed<DrawingSurfaceRequest, DrawingSurfaceRef>("UI", "CreateDrawingSurface", (c, v) =>
        {
            lock (_gate)
            {
                bool view = v.ViewScope == c.Identity.LoadInstanceId && _pages.Values.Any(p => p.Owner == c.Identity) || _dialogs.Values.Any(d => d.Owner == c.Identity && d.Reference.ViewScope == v.ViewScope);
                if (!view || !v.Id.Value.StartsWith(c.Identity.PluginId + ":", StringComparison.Ordinal)) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                if (!double.IsFinite(v.Width) || !double.IsFinite(v.Height) || v.Width is < 1 or > 2048 || v.Height is < 1 or > 2048 || _drawings.Count >= 8 || _drawings.Values.Sum(d => d.Width * d.Height) + v.Width * v.Height > 8 * 1024 * 1024)
                    throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                if (_drawings.Values.Any(d => d.Owner == c.Identity && d.Name == v.Id && d.Surface.ViewScope == v.ViewScope)) throw new AbiHostException(AbiErrors.CONFLICT);
                var reference = new DrawingSurfaceRef(new(Guid.NewGuid().ToString("N")), v.ViewScope, new(Guid.NewGuid().ToString("N")));
                _drawings.Add(reference.Id, new(c.Identity, v.Id, reference, v.Width, v.Height, [])); return ValueTask.FromResult(reference);
            }
        }),
        HostRoutes.Typed<DrawSubmission, Empty>("UI", "SubmitDrawCommands", (c, v) =>
        {
            lock (_gate)
            {
                if (!_drawings.TryGetValue(v.Surface.Id, out var surface) || surface.Owner != c.Identity || surface.Surface.ViewScope != v.Surface.ViewScope) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                if (surface.Surface.Revision != v.ExpectedRevision || v.Surface.Revision == v.ExpectedRevision) throw new AbiHostException(AbiErrors.CONFLICT);
                if (v.Commands.Length > 1024) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                foreach (var command in v.Commands)
                {
                    int count = command.Coordinates.Length;
                    bool valid = command.Kind switch
                    {
                        DrawCommandKind.Line => count is 4 or 5, DrawCommandKind.Rectangle or DrawCommandKind.Ellipse or DrawCommandKind.Clip or DrawCommandKind.Image => count == 4,
                        DrawCommandKind.Transform => count == 6, DrawCommandKind.Text => count == 3 && command.Text is { Length: <= 4096 },
                        DrawCommandKind.Path => count is >= 4 and <= 4096 && count % 2 == 0, _ => false
                    };
                    if (!valid || command.Coordinates.Any(n => !double.IsFinite(n) || Math.Abs(n) > 1000000) || command.Color.Length is not (7 or 9) || command.Color[0] != '#' || command.Color.AsSpan(1).ContainsAnyExcept(HexDigits))
                        throw new AbiHostException(AbiErrors.SCHEMA_INVALID);
                    if (command.Kind == DrawCommandKind.Image && (command.Image is not { } image || !_blobs.TryGetValue(image.Token, out var blob) || blob.Owner != c.Identity || blob.Reference != image)) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                }
                _drawings[v.Surface.Id] = surface with { Surface = v.Surface, Commands = v.Commands }; return ValueTask.FromResult(new Empty());
            }
        }),
        HostRoutes.Typed<InputSubscription, SubscriptionRef>("UI", "SubscribeInput", (c, v) =>
        {
            lock (_gate)
            {
                if (!_drawings.TryGetValue(v.Surface.Id, out var surface) || surface.Owner != c.Identity || surface.Surface.ViewScope != v.Surface.ViewScope || v.Handler == 0) throw new AbiHostException(AbiErrors.HANDLE_INVALID);
                if (v.Events.IsEmpty || v.Events.Length > 4 || v.Events.Distinct().Count() != v.Events.Length || _drawingInputs.Count >= 64) throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
                var reference = Runtime.Services.CreateHostSubscription(c, "nexa.ui/100", "nexa.ui.read", TextResult.Write(new("attached")), 64);
                _drawingInputs.Add(reference.Token, new(c.Identity, v, reference)); return ValueTask.FromResult(reference);
            }
        })
    ];
    public void DrawingInput(EntityId surface, InputEventKind kind, string phase, double x, double y, string? text)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || phase.Length > 64) return;
        bool truncated = text is { Length: > 4096 }; if (truncated) text = text![..4096];
        lock (_gate)
            foreach (var binding in _drawingInputs.Values.Where(b => b.Request.Surface.Id == surface && b.Request.Events.Contains(kind)).ToArray())
            {
                var value = DataCodec.Map(("kind", new AbiValue.Utf8String(kind.ToString())), ("phase", new AbiValue.Utf8String(phase)),
                    ("x", new AbiValue.Float64(x)), ("y", new AbiValue.Float64(y)), ("text", new AbiValue.Utf8String(text ?? "")));
                if (!Runtime.Services.PublishHostSubscription(binding.Owner, binding.Subscription, value)) { _drawingInputs.Remove(binding.Subscription.Token); continue; }
                binding.Gap |= binding.Latest is not null || truncated; binding.Latest = value;
                if (binding.Running) continue;
                binding.Running = true; _ = Task.Run(() => DispatchInput(binding));
            }
    }
    private async Task DispatchInput(InputBinding binding)
    {
        try
        {
            while (true)
            {
                AbiValue value;
                lock (_gate)
                {
                    if (!_drawingInputs.ContainsKey(binding.Subscription.Token) || binding.Latest is null) { binding.Running = false; return; }
                    value = DataCodec.Map(("event", binding.Latest), ("gap", new AbiValue.Bool(binding.Gap)));
                    binding.Latest = null; binding.Gap = false;
                }
                await Runtime.Services.DispatchHostCallback(binding.Owner, "nexa.ui.read", binding.Request.Handler, value,
                    new(Guid.NewGuid(), TimeSpan.FromSeconds(2), default)).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        { lock (_gate) { binding.Running = false; binding.Latest = null; binding.Gap = true; } }
    }
}
