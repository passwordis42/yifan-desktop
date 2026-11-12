using Yifan.Core.Desktop;

namespace Yifan.Platform;

internal static class SingleInstance
{
    private static Mutex? _mutex;
    private static EventWaitHandle? _show;

    public static bool Acquire()
    {
        _mutex = new Mutex(true, ShellPolicy.MutexName, out var created);
        if (!created)
        {
            _mutex.Dispose();
            return false;
        }
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, ShellPolicy.ShowEventName);
        return true;
    }

    public static void SignalShow()
    {
        try
        {
            using var handle = EventWaitHandle.OpenExisting(ShellPolicy.ShowEventName);
            handle.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // 第一个实例还没开始听。
        }
    }
}
