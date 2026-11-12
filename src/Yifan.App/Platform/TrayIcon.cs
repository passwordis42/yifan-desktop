using System.Runtime.InteropServices;

namespace Yifan.Platform;

internal sealed class TrayIcon : IDisposable
{
    public static TrayIcon? Current { get; private set; }

    private readonly IntPtr _hwnd;
    private readonly IntPtr _icon;
    private readonly uint _id = 1;
    public event Action? OpenMain;
    public event Action? Screenshot;
    public event Action? ToggleService;
    public event Action? Quit;

    public bool ServiceEnabled { get; set; } = true;
    public string CropChord { get; set; } = "Alt+S";
    public bool CropEnabled { get; set; } = true;

    public TrayIcon(IntPtr hwnd, string iconPath)
    {
        _hwnd = hwnd;
        _icon = NativeMethods.LoadImage(IntPtr.Zero, iconPath, NativeMethods.IMAGE_ICON, 16, 16, NativeMethods.LR_LOADFROMFILE);
        var data = Build("一翻");
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
        Current = this;
    }

    public void UpdateTip(string tip)
    {
        var data = Build(tip);
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    public void Handle(int mouse)
    {
        if (mouse == NativeMethods.WM_LBUTTONUP)
        {
            OpenMain?.Invoke();
            return;
        }
        if (mouse is NativeMethods.WM_RBUTTONUP or 0x007B)
            ShowMenu();
    }

    public void Dispose()
    {
        var data = Build("一翻");
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
        if (_icon != IntPtr.Zero) NativeMethods.DestroyIcon(_icon);
        if (Current == this) Current = null;
    }

    private void ShowMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (UIntPtr)1, "打开主窗口");
        var cropFlags = NativeMethods.MF_STRING | (CropEnabled && ServiceEnabled ? 0 : NativeMethods.MF_GRAYED);
        NativeMethods.AppendMenu(menu, cropFlags, (UIntPtr)2, $"截图识译\t{CropChord}");
        NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);
        var serviceFlags = NativeMethods.MF_STRING | (ServiceEnabled ? NativeMethods.MF_CHECKED : 0);
        NativeMethods.AppendMenu(menu, serviceFlags, (UIntPtr)3, "翻译服务");
        NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (UIntPtr)4, "退出");
        NativeMethods.GetCursorPos(out var point);
        NativeMethods.SetForegroundWindow(_hwnd);
        var command = NativeMethods.TrackPopupMenu(
            menu,
            NativeMethods.TPM_RIGHTALIGN | NativeMethods.TPM_BOTTOMALIGN | 0x0100,
            point.X,
            point.Y,
            0,
            _hwnd,
            IntPtr.Zero);
        NativeMethods.DestroyMenu(menu);
        switch (command)
        {
            case 1: OpenMain?.Invoke(); break;
            case 2: Screenshot?.Invoke(); break;
            case 3: ToggleService?.Invoke(); break;
            case 4: Quit?.Invoke(); break;
        }
    }

    private NativeMethods.NOTIFYICONDATA Build(string tip)
    {
        var data = new NativeMethods.NOTIFYICONDATA
        {
            hWnd = _hwnd,
            uID = _id,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = NativeMethods.WM_APP_TRAY,
            hIcon = _icon,
            szTip = tip.Length > 127 ? tip[..127] : tip,
        };
        data.cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>();
        return data;
    }
}
