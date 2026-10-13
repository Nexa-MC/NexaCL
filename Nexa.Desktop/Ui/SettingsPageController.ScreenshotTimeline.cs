using Nexa.Services.Minecraft.Management;
using Nexa.Xsr.Renderer;

namespace Nexa.Desktop.Ui;

internal sealed partial class SettingsPageController
{
    private bool _screenshotTimeline;
    private DesktopScreenshotGalleryLayout? _screenshotLayout;
    private IReadOnlyList<InstanceContentEntry>? _screenshotLayoutEntries;
    private IReadOnlyList<IReadOnlyList<DesktopScreenshotGalleryLayout.Slot>>? _screenshotVisibleSlots;
    private double _screenshotLayoutWidth;
    private bool _screenshotLayoutTimeline;

    private static DateTime? ScreenshotTimestamp(long ticks) => ticks > 0 && ticks <= DateTime.MaxValue.Ticks ? new DateTime(ticks, DateTimeKind.Utc).ToLocalTime() : null;

    private XsrUiEntityId _screenshotWaterfall;
    private readonly List<XsrUiEntityId> _screenshotColumns = [];
    private sealed record ScreenshotCardView(XsrUiEntityId Root, XsrUiEntityId Icon, InstanceContentEntry Item);
    private readonly Dictionary<string, ScreenshotCardView> _screenshotCards = new(StringComparer.Ordinal);
    private void UpdateScreenshotWaterfall(InstanceContentSnapshot snapshot)
    {
        var viewport = _shell.Renderer.Viewport; var metric = ContentMetric("screenshot");
        double width = Math.Max(1, viewport.Width - (_shell.Tree.GetComponent<XsrUiElement>(_shell.Root)?.Padding.Horizontal ?? 0)
            - (_shell.Tree.GetComponent<XsrUiElement>(_shell.Navigation)?.Width ?? XsrUiShell.CollapsedRailWidth)
            - (_shell.Tree.GetComponent<XsrUiElement>(_shell.Content)?.Padding.Horizontal ?? 0) - metric.Margin.Top);
        double height = Math.Max(1, viewport.Height);
        if (_screenshotLayout is null || !ReferenceEquals(_screenshotLayoutEntries, snapshot.Entries) || Math.Abs(width - _screenshotLayoutWidth) > .01 || _screenshotLayoutTimeline != _screenshotTimeline)
        {
            _screenshotLayoutEntries = snapshot.Entries; _screenshotLayoutWidth = width; _screenshotLayoutTimeline = _screenshotTimeline;
            _screenshotLayout = DesktopScreenshotGalleryLayout.Create(snapshot.Entries, width, _screenshotTimeline); _screenshotVisibleSlots = null;
        }
        var layout = _screenshotLayout;
        double offset = Math.Clamp(_shell.Tree.GetComponent<XsrUiScroll>(_sections)!.OffsetY - metric.Margin.Right, 0, Math.Max(0, layout.Height - height));
        var visible = layout.SelectWindow(Math.Max(0, offset - metric.Margin.Bottom), offset + height + metric.Margin.Bottom, maxCards: 1023);
        var focused = _screenshotPreview.IsAssigned ? _screenshotPreviousFocus : _shell.Renderer.Focused;
        string? pinnedName = _screenshotCards.FirstOrDefault(pair => ContentContainsFocus(pair.Value.Root, focused)).Key;
        if (pinnedName is not null)
        {
            var projection = visible.Select(column => column.ToList()).ToArray();
            for (int column = 0; column < layout.ColumnCount; column++)
                if (layout.Columns[column].FirstOrDefault(slot => slot.Item?.Name == pinnedName) is { } pinned && !projection[column].Contains(pinned))
                { projection[column].Add(pinned); projection[column].Sort((left, right) => left.Top.CompareTo(right.Top)); }
            visible = projection;
        }
        foreach (var slot in visible.SelectMany(column => column))
            _ = slot.IsHeading ? DesktopMarkup.Dynamic("SettingsContentScreenshotHeading.d.nxml", new Dictionary<string, string> { ["heading"] = slot.HeadingDate is { } headingDate ? headingDate.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.CurrentCulture) : ContentTerm("unknownDate"), ["height"] = ContentNumber(slot.Height) })
                : DesktopMarkup.Dynamic("SettingsContentScreenshotCard.d.nxml", ScreenshotCardProjection(slot.Item!, slot.ImageHeight, slot.Height, _screenshotTimeline));
        if (_contentWindowStart != -1 && _shell.Tree.IsAlive(_screenshotWaterfall) && SameScreenshotWindow(_screenshotVisibleSlots, visible)) return;
        _screenshotVisibleSlots = visible; _contentWindowStart = 0; _screenshotIconEntities.Clear();
        if (!_shell.Tree.IsAlive(_screenshotWaterfall))
        {
            _screenshotCards.Clear(); _screenshotColumns.Clear();
            _screenshotWaterfall = AppendContent(_contentList, "SettingsContentScreenshotWaterfall.d.nxml", new() { ["height"] = ContentNumber(layout.Height) });
            if (!_shell.Tree.IsAlive(_contentEmpty)) _contentEmpty = AppendContent(_contentParking, "SettingsContentEmpty.d.nxml", new() { ["message"] = "" });
        }
        ReloadContent(_screenshotWaterfall, ("height", ContentNumber(layout.Height)));
        foreach (var entry in _screenshotCards.Values.Where(entry => _shell.Tree.IsAlive(entry.Root))) _dynamic.Move(entry.Root, _contentParking);
        foreach (var column in _screenshotColumns)
            foreach (var child in _shell.Tree.Children(column).ToArray()) RemoveContentView(child);
        while (_screenshotColumns.Count > layout.ColumnCount) { RemoveContentView(_screenshotColumns[^1]); _screenshotColumns.RemoveAt(_screenshotColumns.Count - 1); }
        while (_screenshotColumns.Count < layout.ColumnCount)
            _screenshotColumns.Add(AppendContent(_screenshotWaterfall, "SettingsContentScreenshotColumn.d.nxml", new() { ["key"] = "ScreenshotWaterfallColumn." + _screenshotColumns.Count, ["width"] = ContentNumber(layout.CardWidth), ["height"] = ContentNumber(layout.Height) }));
        if (snapshot.Entries.Count == 0) { ReloadContent(_contentEmpty, ("message", ContentTerm(_contentFilter.Length == 0 ? "empty" : "noMatches"))); _dynamic.Move(_contentEmpty, _contentList); _dynamic.Reorder(_contentList, [_contentEmpty, _screenshotWaterfall]); }
        else _dynamic.Move(_contentEmpty, _contentParking);
        var retained = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < layout.ColumnCount; index++)
        {
            var column = _screenshotColumns[index]; ReloadContent(column, ("width", ContentNumber(layout.CardWidth)), ("height", ContentNumber(layout.Height)));
            double cursor = 0;
            foreach (var slot in visible[index])
            {
                if (slot.Top > cursor) AppendContent(column, "SettingsContentSpacer.d.nxml", new() { ["key"] = "ScreenshotWaterfallBefore." + index + "." + slot.EntryIndex, ["height"] = ContentNumber(slot.Top - cursor) });
                if (slot.IsHeading)
                    AppendContent(column, "SettingsContentScreenshotHeading.d.nxml", new() { ["heading"] = slot.HeadingDate is { } date ? date.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.CurrentCulture) : ContentTerm("unknownDate"), ["height"] = ContentNumber(slot.Height) });
                else
                {
                    var item = slot.Item!; retained.Add(item.Name); var data = ScreenshotCardProjection(item, slot.ImageHeight, slot.Height, _screenshotTimeline);
                    if (_screenshotCards.TryGetValue(item.Name, out var retired) && !SameContentFileIdentity(retired.Item, item)) { RemoveContentView(retired.Root); _screenshotCards.Remove(item.Name); }
                    if (!_screenshotCards.TryGetValue(item.Name, out var view))
                    {
                        var card = AppendContent(_contentParking, "SettingsContentScreenshotCard.d.nxml", data); view = new(card, ContentNodes(card)["ManagementContentIcon"], item); _screenshotCards[item.Name] = view;

                    }
                    else ReloadContent(view.Root, data.Select(pair => (pair.Key, pair.Value)).ToArray());
                    var presented = ScreenshotWithCachedThumbnail(item);
                    bool sameIdentity = view.Item.Size == item.Size && view.Item.ModifiedUtcTicks == item.ModifiedUtcTicks;
                    if (presented.Icon is not null || !sameIdentity || _shell.Tree.GetComponent<XsrUiImage>(view.Icon)?.Raster is null) BindContentImage(view.Icon, presented);
                    view = view with { Item = item }; _screenshotCards[item.Name] = view;
                    var currentCard = view.Root; RegisterContentAction(currentCard, () => { if (_screenshotLayoutEntries?.Any(current => current.Name == item.Name && current.Size == item.Size && current.ModifiedUtcTicks == item.ModifiedUtcTicks) == true) OpenScreenshotPreview(item, currentCard); });
                    _screenshotIconEntities[item.Name] = view.Icon;
                    if (item.Icon is null && presented.Icon is { } ownedImage) _screenshotOwnedRealizedImages[view.Icon] = ownedImage;
                    _dynamic.Move(view.Root, column);
                }
                cursor = slot.Top + slot.Height;
            }
            if (layout.Height > cursor) AppendContent(column, "SettingsContentSpacer.d.nxml", new() { ["key"] = "ScreenshotWaterfallAfter." + index, ["height"] = ContentNumber(layout.Height - cursor) });
        }
        foreach (var old in _screenshotCards.Where(pair => !retained.Contains(pair.Key)).ToArray()) { RemoveContentView(old.Value.Root); _screenshotCards.Remove(old.Key); }
        _shell.Tree.MarkDirty(_contentList, XsrUiDirtyKinds.Layout | XsrUiDirtyKinds.Paint);
    }
    private Dictionary<string, string> ScreenshotCardProjection(InstanceContentEntry item, double imageHeight, double cardHeight, bool includeTimestamp) => new()
    {
        ["key"] = "ManagementScreenshot." + ContentFileKey(item.Name),
        ["name"] = item.Name,
        ["label"] = ContentFormat("ViewScreenshot", ("name", item.Name)),
        ["height"] = ContentNumber(cardHeight),
        ["imageHeight"] = ContentNumber(imageHeight),
        ["showTimestamp"] = ContentFlag(includeTimestamp),
        ["timestamp"] = ScreenshotTimestamp(item.ModifiedUtcTicks) is { } timestamp ? timestamp.ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture) : ContentTerm("unknownTime")
    };

    private static bool SameScreenshotWindow(IReadOnlyList<IReadOnlyList<DesktopScreenshotGalleryLayout.Slot>>? previous,
        IReadOnlyList<IReadOnlyList<DesktopScreenshotGalleryLayout.Slot>> current)
    {
        if (previous is null || previous.Count != current.Count) return false;
        for (int column = 0; column < current.Count; column++)
            if (!previous[column].SequenceEqual(current[column])) return false;
        return true;
    }




}
