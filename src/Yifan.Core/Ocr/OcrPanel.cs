using Yifan.Core.Layout;

namespace Yifan.Core.Ocr;

public static class OcrPanel
{
    public const double Margin = 16;
    public const double DefaultWidth = 400;
    public const double EnlargedWidth = 560;
    public const double MinWidth = 280;
    public const double MinHeight = 200;
    public const int FontDefault = 13;
    public const int FontMin = 12;
    public const int FontMax = 22;
    public const int FontStep = 1;

    public static readonly string[] ResizeEdges = ["n", "s", "e", "w", "ne", "nw", "se", "sw"];

    public static bool IsResizeEdge(string? value) => value is not null && ResizeEdges.Contains(value);

    public static string Phase(bool loading) => loading ? "loading" : "ready";

    public static OcrChrome Chrome(string phase)
    {
        var on = phase == "ready";
        return new OcrChrome(on, on, on, on);
    }

    public static OcrPanelLayout Create(ViewportSize viewport, double contentHeight, OcrInsets? insets = null)
    {
        var limits = Resolve(viewport, insets);
        var natural = contentHeight > 0 ? contentHeight : Math.Min(320, limits.MaxH);
        var box = AnchorBottomRight(DefaultWidth, Math.Min(natural, limits.MaxH), limits, enforceMin: false);
        return new OcrPanelLayout(box, box, false, FontDefault, false);
    }

    public static double DefaultWidthFor(ViewportSize viewport, OcrInsets? insets = null)
    {
        var limits = Resolve(viewport, insets);
        return Clamp(DefaultWidth, limits.MinW, limits.MaxW);
    }

    public static OcrPanelLayout Drag(OcrPanelLayout layout, double dx, double dy, ViewportSize viewport, OcrInsets? insets = null)
    {
        var limits = Resolve(viewport, insets);
        var box = ClampBox(layout.Box with { Left = layout.Box.Left + dx, Top = layout.Box.Top + dy }, limits, enforceMin: false);
        var moved = layout.Moved || box.Left != layout.Box.Left || box.Top != layout.Box.Top;
        return layout with { Box = box, Moved = moved };
    }

    public static OcrPanelLayout Resize(OcrPanelLayout layout, string edge, double dx, double dy, ViewportSize viewport, OcrInsets? insets = null)
    {
        var limits = Resolve(viewport, insets);
        var flags = EdgeFlags(edge);
        var start = layout.Box;
        var left = start.Left;
        var top = start.Top;
        var right = start.Left + start.Width;
        var bottom = start.Top + start.Height;
        if (flags.E) right += dx;
        if (flags.W) left += dx;
        if (flags.S) bottom += dy;
        if (flags.N) top += dy;
        left = Clamp(left, limits.MinX, limits.MaxX);
        right = Clamp(right, limits.MinX, limits.MaxX);
        top = Clamp(top, limits.MinY, limits.MaxY);
        bottom = Clamp(bottom, limits.MinY, limits.MaxY);
        (left, right) = FitSpan(left, right, limits.MinW, limits.MaxW, flags.W, flags.E, limits.MinX, limits.MaxX);
        (top, bottom) = FitSpan(top, bottom, limits.MinH, limits.MaxH, flags.N, flags.S, limits.MinY, limits.MaxY);
        var box = ClampBox(new OcrBox(left, top, right - left, bottom - top), limits, enforceMin: true);
        var moved = layout.Moved || box.Left != start.Left || box.Top != start.Top;
        return layout with { Box = box, Moved = moved };
    }

    public static OcrPanelLayout StepFont(OcrPanelLayout layout, int direction)
    {
        var font = (int)Clamp(layout.FontPx + direction * FontStep, FontMin, FontMax);
        return layout with { FontPx = font };
    }

    public static OcrPanelLayout ToggleEnlarge(OcrPanelLayout layout, ViewportSize viewport, OcrInsets? insets = null)
    {
        var limits = Resolve(viewport, insets);
        if (layout.Enlarged)
        {
            return layout with
            {
                Enlarged = false,
                Box = PlaceSize(layout, layout.SessionDefault.Width, layout.SessionDefault.Height, limits),
            };
        }
        return layout with
        {
            Enlarged = true,
            Box = PlaceSize(layout, EnlargedWidth, layout.SessionDefault.Height, limits),
        };
    }

    public static OcrPanelLayout Refit(OcrPanelLayout layout, ViewportSize viewport, OcrInsets? insets = null)
    {
        var limits = Resolve(viewport, insets);
        if (!layout.Moved)
            return layout with { Box = AnchorBottomRight(layout.Box.Width, layout.Box.Height, limits, enforceMin: false) };
        return layout with { Box = ClampBox(layout.Box, limits, enforceMin: false) };
    }

    private static OcrBox PlaceSize(OcrPanelLayout layout, double width, double height, Limits limits)
    {
        if (!layout.Moved) return AnchorBottomRight(width, height, limits, enforceMin: false);
        return ClampBox(new OcrBox(layout.Box.Left, layout.Box.Top, width, height), limits, enforceMin: false);
    }

    private static OcrBox AnchorBottomRight(double width, double height, Limits limits, bool enforceMin)
    {
        var sized = ClampBox(new OcrBox(limits.MinX, limits.MinY, width, height), limits, enforceMin);
        return ClampBox(new OcrBox(limits.MaxX - sized.Width, limits.MaxY - sized.Height, sized.Width, sized.Height), limits, enforceMin);
    }

    private static Limits Resolve(ViewportSize viewport, OcrInsets? insets)
    {
        var pad = insets ?? OcrInsets.Default;
        var minX = pad.Left;
        var minY = pad.Top;
        var maxX = Math.Max(pad.Left + 1, viewport.Width - pad.Right);
        var maxY = Math.Max(pad.Top + 1, viewport.Height - pad.Bottom);
        var maxW = Math.Max(1, maxX - minX);
        var maxH = Math.Max(1, maxY - minY);
        return new Limits(minX, minY, maxX, maxY, Math.Min(MinWidth, maxW), maxW, Math.Min(MinHeight, maxH), maxH);
    }

    private static OcrBox ClampBox(OcrBox box, Limits limits, bool enforceMin)
    {
        var minW = enforceMin ? limits.MinW : Math.Min(limits.MinW, box.Width);
        var minH = enforceMin ? limits.MinH : Math.Min(limits.MinH, box.Height);
        var width = Clamp(box.Width, minW, limits.MaxW);
        var height = Clamp(box.Height, minH, limits.MaxH);
        var left = Clamp(box.Left, limits.MinX, Math.Max(limits.MinX, limits.MaxX - width));
        var top = Clamp(box.Top, limits.MinY, Math.Max(limits.MinY, limits.MaxY - height));
        return new OcrBox(Math.Round(left), Math.Round(top), Math.Round(width), Math.Round(height));
    }

    private static (double Left, double Right) FitSpan(double start, double end, double min, double max, bool moveStart, bool moveEnd, double limitStart, double limitEnd)
    {
        var from = start;
        var to = end;
        if (to - from < min)
        {
            if (moveStart && !moveEnd) from = to - min;
            else to = from + min;
        }
        if (to - from > max)
        {
            if (moveStart && !moveEnd) from = to - max;
            else to = from + max;
        }
        from = Clamp(from, limitStart, limitEnd);
        to = Clamp(to, limitStart, limitEnd);
        if (to - from < min && limitEnd - limitStart >= min)
        {
            if (moveStart && !moveEnd) from = Clamp(to - min, limitStart, limitEnd);
            else to = Clamp(from + min, limitStart, limitEnd);
        }
        return (from, to);
    }

    private static (bool N, bool S, bool E, bool W) EdgeFlags(string edge) =>
    (
        edge is "n" or "ne" or "nw",
        edge is "s" or "se" or "sw",
        edge is "e" or "ne" or "se",
        edge is "w" or "nw" or "sw"
    );

    private static double Clamp(double value, double min, double max)
    {
        if (max < min) return min;
        return Math.Min(max, Math.Max(min, value));
    }

    private readonly record struct Limits(double MinX, double MinY, double MaxX, double MaxY, double MinW, double MaxW, double MinH, double MaxH);
}

public readonly record struct OcrInsets(double Top, double Right, double Bottom, double Left)
{
    public static OcrInsets Default => new(OcrPanel.Margin, OcrPanel.Margin, OcrPanel.Margin, OcrPanel.Margin);
}

public readonly record struct OcrBox(double Left, double Top, double Width, double Height);

public sealed record OcrPanelLayout(OcrBox SessionDefault, OcrBox Box, bool Enlarged, int FontPx, bool Moved);

public readonly record struct OcrChrome(bool Drag, bool Resize, bool Font, bool Enlarge);

public sealed class OcrPanelSession
{
    private OcrPanelLayout? _layout;
    public OcrPanelLayout? Current => _layout;

    public OcrPanelLayout Open(ViewportSize viewport, double contentHeight, OcrInsets? insets = null)
    {
        _layout = OcrPanel.Create(viewport, contentHeight, insets);
        return _layout;
    }

    public OcrPanelLayout Commit(OcrPanelLayout next)
    {
        _layout = next;
        return next;
    }

    public void Close() => _layout = null;
}
