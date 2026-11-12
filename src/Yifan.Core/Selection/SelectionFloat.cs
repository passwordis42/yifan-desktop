using Yifan.Core.Layout;

namespace Yifan.Core.Selection;

/// <summary>
/// 划词结果窗（对齐扩展 v0.1.14）。只活在这一次打开里。没有边角把手，也没有字号控件。
/// </summary>
public static class SelectionFloat
{
    public const double Margin = 8;
    public const double DefaultWidth = 380;
    public const double EnlargedWidth = 640;
    public const double DefaultGutter = 16;
    public const double ViewportGutter = 48;
    public const double SourceFontPx = 13;
    public const double TranslationFontPx = 15;
    public const double FallbackHeight = 180;

    public static double MaxHeight(double viewportHeight) =>
        Math.Max(1, viewportHeight - ViewportGutter);

    public static double Width(double viewportWidth, bool enlarged)
    {
        var preferred = enlarged ? EnlargedWidth : DefaultWidth;
        var gutter = enlarged ? ViewportGutter : DefaultGutter;
        return Math.Max(1, Math.Min(preferred, viewportWidth - gutter));
    }

    public static (double Height, bool Scrolls) Height(double contentHeight, double viewportHeight, bool enlarged)
    {
        var maxHeight = MaxHeight(viewportHeight);
        if (enlarged) return (maxHeight, contentHeight > maxHeight);
        var natural = contentHeight > 0 ? contentHeight : Math.Min(FallbackHeight, maxHeight);
        return (Math.Min(natural, maxHeight), natural > maxHeight);
    }

    public static (double Width, double Height) RenderedSize(SelectionFloatLayout layout, ViewportSize viewport) =>
        (Width(viewport.Width, layout.Enlarged), Height(layout.ContentHeight, viewport.Height, layout.Enlarged).Height);

    public static (double Left, double Top) ClampPosition(double left, double top, double width, double height, ViewportSize viewport, double margin = Margin)
    {
        var maxLeft = Math.Max(margin, viewport.Width - width - margin);
        var maxTop = Math.Max(margin, viewport.Height - height - margin);
        return (Math.Round(FloatAnchor.Clamp(left, margin, maxLeft)), Math.Round(FloatAnchor.Clamp(top, margin, maxTop)));
    }

    public static SelectionFloatLayout Place(SelectionFloatLayout layout, (double Left, double Top)? anchor, ViewportSize viewport)
    {
        var size = RenderedSize(layout, viewport);
        var origin = layout.Moved || anchor is null ? (layout.Left, layout.Top) : anchor.Value;
        var pos = ClampPosition(origin.Item1, origin.Item2, size.Width, size.Height, viewport);
        return layout with { Left = pos.Left, Top = pos.Top };
    }

    public static SelectionFloatLayout Drag(SelectionFloatLayout layout, double dx, double dy, ViewportSize viewport)
    {
        var size = RenderedSize(layout, viewport);
        var pos = ClampPosition(layout.Left + dx, layout.Top + dy, size.Width, size.Height, viewport);
        var moved = layout.Moved || pos.Left != layout.Left || pos.Top != layout.Top;
        return layout with { Left = pos.Left, Top = pos.Top, Moved = moved };
    }

    public static SelectionFloatLayout ToggleEnlarge(SelectionFloatLayout layout, ViewportSize viewport, (double Left, double Top)? anchor) =>
        Place(layout with { Enlarged = !layout.Enlarged }, layout.Moved ? null : anchor, viewport);

    public static SelectionFloatLayout NoteContent(SelectionFloatLayout layout, double contentHeight, ViewportSize viewport) =>
        Place(layout with { ContentHeight = contentHeight }, null, viewport);
}

public sealed record SelectionFloatLayout(double Left, double Top, bool Enlarged, bool Moved, double ContentHeight);

public sealed class SelectionFloatSession
{
    private SelectionFloatLayout? _layout;

    public SelectionFloatLayout? Current => _layout;

    public SelectionFloatLayout Open((double Left, double Top) anchor, double contentHeight)
    {
        _layout = new SelectionFloatLayout(anchor.Left, anchor.Top, false, false, contentHeight);
        return _layout;
    }

    public SelectionFloatLayout Commit(SelectionFloatLayout next)
    {
        _layout = next;
        return next;
    }

    public void Close() => _layout = null;
}

public static class SelectionFloatChrome
{
    public const bool Drag = true;
    public const bool Enlarge = true;
    public const bool Resize = false;
    public const bool FontStep = false;
}
