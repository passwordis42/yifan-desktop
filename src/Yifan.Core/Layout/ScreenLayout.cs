namespace Yifan.Core.Layout;

public readonly record struct ViewportSize(double Width, double Height);

public readonly record struct AnchorBox(double Left, double Top, double Bottom, double Width, double Height);

public static class FloatAnchor
{
    public const double Margin = 8;
    public const double Gap = 8;

    public static (double Left, double Top) Compute(AnchorBox? rect, double cardWidth, double cardHeight, ViewportSize viewport)
    {
        var maxLeft = Math.Max(Margin, viewport.Width - cardWidth - Margin);
        var maxTop = Math.Max(Margin, viewport.Height - cardHeight - Margin);
        if (rect is null)
        {
            return (
                Clamp((viewport.Width - cardWidth) / 2, Margin, maxLeft),
                Clamp(viewport.Height / 5, Margin, maxTop));
        }
        var top = rect.Value.Bottom + Gap;
        if (top + cardHeight > viewport.Height - Margin)
            top = rect.Value.Top - cardHeight - Gap;
        return (Clamp(rect.Value.Left, Margin, maxLeft), Clamp(top, Margin, maxTop));
    }

    public static double Clamp(double value, double min, double max)
    {
        if (max < min) return min;
        return Math.Min(max, Math.Max(min, value));
    }
}

public readonly record struct ScreenRect(double X, double Y, double Width, double Height);

/// <summary>松手后把窗口收进某一块可见工作区。</summary>
public static class ScreenClamp
{
    public static ScreenRect PlaceOnScreen(ScreenRect window, IReadOnlyList<ScreenRect> workAreas, double margin = 8)
    {
        if (workAreas.Count == 0) return window;
        var target = workAreas
            .Select(area => (Area: area, Overlap: Overlap(window, area)))
            .OrderByDescending(item => item.Overlap)
            .ThenBy(item => Distance(window, item.Area))
            .First().Area;
        var maxW = Math.Max(1, target.Width - margin * 2);
        var maxH = Math.Max(1, target.Height - margin * 2);
        var width = Math.Min(Math.Max(1, window.Width), maxW);
        var height = Math.Min(Math.Max(1, window.Height), maxH);
        var left = FloatAnchor.Clamp(window.X, target.X + margin, target.X + target.Width - margin - width);
        var top = FloatAnchor.Clamp(window.Y, target.Y + margin, target.Y + target.Height - margin - height);
        return new ScreenRect(left, top, width, height);
    }

    private static double Overlap(ScreenRect a, ScreenRect b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        var w = right - left;
        var h = bottom - top;
        if (w <= 0 || h <= 0) return 0;
        return w * h;
    }

    private static double Distance(ScreenRect window, ScreenRect area)
    {
        var cx = window.X + window.Width / 2;
        var cy = window.Y + window.Height / 2;
        var ax = FloatAnchor.Clamp(cx, area.X, area.X + area.Width);
        var ay = FloatAnchor.Clamp(cy, area.Y, area.Y + area.Height);
        var dx = cx - ax;
        var dy = cy - ay;
        return dx * dx + dy * dy;
    }
}
