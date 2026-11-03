using System.Buffers.Binary;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private string _inspectionContext = "", _inspectionError = "";
    private ContentInspectionQuery? _inspectionRequest;
    private ContentInspectionSnapshot? _inspection;
    private Task<XsrResult<ContentInspectionSnapshot>>? _inspectionRead;
    private CancellationTokenSource? _inspectionStop;
    private XsrUiEntityId _inspectionSlot, _inspectionRoot, _inspectionPath;
    private readonly Dictionary<string, XsrUiEntityId> _inspectionRows = [];
    private readonly Stack<ContentInspectionQuery> _inspectionHistory = new();
    private void BuildInspectionPage(InstanceManagementSnapshot snapshot)
    {
        string context = _instance + "\0inspector\0";
        if (_inspectionContext != context)
        {
            RetireInspection(); _inspectionContext = context; _inspectionSlot = _sections;
            Inspect(new(snapshot.GameDirectory), false);
        }
        _inspectionSlot = _sections; BuildInspection();
    }
    private void BuildInspectionForContent(XsrUiEntityId slot, InstanceContentEntry item)
    {
        if (_selected is not ("schematics" or "shaderpacks" or "resourcepacks") || !_queries.TryResolve(ContentInspectionContract.Read, out _))
        { RetireInspection(); return; }
        string context = _instance + "\0" + _selected + "\0" + item.Name;
        if (_inspectionContext != context)
        {
            RetireInspection(); _inspectionContext = context;
            string? directory = _management?.Pages.FirstOrDefault(p => p.Id == _selected)?.Directory;
            if (directory is not null) Inspect(new(Path.Combine(directory, item.Name)), false);
        }
        _inspectionSlot = slot; BuildInspection();
    }
    private void RetireInspection()
    {
        _inspectionStop?.Cancel(); _inspectionStop?.Dispose(); _inspectionStop = null;
        ObserveRetiredInstanceToolRead(_inspectionRead); _inspectionRead = null;
        _inspection = null; _inspectionRequest = null; _inspectionContext = _inspectionError = ""; _inspectionHistory.Clear();
        RetireWorldRows(_inspectionRows, []); RemoveWorldView(ref _inspectionRoot);
    }
    private void Inspect(ContentInspectionQuery query, bool push = true)
    {
        if (push && _inspectionRequest is { } previous) _inspectionHistory.Push(previous);
        _inspectionStop?.Cancel(); _inspectionStop?.Dispose(); ObserveRetiredInstanceToolRead(_inspectionRead);
        _inspectionStop = new(); _inspectionRequest = query; _inspection = null; _inspectionError = "";
        if (_queries.TryResolve(ContentInspectionContract.Read, out var route))
        {
            _inspectionRead = _queries.QueryAsync<ContentInspectionQuery, ContentInspectionSnapshot>(route, query, cancellationToken: _inspectionStop.Token).AsTask();
            WakeOnPlatformCompletion(_inspectionRead);
        }
        else _inspectionError = WorldTerm("inspect.unavailable");
        BuildInspection();
    }
    private void UpdateInspection()
    {
        if (_inspectionRequest is null) return;
        if (!_visible || (_contentDetail is null && _selected != "inspector") || !_inspectionContext.StartsWith(_instance + "\0" + _selected + "\0", StringComparison.Ordinal)) { RetireInspection(); return; }
        if (_inspectionRead is not { IsCompleted: true } read) return;
        _inspectionRead = null;
        if (PendingQuery.Succeeded(read)) _inspection = read.Result.Value;
        else _inspectionError = read.IsCompletedSuccessfully ? read.Result.Error?.Message ?? WorldTerm("inspect.failed") : WorldTerm("inspect.failed");
        BuildInspection();
    }
    private void BuildInspection()
    {
        if (!_shell.Tree.IsAlive(_inspectionSlot)) return;
        var request = _inspectionRequest; var value = _inspection;
        var nodes = WorldView(_inspectionSlot, ref _inspectionRoot, "Inspection", new()
        {
            ["summary"] = value is null ? _inspectionError.Length > 0 ? _inspectionError : WorldTerm("inspect.reading") : value.Format + " · " + value.Summary,
            ["text"] = value?.Text ?? "",
            ["path"] = request?.Path ?? "",
            ["location"] = (request?.ArchiveEntry ?? "") + " / " + (request?.Node ?? ""),
            ["image"] = Flag(value?.Image.Length > 0),
            ["back"] = Flag(_inspectionHistory.Count > 0),
            ["previous"] = Flag(request?.Offset > 0),
            ["next"] = Flag(value is not null && request is not null && (long)request.Offset + 64 < value.Total),
            ["count"] = value is null ? "" : $"{request!.Offset + 1}–{Math.Min((long)request.Offset + 64, value.Total)} / {value.Total}"
        });
        _inspectionPath = nodes["Inspection.Path"]; _shell.Renderer.SetTextInputValue(_inspectionPath, request?.Path ?? "");
        if (value?.Image is { Length: >= 24 } image && Nexa.Core.Media.PngImage.TryCreatePreview(image) is { } parsedImage)
        {
            int width = BinaryPrimitives.ReadInt32BigEndian(image.AsSpan(16)), height = BinaryPrimitives.ReadInt32BigEndian(image.AsSpan(20));
            _shell.Tree.SetComponent(nodes["Inspection.Image"], new XsrUiRasterImage(parsedImage, []) { FitToBounds = true, AspectRatio = (double)width / height });
        }
        BindWorldAction(nodes, "Inspection.Open", () => Inspect(new(_shell.Tree.GetComponent<XsrUiTextInput>(_inspectionPath)!.ReadDraft().Trim())));
        BindWorldAction(nodes, "Inspection.Back", () => { if (_inspectionHistory.TryPop(out var previous)) Inspect(previous, false); });
        BindWorldAction(nodes, "Inspection.Previous", () => { if (_inspectionRequest is { } q) Inspect(q with { Offset = Math.Max(0, q.Offset - 64) }, false); });
        BindWorldAction(nodes, "Inspection.Next", () => { if (_inspectionRequest is { } q && _inspection is { } v && (long)q.Offset + 64 < v.Total) Inspect(q with { Offset = q.Offset + 64 }, false); });
        var items = value?.Items ?? []; RetireWorldRows(_inspectionRows, items.Select(i => i.Id));
        foreach (var item in items)
        {
            string key = "Inspection.Item." + InstanceToolIdentity(item.Id);
            var row = WorldRow(nodes["Inspection.Rows"], _inspectionRows, item.Id, "InspectionRow", new()
            { ["key"] = key, ["open"] = key + ".Open", ["name"] = item.Name, ["type"] = item.Type, ["value"] = item.Value, ["expand"] = Flag(item.Expandable) });
            BindWorldAction(row, key + ".Open", () =>
            {
                if (_inspectionRequest is not { } q || _inspection is not { } v) return;
                Inspect(v.Format switch { "NBT" => q with { Node = item.Id, Offset = 0 }, "ZIP" => q with { ArchiveEntry = item.Id, Node = "", Offset = 0 }, _ => new(item.Id) });
            });
        }
        _dynamic.Reorder(nodes["Inspection.Rows"], items.Select(i => _inspectionRows[i.Id]).ToArray());
    }
}
