using System.Runtime.InteropServices;
using Yifan.Core.Desktop;
using Yifan.Core.Shortcuts;

namespace Yifan.Platform;

internal sealed class HotkeyHost : IDisposable
{
    public const int SelectionId = 1;
    public const int CropId = 2;
    private readonly IntPtr _hwnd;
    private readonly SubclassProc _proc;

    public event Action<string>? Pressed;
    public string? SelectionWarning { get; private set; }
    public string? CropWarning { get; private set; }

    public HotkeyHost(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _proc = Subclass;
        SetWindowSubclass(hwnd, _proc, UIntPtr.Zero, IntPtr.Zero);
    }

    public void Apply(DesktopShortcuts shortcuts, bool serviceEnabled)
    {
        NativeMethods.UnregisterHotKey(_hwnd, SelectionId);
        NativeMethods.UnregisterHotKey(_hwnd, CropId);
        SelectionWarning = null;
        CropWarning = null;
        if (!serviceEnabled) return;
        SelectionWarning = TryRegister(SelectionId, shortcuts.Selection, "划词翻译");
        CropWarning = TryRegister(CropId, shortcuts.Crop, "截图识译");
    }

    public void Dispose()
    {
        NativeMethods.UnregisterHotKey(_hwnd, SelectionId);
        NativeMethods.UnregisterHotKey(_hwnd, CropId);
        RemoveWindowSubclass(_hwnd, _proc, UIntPtr.Zero);
    }

    private string? TryRegister(int id, string chord, string label)
    {
        var mapped = Hotkeys.ToWin32(chord);
        if (mapped is null) return $"{label}的快捷键无法注册，请到设置里改一个。";
        if (!NativeMethods.RegisterHotKey(_hwnd, id, mapped.Value.Modifiers, mapped.Value.VirtualKey))
            return $"{chord} 可能被系统或其他软件占用。{label}请到设置里改一个。";
        return null;
    }

    private IntPtr Subclass(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr data)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var hotkey = wParam.ToInt32();
            if (hotkey == SelectionId) Pressed?.Invoke(ShortcutIds.Selection);
            else if (hotkey == CropId) Pressed?.Invoke(ShortcutIds.Crop);
            return IntPtr.Zero;
        }
        if (msg == NativeMethods.WM_APP_TRAY)
        {
            TrayIcon.Current?.Handle(lParam.ToInt32());
            return IntPtr.Zero;
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr data);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr id, IntPtr data);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr id);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
