using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Yifan.Platform;
using Yifan.Services;

namespace Yifan;

public sealed partial class MainWindow : Window
{
    private bool _allowExit;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "yifan.ico"));
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 760;
            presenter.PreferredMinimumHeight = 560;
        }
        AppWindow.Resize(new SizeInt32((int)Math.Round(880 * scale), (int)Math.Round(696 * scale)));
        Closed += OnClosed;
    }

    public void ShowPage(string page)
    {
        var home = page != "settings";
        Home.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
        HomeNav.Background = new SolidColorBrush(home ? WindowsColor(255, 255, 255) : WindowsColor(0, 0, 0, 0));
        SettingsNav.Background = new SolidColorBrush(home ? WindowsColor(0, 0, 0, 0) : WindowsColor(255, 255, 255));
        if (!home) SettingsPage.Reload();
        else Home.Reload();
        Activate();
        AppWindow.Show();
    }

    public void AllowExit() => _allowExit = true;

    private void HomeNav_Click(object sender, RoutedEventArgs e) => ShowPage("home");
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowPage("settings");

    private void OnClosed(object sender, WindowEventArgs e)
    {
        if (_allowExit) return;
        e.Handled = true;
        AppWindow.Hide();
    }

    private static Windows.UI.Color WindowsColor(byte r, byte g, byte b) =>
        Windows.UI.Color.FromArgb(255, r, g, b);

    private static Windows.UI.Color WindowsColor(byte a, byte r, byte g, byte b) =>
        Windows.UI.Color.FromArgb(a, r, g, b);
}
