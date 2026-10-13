using Nexa.Core.Media;
using Nexa.Services.Minecraft.Management;
using Nexa.Xsr;
using Nexa.Xsr.Renderer;
using Nexa.Xsr.Renderer.Nxml;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private static readonly XsrSemanticId ScreenshotPreviewAction = XsrSemanticId.Parse("ui.settings.screenshot-preview.action");
    internal Func<ReadOnlyMemory<byte>, Task>? CopyScreenshotAsync { get; set; }
    internal Func<string, long, long, Task>? ShareScreenshotAsync { get; set; }
    private readonly Dictionary<XsrUiEntityId, Action> _screenshotPreviewActions = [];
    private XsrUiEntityId _screenshotPreview, _screenshotPreviewCard, _screenshotPreviewImage, _screenshotPreviewStatus, _screenshotPreviousFocus;
    private XsrUiEntityId _screenshotCropButton, _screenshotPreviewSlot;
    private bool _screenshotCropInitialized;
    private string? _screenshotPreviewStatusOverride;
    private XsrUiEntityId _cropX, _cropY, _cropWidth, _cropHeight;
    private InstanceScreenshotQuery? _screenshotPreviewQuery;
    private InstanceContentEntry? _screenshotPreviewItem;
    private CancellationTokenSource? _screenshotPreviewStop;
    private Task<XsrResult<InstanceScreenshot>>? _screenshotPreviewRead;
    private Task? _screenshotOperation;
    private long _screenshotPreviewGeneration;
    private Guid _screenshotRemovalDialog;
    private bool _screenshotPreviousFocusVisible, _screenshotCropVisible;
    private XsrUiSize _screenshotPreviewViewport;

    private static bool SameScreenshot(InstanceContentEntry item, InstanceScreenshotQuery file) =>
        item.Name == file.Name && item.Size == file.ExpectedSize && item.ModifiedUtcTicks == file.ExpectedModifiedUtcTicks;

    private bool ScreenshotPreviewCurrent(InstanceScreenshotQuery file, long generation) =>
        Volatile.Read(ref _screenshotPreviewGeneration) == generation && _screenshotPreviewQuery == file
        && _instance == file.InstanceDirectory && _visible && _selected == "screenshots" && _shell.Stage.Navigation.Current == Page;

    private void OpenScreenshotPreview(InstanceContentEntry item, XsrUiEntityId source)
    {
        if (_instance is null || item.IsDirectory) return;
        CloseScreenshotPreview(restoreFocus: false);
        _screenshotPreviousFocus = _shell.Tree.IsAlive(_shell.Renderer.Focused) ? _shell.Renderer.Focused : source;
        _screenshotPreviousFocusVisible = _shell.Tree.IsAlive(_screenshotPreviousFocus) && _shell.Tree.GetComponent<XsrUiInput>(_screenshotPreviousFocus)?.IsFocusVisible == true;
        _screenshotPreviewItem = item;
        _screenshotPreviewQuery = item.Size is { } size ? new(_instance, item.Name, size, item.ModifiedUtcTicks) : null;
        _screenshotPreviewStop = new();
        _screenshotPreviewSlot = NxmlUiLoader.LoadDetached(DesktopMarkup.Load("SettingsContentScreenshotSlot.nxml"), _shell.Tree, _store);
        _screenshotPreview = AppendContent(_screenshotPreviewSlot, "SettingsContentScreenshotPreview.d.nxml", ScreenshotPreviewProjection(item));
        _screenshotPreviewActions[_screenshotPreview] = () => CloseScreenshotPreview();
        BuildScreenshotPreviewCard(); _shell.Stage.Show(_screenshotPreviewSlot, modal: true);


        FocusScreenshotPreviewClose();
        if (_screenshotPreviewQuery is { } query && _queries.TryResolve(InstanceScreenshotContract.Read, out var route))
        {
            _screenshotPreviewRead = _queries.QueryAsync<InstanceScreenshotQuery, InstanceScreenshot>(route, query,
                cancellationToken: _screenshotPreviewStop.Token).AsTask();
            ObserveTransfer(_screenshotPreviewRead);
        }
    }

    private Dictionary<string, string> ScreenshotPreviewProjection(InstanceContentEntry item)
    {
        _screenshotPreviewViewport = _shell.Renderer.Viewport;
        var metric = ContentMetric("screenshot");
        bool readable = _screenshotPreviewQuery is not null && item.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && _queries.TryResolve(InstanceScreenshotContract.Read, out _);
        string? dimensions = item.Icon is { } image ? ContentFormat("Dimensions", ("width", image.Width.ToString(System.Globalization.CultureInfo.CurrentCulture)), ("height", image.Height.ToString(System.Globalization.CultureInfo.CurrentCulture))) : item is { ImageWidth: > 0, ImageHeight: > 0 } ? ContentFormat("Dimensions", ("width", item.ImageWidth!.Value.ToString(System.Globalization.CultureInfo.CurrentCulture)), ("height", item.ImageHeight!.Value.ToString(System.Globalization.CultureInfo.CurrentCulture))) : null;
        double width = Math.Max(metric.MinWidth!.Value, Math.Min(metric.Width!.Value, _screenshotPreviewViewport.Width - metric.Padding.Left));
        return new()
        {
            ["name"] = item.Name,
            ["enabled"] = ContentFlag(_managementWrite is null),
            ["width"] = ContentNumber(width),
            ["maxHeight"] = ContentNumber(Math.Max(metric.Margin.Left, _screenshotPreviewViewport.Height - metric.Padding.Top)),
            ["imageHeight"] = ContentNumber(Math.Max(metric.MinHeight!.Value, Math.Min(_screenshotCropVisible ? metric.MaxHeight!.Value : metric.Height!.Value, _screenshotPreviewViewport.Height - (_screenshotCropVisible ? metric.Padding.Bottom : metric.Padding.Right)))),
            ["status"] = _screenshotPreviewStatusOverride ?? (dimensions is null ? ContentTerm("previewUnavailable") : ContentFormat("ImageStatus", ("dimensions", dimensions), ("size", FormatContentSize(item.Size ?? 0)))),
            ["actionOrientation"] = width >= metric.MaxWidth!.Value ? "Horizontal" : "Vertical",
            ["canCopy"] = ContentFlag(CopyScreenshotAsync is not null),
            ["canShare"] = ContentFlag(ShareScreenshotAsync is not null),
            ["readable"] = ContentFlag(readable),
            ["canFolder"] = ContentFlag(OpenManagementDirectory is not null && _management?.Pages.Any(page => page.Id == "screenshots" && page.Directory is not null) == true),
            ["canRemove"] = ContentFlag(_commands.TryResolve(InstanceManagementContract.RemoveContent, out _)),
            ["canCrop"] = ContentFlag(_commands.TryResolve(InstanceScreenshotContract.Crop, out _)),
            ["cropEnabled"] = ContentFlag(readable && item.Icon is not null),
            ["cropVisible"] = ContentFlag(_screenshotCropVisible),
            ["cropLabel"] = ContentTerm(_screenshotCropVisible ? "collapseCrop" : "cropCopy")
        };
    }
    private void BuildScreenshotPreviewCard()
    {
        if (_screenshotPreviewItem is not { } item || !_shell.Tree.IsAlive(_screenshotPreview)) return;
        ReloadContent(_screenshotPreview, ScreenshotPreviewProjection(item).Select(pair => (pair.Key, pair.Value)).ToArray());
        var nodes = ContentNodes(_screenshotPreview); _screenshotPreviewCard = nodes["ScreenshotPreview"]; _screenshotPreviewImage = nodes["ScreenshotPreviewImage"]; _screenshotPreviewStatus = nodes["ScreenshotPreviewStatus"]; _screenshotCropButton = nodes["ScreenshotPreview.Crop"];
        BindContentImage(_screenshotPreviewImage, item);
        void Bind(string key, Action action) { var entity = nodes["ScreenshotPreview." + key]; _screenshotPreviewActions[entity] = action; }
        Bind("Close", () => CloseScreenshotPreview()); Bind("Refresh", RefreshManagement); Bind("Copy", () => StartScreenshotOperation(copy: true)); Bind("Share", () => StartScreenshotOperation(copy: false));
        Bind("Folder", () => { var directory = _management?.Pages.FirstOrDefault(page => page.Id == "screenshots")?.Directory; if (directory is not null) OpenContentDirectory(directory); });
        Bind("Remove", () => { if (_screenshotPreviewItem is { } current) RemoveContent(current); });
        Bind("Crop", () => { _screenshotCropVisible = !_screenshotCropVisible; BuildScreenshotPreviewCard(); FocusScreenshotPreviewClose(); });
        _cropX = nodes["ScreenshotCropX"]; _cropY = nodes["ScreenshotCropY"]; _cropWidth = nodes["ScreenshotCropWidth"]; _cropHeight = nodes["ScreenshotCropHeight"];
        if (_screenshotCropVisible && !_screenshotCropInitialized && item.Icon is { } image)
        {
            _screenshotCropInitialized = true; _shell.Renderer.SetTextInputValue(_cropX, "0"); _shell.Renderer.SetTextInputValue(_cropY, "0");
            _shell.Renderer.SetTextInputValue(_cropWidth, image.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)); _shell.Renderer.SetTextInputValue(_cropHeight, image.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        Bind("SaveCrop", SaveScreenshotCrop);
    }
    private void SaveScreenshotCrop()
    {
        if (_screenshotPreviewQuery is not { } query || _instance != query.InstanceDirectory) return;
        int? Read(XsrUiEntityId entity) => int.TryParse(_shell.Tree.GetComponent<XsrUiTextInput>(entity)?.ReadDraft(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value) ? value : null;
        if (Read(_cropX) is not { } x || Read(_cropY) is not { } y || Read(_cropWidth) is not { } width || Read(_cropHeight) is not { } height) { _feedback.Error(ContentTerm("cropInteger")); return; }
        DispatchWorld(InstanceScreenshotContract.Crop, new InstanceScreenshotCropCommand(query, x, y, width, height));
    }

    private void FocusScreenshotPreviewClose()
    {
        var close = _screenshotPreviewActions.Keys.FirstOrDefault(entity => _shell.Tree.IsAlive(entity) && _shell.Tree.Name(entity) == "ScreenshotPreview.Close");
        if (close.IsAssigned) _shell.Renderer.Focus(close, _screenshotPreviousFocusVisible);
    }

    private void HandleScreenshotPreviewIntent(DesktopUiIntent intent)
    {
        if (_screenshotPreview.IsAssigned && _shell.Tree.IsAlive(intent.Source) && _visible && _selected == "screenshots"
            && _shell.Stage.Navigation.Current == Page && _screenshotPreviewActions.TryGetValue(intent.Source, out var action)
            && ContentActionCurrent(intent.Source)) action();
    }

    private void UpdateScreenshotPreview()
    {
        if (!_screenshotPreview.IsAssigned) return;
        if (_selected != "screenshots" || !_visible || _shell.Stage.Navigation.Current != Page
            || _screenshotPreviewQuery is { } current && (current.InstanceDirectory != _instance
                || _management?.Contents.FirstOrDefault(page => page.PageId == "screenshots")?.Entries.Any(item => SameScreenshot(item, current)) != true))
        { CloseScreenshotPreview(restoreFocus: false); return; }
        if (_screenshotPreviewRead is { IsCompleted: true } reading)
        {
            _screenshotPreviewRead = null;
            if (PendingQuery.Succeeded(reading) && _screenshotPreviewItem is { } item)
            {
                _screenshotPreviewItem = item with { Icon = reading.Result.Value!.Image };
                var image = reading.Result.Value.Image;
                _shell.Tree.GetComponent<XsrUiImage>(_screenshotPreviewImage)!.Raster = new(image,
                    [new(new(0, 0, image.Width, image.Height), new(0, 0, 1, 1))])
                { FitToBounds = true };
                SetScreenshotPreviewStatus(ContentFormat("ImageStatus", ("dimensions", ContentFormat("Dimensions", ("width", image.Width.ToString(System.Globalization.CultureInfo.CurrentCulture)), ("height", image.Height.ToString(System.Globalization.CultureInfo.CurrentCulture)))), ("size", FormatContentSize(item.Size ?? 0))), literal: true);
                ReloadContent(_screenshotPreview, ("cropEnabled", "true"));
                _shell.Tree.MarkDirty(_screenshotPreviewImage, XsrUiDirtyKinds.Paint);
                _shell.Tree.MarkDirty(_screenshotPreviewStatus, XsrUiDirtyKinds.Paint);
            }
            else if (_shell.Tree.IsAlive(_screenshotPreviewStatus))
            {
                SetScreenshotPreviewStatus(ContentTerm("copy.0287f08c20b7"), literal: false);
            }
        }
        if (_screenshotOperation is { IsCompleted: true } operation)
        {
            _screenshotOperation = null;
            if (operation.IsCompletedSuccessfully) _feedback.Info(ContentTerm("copy.b5faedfb762e"));
            else if (!operation.IsCanceled) { _ = operation.Exception; _feedback.Error(ContentTerm("copy.f4fd63d7464d")); }
        }
        if (_screenshotPreviewViewport != _shell.Renderer.Viewport) BuildScreenshotPreviewCard();
    }

    private void SetScreenshotPreviewStatus(string content, bool literal)
    {
        _screenshotPreviewStatusOverride = literal ? content : _shell.Renderer.LocalizeText(content); ReloadContent(_screenshotPreview, ("status", _screenshotPreviewStatusOverride));
    }

    private void StartScreenshotOperation(bool copy)
    {
        if (_screenshotOperation is not null || _managementWrite is not null || _screenshotPreviewQuery is not { } file
            || _screenshotPreviewStop is not { } stop || !_queries.TryResolve(InstanceScreenshotContract.Read, out var route)) return;
        long generation = _screenshotPreviewGeneration;
        var token = stop.Token;
        async Task Apply()
        {
            var result = await _queries.QueryAsync<InstanceScreenshotQuery, InstanceScreenshot>(route, file, cancellationToken: token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!ScreenshotPreviewCurrent(file, generation)) return;
            if (!result.IsSuccess) throw new IOException(result.Error?.Message);
            if (copy && CopyScreenshotAsync is { } copyAction) await copyAction(result.Value!.Image.Bytes).ConfigureAwait(false);
            else if (!copy && ShareScreenshotAsync is { } shareAction) await shareAction(result.Value!.Path, file.ExpectedSize, file.ExpectedModifiedUtcTicks).ConfigureAwait(false);
        }
        _screenshotOperation = Apply(); ObserveTransfer(_screenshotOperation);
    }



    private void CloseScreenshotPreview(bool restoreFocus = true)
    {
        Interlocked.Increment(ref _screenshotPreviewGeneration);
        ObserveRetiredInstanceToolRead(_screenshotPreviewRead); ObserveRetiredInstanceToolRead(_screenshotOperation);
        _screenshotPreviewStop?.Cancel(); _screenshotPreviewStop?.Dispose(); _screenshotPreviewStop = null;
        _screenshotPreviewRead = null; _screenshotOperation = null; _screenshotPreviewQuery = null; _screenshotPreviewItem = null;
        _screenshotCropVisible = false; _screenshotCropInitialized = false; _screenshotPreviewStatusOverride = null;
        if (_screenshotRemovalDialog != Guid.Empty) _feedback.DismissDialog(_screenshotRemovalDialog);
        _screenshotRemovalDialog = default;
        if (_screenshotPreview.IsAssigned && _shell.Tree.IsAlive(_screenshotPreview))
        { RemoveContentView(_screenshotPreview); _shell.Stage.Dismiss(_screenshotPreviewSlot); _shell.Tree.Destroy(_screenshotPreviewSlot); }
        bool hadPreview = _screenshotPreview.IsAssigned;
        _screenshotPreview = _screenshotPreviewSlot = default; _screenshotPreviewActions.Clear();
        if (restoreFocus && _screenshotPreviousFocus.IsAssigned && _shell.Tree.IsAlive(_screenshotPreviousFocus))
            _shell.Renderer.Focus(_screenshotPreviousFocus, _screenshotPreviousFocusVisible);
        else if (hadPreview && _shell.Renderer.Focused.IsAssigned && !_shell.Tree.IsAlive(_shell.Renderer.Focused)) _shell.Renderer.Focus(default);
        _screenshotPreviousFocus = default;
    }
}
