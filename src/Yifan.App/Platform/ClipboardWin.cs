using System.Runtime.InteropServices;
using System.Text;
using Yifan.Core.Desktop;

namespace Yifan.Platform;

internal sealed class ClipboardWin : IClipboardGateway
{
    private const uint GmemMoveable = 0x0002;

    public string? ReadText()
    {
        if (!NativeMethods.OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            var handle = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return "";
            var pointer = NativeMethods.GlobalLock(handle);
            if (pointer == IntPtr.Zero) return "";
            try
            {
                return Marshal.PtrToStringUni(pointer) ?? "";
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    public void SetText(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text + "\0");
        var memory = NativeMethods.GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
        if (memory == IntPtr.Zero) return;
        var pointer = NativeMethods.GlobalLock(memory);
        if (pointer == IntPtr.Zero) return;
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        NativeMethods.GlobalUnlock(memory);
        if (!NativeMethods.OpenClipboard(IntPtr.Zero)) return;
        try
        {
            NativeMethods.EmptyClipboard();
            if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, memory) == IntPtr.Zero)
                return;
            memory = IntPtr.Zero;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    public static ClipboardBag CaptureBag()
    {
        var bag = new ClipboardBag();
        if (!NativeMethods.OpenClipboard(IntPtr.Zero)) return bag;
        try
        {
            uint format = 0;
            while ((format = NativeMethods.EnumClipboardFormats(format)) != 0)
            {
                try
                {
                    var handle = NativeMethods.GetClipboardData(format);
                    if (handle == IntPtr.Zero) continue;
                    var size = (int)NativeMethods.GlobalSize(handle);
                    if (size <= 0 || size > 32 * 1024 * 1024) continue;
                    var pointer = NativeMethods.GlobalLock(handle);
                    if (pointer == IntPtr.Zero) continue;
                    try
                    {
                        var data = new byte[size];
                        Marshal.Copy(pointer, data, 0, size);
                        bag.Items.Add((format, data));
                    }
                    finally
                    {
                        NativeMethods.GlobalUnlock(handle);
                    }
                }
                catch (Exception)
                {
                    // 有的格式不是全局内存，跳过这一项，文本仍会还原。
                }
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
        return bag;
    }

    public static void RestoreBag(ClipboardBag bag)
    {
        if (!NativeMethods.OpenClipboard(IntPtr.Zero)) return;
        try
        {
            NativeMethods.EmptyClipboard();
            foreach (var (format, data) in bag.Items)
            {
                var memory = NativeMethods.GlobalAlloc(GmemMoveable, (UIntPtr)data.Length);
                if (memory == IntPtr.Zero) continue;
                var pointer = NativeMethods.GlobalLock(memory);
                if (pointer == IntPtr.Zero) continue;
                Marshal.Copy(data, 0, pointer, data.Length);
                NativeMethods.GlobalUnlock(memory);
                if (NativeMethods.SetClipboardData(format, memory) == IntPtr.Zero)
                    continue;
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    public static void SendCopy()
    {
        var inputs = new NativeMethods.INPUT[4];
        inputs[0] = Key(0x11, false);
        inputs[1] = Key(0x43, false);
        inputs[2] = Key(0x43, true);
        inputs[3] = Key(0x11, true);
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public static void ReleaseModifiers(bool alt, bool ctrl, bool shift, bool meta)
    {
        var inputs = new List<NativeMethods.INPUT>();
        if (alt) inputs.Add(Key(0x12, true));
        if (ctrl) inputs.Add(Key(0x11, true));
        if (shift) inputs.Add(Key(0x10, true));
        if (meta) inputs.Add(Key(0x5B, true));
        if (inputs.Count == 0) return;
        NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT Key(ushort vk, bool up) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.INPUTUNION
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = vk,
                dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0,
            },
        },
    };
}

internal sealed class ClipboardBag
{
    public List<(uint Format, byte[] Data)> Items { get; } = [];
}
