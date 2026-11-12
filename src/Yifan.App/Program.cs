using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Yifan.Platform;

namespace Yifan;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (!SingleInstance.Acquire())
        {
            SingleInstance.SignalShow();
            return;
        }

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
