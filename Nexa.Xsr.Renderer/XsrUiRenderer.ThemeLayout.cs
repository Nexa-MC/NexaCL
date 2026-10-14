namespace Nexa.Xsr.Renderer;

public sealed partial class XsrUiRenderer
{
    private readonly Dictionary<XsrUiEntityId, ThemeGridMeasurement> _themeGridMeasurements = [];

    private IEnumerable<XsrUiEntityId> OrderedChildren(XsrUiEntityId entity)
    {
        IReadOnlyList<XsrUiEntityId> children = _tree.Children(entity);
        if (_themeProgram is null) return children;
        // Modal/overlay ownership remains in authored order, above normal flow content.
        return children.Where(child => _tree.GetComponent<XsrUiOverlayLayer>(child) is null)
            .OrderBy(child => GetThemeStyle(child)?.Style.Order ?? 0)
            .Concat(children.Where(child => _tree.GetComponent<XsrUiOverlayLayer>(child) is not null));
    }

    private bool IsThemedOutOfFlow(XsrUiEntityId entity) =>
        GetThemeStyle(entity)?.Style.Position is XsrUiThemePosition.Absolute or XsrUiThemePosition.Overlay;

    private void LayoutThemedChild(XsrUiEntityId child, XsrUiRect slot)
    {
        XsrUiComputedStyle? style = GetThemeStyle(child)?.Style;
        if (style?.Position is XsrUiThemePosition.Absolute or XsrUiThemePosition.Overlay)
        {
            XsrUiSize desired = Measure(child, new(slot.Width, slot.Height));
            double width = style.Left is not null && style.Right is not null
                ? Math.Max(0, slot.Width - style.Left.Value - style.Right.Value) : desired.Width;
            double height = style.Top is not null && style.Bottom is not null
                ? Math.Max(0, slot.Height - style.Top.Value - style.Bottom.Value) : desired.Height;
            if (width <= 0) width = slot.Width;
            if (height <= 0) height = slot.Height;
            double left = style.Left ?? (style.Right is { } right ? slot.Width - right - width : 0);
            double top = style.Top ?? (style.Bottom is { } bottom ? slot.Height - bottom - height : 0);
            slot = new(slot.X + left, slot.Y + top, width, height);
        }
        Layout(child, slot);
    }

    private XsrUiSize MeasureThemeGrid(XsrUiEntityId entity, XsrUiGridLayout grid, XsrUiSize available)
    {
        XsrUiEntityId[] children = OrderedChildren(entity).Where(ParticipatesInStackFlow).ToArray();
        ThemeGridCell[] cells = PlaceGridCells(children, grid.Columns.Count);
        int rowCount = Math.Max(grid.Rows?.Count ?? 0, cells.Length == 0 ? 1 : cells.Max(cell => cell.Row + cell.RowSpan));
        XsrUiGridTrack[] rows = Enumerable.Range(0, rowCount)
            .Select(index => grid.Rows is { } declared && index < declared.Count ? declared[index] : new(XsrUiGridUnit.Auto)).ToArray();
        double[] columnNatural = new double[grid.Columns.Count];
        foreach (ThemeGridCell cell in cells)
        {
            XsrUiSize desired = Measure(cell.Entity, available);
            XsrUiThickness margin = EffectiveElement(cell.Entity)?.Margin ?? default;
            DistributeGridIntrinsic(columnNatural, grid.Columns, cell.Column, cell.ColumnSpan,
                desired.Width + margin.Horizontal, grid.ColumnGap);
        }
        double[] columns = ResolveGridTracks(grid.Columns, columnNatural, available.Width, grid.ColumnGap);
        double[] rowNatural = new double[rowCount];
        foreach (ThemeGridCell cell in cells)
        {
            double width = SpanExtent(columns, cell.Column, cell.ColumnSpan, grid.ColumnGap);
            XsrUiSize desired = Measure(cell.Entity, new(width, available.Height));
            XsrUiThickness margin = EffectiveElement(cell.Entity)?.Margin ?? default;
            DistributeGridIntrinsic(rowNatural, rows, cell.Row, cell.RowSpan,
                desired.Height + margin.Vertical, grid.RowGap);
        }
        double[] rowSizes = ResolveGridTracks(rows, rowNatural, available.Height, grid.RowGap);
        ThemeGridMeasurement measurement = new(grid, available, columns, rowSizes, cells);
        _themeGridMeasurements[entity] = measurement;
        XsrUiSize result = new(columns.Sum() + Math.Max(0, columns.Length - 1) * grid.ColumnGap,
            rowSizes.Sum() + Math.Max(0, rowSizes.Length - 1) * grid.RowGap);
        _stackContentSizes[entity.Index] = result;
        return result;
    }

    private void ArrangeThemeGrid(XsrUiEntityId entity, XsrUiGridLayout grid, XsrUiRect rect)
    {
        if (!_themeGridMeasurements.TryGetValue(entity, out ThemeGridMeasurement? measurement)
            || measurement.Grid != grid || measurement.Available.Width != rect.Width || measurement.Available.Height != rect.Height)
        {
            MeasureThemeGrid(entity, grid, new(rect.Width, rect.Height));
            measurement = _themeGridMeasurements[entity];
        }
        XsrUiScroll? scroll = _tree.GetComponent<XsrUiScroll>(entity);
        if (scroll is not null)
        {
            XsrUiSize extent = _stackContentSizes[entity.Index];
            scroll.MaximumOffsetX = Math.Max(0, extent.Width - rect.Width);
            scroll.MaximumOffsetY = Math.Max(0, extent.Height - rect.Height);
            scroll.OffsetX = Math.Clamp(scroll.OffsetX, 0, scroll.MaximumOffsetX);
            scroll.OffsetY = Math.Clamp(scroll.OffsetY, 0, scroll.MaximumOffsetY);
        }
        double x = rect.X - (scroll?.OffsetX ?? 0), y = rect.Y - (scroll?.OffsetY ?? 0);
        foreach (ThemeGridCell cell in measurement.Cells)
        {
            double cellX = x + measurement.Columns.Take(cell.Column).Sum() + cell.Column * grid.ColumnGap;
            double cellY = y + measurement.Rows.Take(cell.Row).Sum() + cell.Row * grid.RowGap;
            Layout(cell.Entity, new(cellX, cellY, SpanExtent(measurement.Columns, cell.Column, cell.ColumnSpan, grid.ColumnGap),
                SpanExtent(measurement.Rows, cell.Row, cell.RowSpan, grid.RowGap)));
        }
        foreach (XsrUiEntityId child in OrderedChildren(entity).Where(child => IsVisible(child)
            && (_tree.GetComponent<XsrUiOverlayLayer>(child) is not null || IsThemedOutOfFlow(child))))
            LayoutThemedChild(child, rect);
    }

    private void ValidateThemeGridPlacements(IReadOnlyDictionary<XsrUiEntityId, ThemeStyleEntry> candidate)
    {
        foreach (var (parent, entry) in candidate)
        {
            if (entry.Style.Grid is not { } grid) continue;
            XsrUiEntityId[] children = _tree.Children(parent)
                .Where(child => _tree.GetComponent<XsrUiOverlayLayer>(child) is null
                    && candidate[child].Style.Position is not (XsrUiThemePosition.Absolute or XsrUiThemePosition.Overlay))
                .OrderBy(child => candidate[child].Style.Order ?? 0).ToArray();
            _ = PlaceGridCells(children, grid.Columns.Count, candidate);
        }
    }

    private ThemeGridCell[] PlaceGridCells(XsrUiEntityId[] children, int columns,
        IReadOnlyDictionary<XsrUiEntityId, ThemeStyleEntry>? candidate = null)
    {
        List<ThemeGridCell> cells = [];
        HashSet<(int Row, int Column)> occupied = [];
        int cursor = 0;
        foreach (XsrUiEntityId child in children)
        {
            XsrUiComputedStyle? style = candidate is null ? GetThemeStyle(child)?.Style : candidate[child].Style;
            int columnSpan = style?.GridColumnSpan ?? 1, rowSpan = style?.GridRowSpan ?? 1;
            if (columnSpan > columns) throw new XsrUiThemeStyleException(child, "A grid column span exceeds the admitted parent tracks.");
            int column = style?.GridColumn ?? cursor % columns, row = style?.GridRow ?? cursor / columns;
            if (column >= columns) throw new XsrUiThemeStyleException(child, "A grid column falls outside the admitted parent tracks.");
            if (row + rowSpan > 4096) throw new XsrUiThemeStyleException(child, "A grid exceeds its bounded row count.");
            if (style?.GridColumn is not null || style?.GridRow is not null)
            {
                if (column + columnSpan > columns)
                    throw new XsrUiThemeStyleException(child, "A positioned grid span exceeds the admitted parent tracks.");
            }
            if (style?.GridColumn is null && style?.GridRow is null)
            {
                while (column + columnSpan > columns || Enumerable.Range(row, rowSpan).Any(r =>
                    Enumerable.Range(column, columnSpan).Any(c => occupied.Contains((r, c)))))
                {
                    cursor++; column = cursor % columns; row = cursor / columns;
                    if (row + rowSpan > 4096) throw new XsrUiThemeStyleException(child, "A grid exceeds its bounded row count.");
                }
            }
            if (row + rowSpan > 4096) throw new XsrUiThemeStyleException(child, "A grid exceeds its bounded row count.");
            cells.Add(new(child, column, row, columnSpan, rowSpan));
            for (int r = row; r < row + rowSpan; r++)
                for (int c = column; c < column + columnSpan; c++) occupied.Add((r, c));
            cursor = Math.Max(cursor, row * columns + column + columnSpan);
        }
        return cells.ToArray();
    }

    private static void DistributeGridIntrinsic(double[] natural, IReadOnlyList<XsrUiGridTrack> tracks,
        int start, int span, double desired, double gap)
    {
        double occupied = natural.Skip(start).Take(span).Sum() + Math.Max(0, span - 1) * gap;
        int[] flexible = Enumerable.Range(start, span).Where(index => tracks[index].Unit != XsrUiGridUnit.Pixels).ToArray();
        if (desired <= occupied || flexible.Length == 0) return;
        double extra = (desired - occupied) / flexible.Length;
        foreach (int index in flexible) natural[index] += extra;
    }

    private static double[] ResolveGridTracks(IReadOnlyList<XsrUiGridTrack> tracks, double[] natural,
        double available, double gap)
    {
        double[] sizes = new double[tracks.Count];
        double fractions = 0, used = Math.Max(0, tracks.Count - 1) * gap;
        for (int index = 0; index < tracks.Count; index++)
        {
            XsrUiGridTrack track = tracks[index];
            if (track.Unit == XsrUiGridUnit.Fraction) fractions += track.Value;
            else
            {
                sizes[index] = track.Unit == XsrUiGridUnit.Pixels ? track.Value : natural[index];
                used += sizes[index];
            }
        }
        double remaining = double.IsFinite(available) ? Math.Max(0, available - used) : natural.Sum();
        for (int index = 0; index < tracks.Count; index++)
            if (tracks[index].Unit == XsrUiGridUnit.Fraction)
                sizes[index] = fractions > 0 ? remaining * tracks[index].Value / fractions : natural[index];
        return sizes;
    }

    private static double SpanExtent(double[] tracks, int start, int span, double gap) =>
        tracks.Skip(start).Take(span).Sum() + Math.Max(0, span - 1) * gap;
    private sealed record ThemeGridMeasurement(XsrUiGridLayout Grid, XsrUiSize Available,
        double[] Columns, double[] Rows, ThemeGridCell[] Cells);
    private readonly record struct ThemeGridCell(XsrUiEntityId Entity, int Column, int Row, int ColumnSpan, int RowSpan);
}
