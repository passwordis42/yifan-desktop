using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Yifan.Platform;

internal readonly record struct CropResult(bool Cancelled, string? DataUrl, int Width, int Height, string? Error);

/// <summary>
/// 框选时只在内存里留一帧屏幕。取消会丢掉这帧，不上传、不写盘、不改剪贴板。
/// </summary>
internal sealed class CropOverlay
{
    private const string ClassName = "YifanCropOverlay";
    private static readonly Dictionary<IntPtr, CropOverlay> Live = new();
    private static bool _classReady;
    private static NativeMethods.WndProc? _proc;

    private readonly TaskCompletionSource<CropResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _originX;
    private readonly int _originY;
    private readonly Bitmap _shot;
    private IntPtr _hwnd;
    private bool _dragging;
    private Point _start;
    private Rectangle _selection;
    private bool _hasSelection;
    private Rectangle _cancelButton;
    private Rectangle _okButton;

    private CropOverlay(Bitmap shot, int originX, int originY)
    {
        _shot = shot;
        _originX = originX;
        _originY = originY;
    }

    public static Task<CropResult> PickAsync()
    {
        int originX = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int originY = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        Bitmap shot;
        try
        {
            shot = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(shot);
            graphics.CopyFromScreen(originX, originY, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }
        catch (Exception)
        {
            return Task.FromResult(new CropResult(false, null, 0, 0, "截不到当前屏幕。不会假翻。"));
        }
        var overlay = new CropOverlay(shot, originX, originY);
        EnsureClass();
        overlay._hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_TOOLWINDOW,
            ClassName,
            "一翻框选",
            NativeMethods.WS_POPUP,
            originX,
            originY,
            width,
            height,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.GetModuleHandle(null),
            IntPtr.Zero);
        if (overlay._hwnd == IntPtr.Zero)
        {
            shot.Dispose();
            return Task.FromResult(new CropResult(false, null, 0, 0, "打不开框选层。不会假翻。"));
        }
        Live[overlay._hwnd] = overlay;
        NativeMethods.ShowWindow(overlay._hwnd, 5);
        NativeMethods.SetForegroundWindow(overlay._hwnd);
        return overlay._done.Task;
    }

    private static void EnsureClass()
    {
        if (_classReady) return;
        _proc = WndProc;
        var wc = new NativeMethods.WNDCLASS
        {
            lpfnWndProc = _proc,
            lpszClassName = ClassName,
            hInstance = NativeMethods.GetModuleHandle(null),
        };
        NativeMethods.RegisterClass(ref wc);
        _classReady = true;
    }

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        Live.TryGetValue(hwnd, out var overlay);
        switch (msg)
        {
            case 0x000F when overlay is not null: // WM_PAINT
                overlay.Paint();
                return IntPtr.Zero;
            case NativeMethods.WM_LBUTTONDOWN when overlay is not null:
                overlay.MouseDown(Lo(lParam), Hi(lParam));
                return IntPtr.Zero;
            case NativeMethods.WM_MOUSEMOVE when overlay is not null && overlay._dragging:
                overlay.MouseMove(Lo(lParam), Hi(lParam));
                return IntPtr.Zero;
            case NativeMethods.WM_LBUTTONUP when overlay is not null:
                overlay.MouseUp(Lo(lParam), Hi(lParam));
                return IntPtr.Zero;
            case NativeMethods.WM_KEYDOWN when overlay is not null && wParam.ToInt32() == 0x1B:
                overlay.Finish(new CropResult(true, null, 0, 0, null));
                return IntPtr.Zero;
            case NativeMethods.WM_DESTROY:
                return IntPtr.Zero;
        }
        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void MouseDown(int x, int y)
    {
        if (_hasSelection && _cancelButton.Contains(x, y))
        {
            Finish(new CropResult(true, null, 0, 0, null));
            return;
        }
        if (_hasSelection && _okButton.Contains(x, y))
        {
            Confirm();
            return;
        }
        if (_hasSelection && !_selection.Contains(x, y))
        {
            Finish(new CropResult(true, null, 0, 0, null));
            return;
        }
        _dragging = true;
        _start = new Point(x, y);
        _hasSelection = false;
        SetCapture(_hwnd);
        Invalidate();
    }

    private void MouseMove(int x, int y)
    {
        _selection = Rectangle.FromLTRB(Math.Min(_start.X, x), Math.Min(_start.Y, y), Math.Max(_start.X, x), Math.Max(_start.Y, y));
        Invalidate();
    }

    private void MouseUp(int x, int y)
    {
        ReleaseCapture();
        _dragging = false;
        _selection = Rectangle.FromLTRB(Math.Min(_start.X, x), Math.Min(_start.Y, y), Math.Max(_start.X, x), Math.Max(_start.Y, y));
        if (_selection.Width < 12 || _selection.Height < 12)
        {
            Finish(new CropResult(true, null, 0, 0, null));
            return;
        }
        _hasSelection = true;
        Invalidate();
    }

    private void Confirm()
    {
        try
        {
            var rect = Rectangle.Intersect(_selection, new Rectangle(0, 0, _shot.Width, _shot.Height));
            if (rect.Width < 2 || rect.Height < 2)
            {
                Finish(new CropResult(false, null, 0, 0, "选区太小。不会假翻。"));
                return;
            }
            using var crop = _shot.Clone(rect, _shot.PixelFormat);
            var url = ScreenGrab.JpegDataUrl(crop, out var error);
            Finish(new CropResult(url is null, url, rect.Width, rect.Height, error));
        }
        catch (Exception)
        {
            Finish(new CropResult(false, null, 0, 0, "截不到这块屏幕。不会假翻。"));
        }
    }

    private void Paint()
    {
        var ps = new PAINTSTRUCT();
        var hdc = BeginPaint(_hwnd, out ps);
        using var graphics = Graphics.FromHdc(hdc);
        graphics.DrawImage(_shot, 0, 0);
        using var dim = new SolidBrush(Color.FromArgb(110, 20, 24, 32));
        graphics.FillRectangle(dim, 0, 0, _shot.Width, _shot.Height);
        using var font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold);
        using var tipBrush = new SolidBrush(Color.FromArgb(244, 241, 234));
        graphics.DrawString(global::Yifan.Core.Ocr.Ocr.CropTip, font, tipBrush, new PointF(24, 24));
        if (_dragging || _hasSelection)
        {
            graphics.SetClip(_selection);
            graphics.DrawImage(_shot, 0, 0);
            graphics.ResetClip();
            using var pen = new Pen(Color.FromArgb(47, 111, 237), 2);
            graphics.DrawRectangle(pen, _selection);
            if (_hasSelection) DrawActions(graphics);
        }
        EndPaint(_hwnd, ref ps);
    }

    private void DrawActions(Graphics graphics)
    {
        var size = $"{_selection.Width} × {_selection.Height}";
        _cancelButton = new Rectangle(_selection.Right - 196, _selection.Bottom + 8, 72, 32);
        _okButton = new Rectangle(_selection.Right - 116, _selection.Bottom + 8, 116, 32);
        if (_okButton.Bottom > _shot.Height - 8)
        {
            _cancelButton.Y = _selection.Top - 40;
            _okButton.Y = _selection.Top - 40;
        }
        using var bg = new SolidBrush(Color.FromArgb(20, 20, 20));
        using var white = new SolidBrush(Color.White);
        using var blue = new SolidBrush(Color.FromArgb(47, 111, 237));
        using var font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold);
        var label = new Rectangle(_selection.Left, _cancelButton.Y, 110, 32);
        graphics.FillRectangle(bg, label);
        graphics.DrawString(size, font, white, label.X + 8, label.Y + 6);
        graphics.FillRectangle(white, _cancelButton);
        graphics.DrawString("取消", font, bg, _cancelButton.X + 18, _cancelButton.Y + 6);
        graphics.FillRectangle(blue, _okButton);
        graphics.DrawString("识别并翻译", font, white, _okButton.X + 14, _okButton.Y + 6);
    }

    private void Invalidate() => InvalidateRect(_hwnd, IntPtr.Zero, false);

    private void Finish(CropResult result)
    {
        Live.Remove(_hwnd);
        NativeMethods.DestroyWindow(_hwnd);
        _shot.Dispose();
        _done.TrySetResult(result);
    }

    private static int Lo(IntPtr value) => unchecked((short)(value.ToInt64() & 0xFFFF));
    private static int Hi(IntPtr value) => unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
}
