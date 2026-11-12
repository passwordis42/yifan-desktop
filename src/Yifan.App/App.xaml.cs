using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Yifan.Logging;
using Yifan.Platform;
using Yifan.Services;

namespace Yifan;

public partial class App : Application
{
    public static AppController Controller { get; private set; } = null!;
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandled;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
                Crash.Write(exception, "进程未处理异常");
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Crash.Write(eventArgs.Exception, "未观察的任务异常");
            eventArgs.SetObserved();
        };

        Controller = new AppController();
        _window = Controller.Start(Environment.GetCommandLineArgs());
    }

    private void OnUnhandled(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs eventArgs)
    {
        Crash.Write(eventArgs.Exception, "界面线程未处理异常");
        eventArgs.Handled = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = "一翻遇到问题",
                Content = "这次操作没有完成，详情已写入本机日志。不会假翻。",
                CloseButtonText = "关闭",
                XamlRoot = _window?.Content.XamlRoot,
            };
            _ = dialog.ShowAsync();
        }
        catch (Exception)
        {
            NativeMessage.Error("一翻遇到问题，已经写入本机日志。不会假翻。");
        }
    }
}
