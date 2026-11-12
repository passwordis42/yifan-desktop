using System.Runtime.InteropServices;
using Yifan.Core.Shortcuts;

namespace Yifan.Platform;

internal static class RebindHook
{
    private static NativeMethods.LowLevelKeyboardProc? _proc;
    private static IntPtr _hook;
    private static TaskCompletionSource<string?>? _pending;

    public static Task<string?> CaptureAsync()
    {
        Cancel();
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;
        _proc = Hook;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            pending.TrySetResult(null);
            return pending.Task;
        }
        return pending.Task;
    }

    public static void Cancel()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _pending?.TrySetResult(null);
        _pending = null;
        _proc = null;
    }

    private static IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var alt = (NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0;
            var ctrl = (NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0;
            var shift = (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;
            var meta = (NativeMethods.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (NativeMethods.GetAsyncKeyState(0x5C) & 0x8000) != 0;
            var snapshot = Hotkeys.FromVirtualKey(data.vkCode, alt, ctrl, shift, meta, repeat: false);
            if (snapshot is not null)
            {
                var kind = Hotkeys.ClassifyRebind(snapshot);
                if (kind == RebindKind.Cancel)
                {
                    Finish(null);
                    return (IntPtr)1;
                }
                if (kind == RebindKind.Accept)
                {
                    var chord = Hotkeys.FromKey(snapshot);
                    Finish(chord is null ? null : Hotkeys.Format(chord));
                    return (IntPtr)1;
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static void Finish(string? chord)
    {
        var pending = _pending;
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _pending = null;
        _proc = null;
        pending?.TrySetResult(chord);
    }
}
