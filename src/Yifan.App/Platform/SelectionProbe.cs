using System.Windows.Automation;
using Yifan.Core.Logging;

namespace Yifan.Platform;

internal readonly record struct ProbeHit(string? Text, bool Protected, double? Left, double? Top, double? Right, double? Bottom);

internal static class SelectionProbe
{
    public static ProbeHit Read()
    {
        var elevated = ForegroundLooksProtected();
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null) return new ProbeHit(null, elevated, null, null, null, null);
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var raw) || raw is not TextPattern text)
                return new ProbeHit(null, elevated, null, null, null, null);
            var ranges = text.GetSelection();
            if (ranges is null || ranges.Length == 0) return new ProbeHit(null, elevated, null, null, null, null);
            var builder = new System.Text.StringBuilder();
            double? left = null, top = null, right = null, bottom = null;
            foreach (var range in ranges)
            {
                var piece = range.GetText(5001) ?? "";
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(piece);
                if (left is null)
                {
                    var rects = range.GetBoundingRectangles();
                    if (rects.Length > 0)
                    {
                        var rect = rects[0];
                        left = rect.X;
                        top = rect.Y;
                        right = rect.X + rect.Width;
                        bottom = rect.Y + rect.Height;
                    }
                }
            }
            var value = builder.ToString();
            return new ProbeHit(value, false, left, top, right, bottom);
        }
        catch (UnauthorizedAccessException)
        {
            return new ProbeHit(null, true, null, null, null, null);
        }
        catch (ElementNotAvailableException)
        {
            return new ProbeHit(null, elevated, null, null, null, null);
        }
        catch (System.Runtime.InteropServices.COMException exception) when (IsDenied(exception))
        {
            return new ProbeHit(null, true, null, null, null, null);
        }
        catch (Exception exception)
        {
            FileLogHolder.Current?.Error(exception, "读取选中文字失败");
            return new ProbeHit(null, elevated, null, null, null, null);
        }
    }

    private static bool ForegroundLooksProtected()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0) return false;
        var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero) return true;
        NativeMethods.CloseHandle(process);
        return false;
    }

    private static bool IsDenied(System.Runtime.InteropServices.COMException exception)
    {
        var code = unchecked((uint)exception.HResult);
        return code is 0x80070005 or 0x80040201 or 0x80040200;
    }
}

internal static class FileLogHolder
{
    public static Yifan.Core.Logging.FileLog? Current { get; set; }
}
