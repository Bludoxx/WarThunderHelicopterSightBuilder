namespace HeliSightBuilder.Native;

public static class EditorStateRules
{
    public const double MaxCoordinate = 1_000_000;
    public const double DefaultScale = 100;
    public const double DefaultZoom = 100;
    public const double DefaultGrid = 1;
    public const double DefaultNudge = .5;
    public const double MinimumScale = 1;
    public const double MinimumZoom = .01;
    public const double MaximumZoom = 1_000_000;
    public const double MinimumGrid = .01;
    public const double MinimumNudge = .01;
    public const double GameScaleAtOneHundredPercent = 1;
    public const double RelativeSizeReferenceEnvelope = 1_000;
    private const double LegacyGameScale = 70;

    private static readonly HashSet<string> Shapes =
        ["Dot", "Circle", "Cross", "Box", "T Sight", "Custom"];
    private static readonly HashSet<string> Tools =
        ["Select", "Pan", "Line", "Circle", "Box", "Dot"];
    private static readonly HashSet<string> Parts =
        ["Crosshair", "Brackets", "Chevron", "Pipper", "Rocket Ladder", "Side Posts"];
    private static readonly HashSet<string> Colors =
        ["White", "Green", "Amber", "Red", "Cyan"];

    public static bool IsValidItem(SightItem item) =>
        IsCoordinate(item.X1) && IsCoordinate(item.Y1) &&
        IsCoordinate(item.X2) && IsCoordinate(item.Y2);

    public static AppState Sanitize(AppState state, out bool recovered)
    {
        recovered = false;

        var validItems = state.Items is { Count: > 0 } && state.Items.All(IsValidItem);
        var items = validItems ? state.Items : new List<SightItem> { new(SightItemKind.Ellipse, 0, 0, 2.1, 2.1) };
        recovered |= !validItems;
        if (state.ScaleCalibrationVersion is > 0 and < 2)
        {
            items = items.Select(item => item.Kind == SightItemKind.FilledEllipse
                ? item with { X2 = item.X2 * LegacyGameScale, Y2 = item.Y2 * LegacyGameScale }
                : item).ToList();
            recovered = true;
        }

        var shape = ValidChoice(state.Shape, Shapes, "Dot", ref recovered);
        var tool = ValidChoice(state.Tool, Tools, "Select", ref recovered);
        var part = ValidChoice(state.Part, Parts, "Crosshair", ref recovered);
        var color = ValidChoice(state.Color, Colors, "White", ref recovered);

        var size = Range(state.Size, .01, MaxCoordinate, 4.2, ref recovered);
        var gap = Range(state.Gap, 0, MaxCoordinate, 1, ref recovered);
        var scale = Range(state.Scale, MinimumScale, MaxCoordinate, DefaultScale, ref recovered);
        if (state.ScaleCalibrationVersion == 0 && scale >= 1_000)
        {
            // Older builds needed values near 7000% to reach normal in-game size.
            scale /= LegacyGameScale;
            recovered = true;
        }
        if (state.ScaleCalibrationVersion is > 0 and < 3)
        {
            var extent = GeometryExtent(items);
            if (extent > .000001)
                scale = scale * extent / RelativeSizeReferenceEnvelope;
            recovered = true;
        }
        if (state.ScaleCalibrationVersion < 4 && shape == "Custom" && validItems)
        {
            var extent = GeometryExtent(items);
            if (extent > .000001)
                scale = scale * RelativeSizeReferenceEnvelope / extent;
            recovered = true;
        }
        var originX = Range(state.OriginX, -MaxCoordinate, MaxCoordinate, 0, ref recovered);
        var originY = Range(state.OriginY, -MaxCoordinate, MaxCoordinate, 0, ref recovered);
        var zoom = Range(state.Zoom, MinimumZoom, MaximumZoom, DefaultZoom, ref recovered);
        var grid = Range(state.Grid, MinimumGrid, 100, DefaultGrid, ref recovered);
        var nudge = Range(state.Nudge, MinimumNudge, 100, DefaultNudge, ref recovered);
        var lineWidth = Range(state.LineWidth, .1, 50, 2, ref recovered);
        var rangeX = Range(state.RangeX, -MaxCoordinate, MaxCoordinate, 0, ref recovered);
        var rangeY = Range(state.RangeY, -MaxCoordinate, MaxCoordinate, 5, ref recovered);
        var rangeFontSize = Range(state.RangeFontSize, .25, 72, 10, ref recovered);
        var selected = Math.Clamp(state.Selected, -1, items.Count - 1);
        recovered |= selected != state.Selected;

        var output = string.IsNullOrWhiteSpace(state.Output)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "HeliSightOutput")
            : state.Output;
        recovered |= output != state.Output;

        return new AppState(shape, size, gap, scale, color, originX, originY,
            [.. items], selected, output, tool, part, zoom, state.Snap, grid, nudge,
            state.GameContent, 4, state.DarkMode,
            string.IsNullOrWhiteSpace(state.SizeProfile) ? "Custom" : state.SizeProfile,
            lineWidth, state.ShowLiveRange, rangeX, rangeY, rangeFontSize);
    }

    public static double SafeCoordinate(double value) =>
        IsCoordinate(value) ? value : 0;

    public static double OutputScale(double percent) =>
        percent / 100 * GameScaleAtOneHundredPercent;

    private static bool IsCoordinate(double value) =>
        double.IsFinite(value) && Math.Abs(value) <= MaxCoordinate;

    private static double GeometryExtent(IEnumerable<SightItem> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return 0;
        var left = list.Min(item => item.Bounds.Left);
        var right = list.Max(item => item.Bounds.Right);
        var top = list.Min(item => item.Bounds.Top);
        var bottom = list.Max(item => item.Bounds.Bottom);
        return Math.Max(right - left, bottom - top);
    }

    private static double Range(double value, double minimum, double maximum, double fallback, ref bool recovered)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            recovered = true;
            return fallback;
        }
        return value;
    }

    private static string ValidChoice(string? value, HashSet<string> choices, string fallback, ref bool recovered)
    {
        if (value is not null && choices.Contains(value)) return value;
        recovered = true;
        return fallback;
    }
}
