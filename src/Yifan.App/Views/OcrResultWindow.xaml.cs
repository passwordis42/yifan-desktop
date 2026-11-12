using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Yifan.Core.Layout;
using Yifan.Core.Ocr;
using Yifan.Platform;

namespace Yifan.Views;

public sealed partial class OcrResultWindow : Window
{
    private readonly OcrPanelSession _session = new();
    private OcrPanelLayout? _layout;
    private bool _ready;
    private bool _dragging;
    private bool _resizing;
    private string? _edge;
    private int _originX;
    private int _originY;
    private int _windowX;
    private int _windowY;
    private double _windowW;
    private double _windowH;
    private bool _hover;

    public event Action? CopySourceRequested;
    public event Action? CopyTranslationRequested;
    public event Action? Dismissed;

    public OcrResultWindow()
    {
        InitializeComponent();
        BrandIcon.Apply(BrandMark);
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        Closed += (_, _) => Dismissed?.Invoke();
        SizeChanged += (_, _) => PlaceHandles();
        foreach (var handle in new[] { HandleN, HandleS, HandleE, HandleW, HandleNw, HandleNe, HandleSw, HandleSe })
        {
            handle.PointerEntered += (_, _) => SetHover(true);
            handle.PointerExited += (_, _) => { if (!_resizing) SetHover(false); };
            handle.PointerPressed += Handle_PointerPressed;
            handle.PointerMoved += Handle_PointerMoved;
            handle.PointerReleased += Handle_PointerReleased;
        }
    }

    public void ShowLoading()
    {
        _ready = false;
        _session.Close();
        _layout = null;
        LoadingPanel.Visibility = Visibility.Visible;
        BodyScroll.Visibility = Visibility.Collapsed;
        FontTools.Visibility = Visibility.Collapsed;
        ZoomButton.Visibility = Visibility.Collapsed;
        Handles.IsHitTestVisible = false;
        SetHover(false);
        var viewport = Viewport();
        var scale = Scale();
        var width = (int)Math.Round(Math.Min(360, viewport.Width - 32) * scale);
        var height = (int)Math.Round(220 * scale);
        var work = Work();
        AppWindow.MoveAndResize(new RectInt32(
            (int)(work.X + work.Width - width - 16),
            (int)(work.Y + work.Height - height - 16),
            Math.Max(1, width),
            Math.Max(1, height)));
        Activate();
    }

    public void ShowResult(string? source, string? translation, string? errorTitle, string? error, string? thumbDataUrl, int cropWidth, int cropHeight)
    {
        _ready = true;
        LoadingPanel.Visibility = Visibility.Collapsed;
        BodyScroll.Visibility = Visibility.Visible;
        FontTools.Visibility = Visibility.Visible;
        ZoomButton.Visibility = Visibility.Visible;
        Handles.IsHitTestVisible = true;
        var hasSource = !string.IsNullOrWhiteSpace(source);
        var hasTranslation = !string.IsNullOrWhiteSpace(translation);
        SourceLabel.Visibility = hasSource ? Visibility.Visible : Visibility.Collapsed;
        SourceText.Text = source ?? "";
        SourceText.Visibility = hasSource ? Visibility.Visible : Visibility.Collapsed;
        TranslationLabel.Visibility = hasTranslation ? Visibility.Visible : Visibility.Collapsed;
        TranslationText.Text = translation ?? "";
        TranslationText.Visibility = hasTranslation ? Visibility.Visible : Visibility.Collapsed;
        ErrorBox.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
        ErrorTitle.Text = errorTitle ?? "识译失败";
        ErrorBody.Text = error ?? "";
        Thumb.Visibility = Visibility.Visible;
        ThumbLabel.Text = $"选区缩略 · {cropWidth}×{cropHeight}";
        if (!string.IsNullOrEmpty(thumbDataUrl) && thumbDataUrl.Contains(',', StringComparison.Ordinal))
        {
            try
            {
                var payload = thumbDataUrl[(thumbDataUrl.IndexOf(',') + 1)..];
                _ = LoadThumbAsync(Convert.FromBase64String(payload));
            }
            catch (Exception)
            {
                ThumbImage.Source = null;
            }
        }
        Actions.Children.Clear();
        AddAction("复制原文", false, hasSource, () => CopySourceRequested?.Invoke());
        AddAction("复制译文", true, hasTranslation, () => CopyTranslationRequested?.Invoke());
        AddAction("关闭", false, true, Close);
        var viewport = Viewport();
        (Content as FrameworkElement)?.UpdateLayout();
        var measured = 280 + (hasSource ? 80 : 0) + (hasTranslation ? 80 : 0) + (string.IsNullOrEmpty(error) ? 0 : 72);
        _layout = _session.Open(viewport, measured);
        ApplyBox();
        ApplyFont();
        Activate();
    }

    private async Task LoadThumbAsync(byte[] bytes)
    {
        try
        {
            var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var writer = new Windows.Storage.Streams.DataWriter(stream);
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
            stream.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            ThumbImage.Source = image;
        }
        catch (Exception)
        {
            ThumbImage.Source = null;
        }
    }

    private void AddAction(string label, bool primary, bool enabled, Action click)
    {
        var button = new Button { Content = label, IsEnabled = enabled, Margin = new Thickness(0, 0, 8, 0) };
        if (primary && enabled)
        {
            button.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 47, 111, 237));
            button.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        }
        button.Click += (_, _) => click();
        Actions.Children.Add(button);
    }

    private void ApplyBox()
    {
        if (_layout is null) return;
        var scale = Scale();
        var work = Work();
        var width = Math.Max(1, (int)Math.Round(_layout.Box.Width * scale));
        var height = Math.Max(1, (int)Math.Round(_layout.Box.Height * scale));
        var x = (int)Math.Round(work.X + _layout.Box.Left * scale);
        var y = (int)Math.Round(work.Y + _layout.Box.Top * scale);
        var fitted = ScreenClamp.PlaceOnScreen(new ScreenRect(x, y, width, height), AllWork(), 8);
        AppWindow.MoveAndResize(new RectInt32((int)fitted.X, (int)fitted.Y, Math.Max(1, (int)fitted.Width), Math.Max(1, (int)fitted.Height)));
        ZoomButton.Content = _layout.Enlarged ? "还原" : "放大";
        Smaller.IsEnabled = _layout.FontPx > OcrPanel.FontMin;
        Larger.IsEnabled = _layout.FontPx < OcrPanel.FontMax;
        PlaceHandles();
    }

    private void ApplyFont()
    {
        if (_layout is null) return;
        SourceText.FontSize = _layout.FontPx;
        TranslationText.FontSize = _layout.FontPx;
        ErrorBody.FontSize = _layout.FontPx;
    }

    private void PlaceHandles()
    {
        var width = Root.ActualWidth;
        var height = Root.ActualHeight;
        if (width <= 0 || height <= 0) return;
        Canvas.SetLeft(HandleN, width / 2 - 14);
        Canvas.SetTop(HandleS, height - 8);
        Canvas.SetLeft(HandleS, width / 2 - 14);
        Canvas.SetLeft(HandleE, width - 8);
        Canvas.SetTop(HandleE, height / 2 - 14);
        Canvas.SetTop(HandleW, height / 2 - 14);
        Canvas.SetLeft(HandleNe, width - 10);
        Canvas.SetTop(HandleSw, height - 10);
        Canvas.SetLeft(HandleSe, width - 10);
        Canvas.SetTop(HandleSe, height - 10);
    }

    private void SetHover(bool hover)
    {
        _hover = hover;
        var opacity = hover && _ready ? 1 : 0;
        foreach (var handle in new[] { HandleN, HandleS, HandleE, HandleW, HandleNw, HandleNe, HandleSw, HandleSe })
            handle.Opacity = opacity;
    }

    private void Title_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_ready || e.OriginalSource is Button) return;
        _dragging = true;
        NativeMethods.GetCursorPos(out var cursor);
        _originX = cursor.X;
        _originY = cursor.Y;
        _windowX = AppWindow.Position.X;
        _windowY = AppWindow.Position.Y;
        TitleBar.CapturePointer(e.Pointer);
    }

    private void Title_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _layout is null) return;
        NativeMethods.GetCursorPos(out var cursor);
        var scale = Scale();
        var dx = (cursor.X - _originX) / scale;
        var dy = (cursor.Y - _originY) / scale;
        _layout = OcrPanel.Drag(_layout, dx, dy, Viewport());
        _originX = cursor.X;
        _originY = cursor.Y;
        ApplyBox();
        _windowX = AppWindow.Position.X;
        _windowY = AppWindow.Position.Y;
    }

    private void Title_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        TitleBar.ReleasePointerCapture(e.Pointer);
    }

    private void Handle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_ready || sender is not FrameworkElement element || element.Tag is not string edge) return;
        _resizing = true;
        _edge = edge;
        NativeMethods.GetCursorPos(out var cursor);
        _originX = cursor.X;
        _originY = cursor.Y;
        _windowW = _layout?.Box.Width ?? 400;
        _windowH = _layout?.Box.Height ?? 300;
        element.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Handle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing || _layout is null || _edge is null) return;
        NativeMethods.GetCursorPos(out var cursor);
        var scale = Scale();
        var dx = (cursor.X - _originX) / scale;
        var dy = (cursor.Y - _originY) / scale;
        _layout = OcrPanel.Resize(_layout, _edge, dx, dy, Viewport());
        _originX = cursor.X;
        _originY = cursor.Y;
        ApplyBox();
    }

    private void Handle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _resizing = false;
        _edge = null;
        if (sender is FrameworkElement element) element.ReleasePointerCapture(e.Pointer);
        SetHover(false);
    }

    private void Smaller_Click(object sender, RoutedEventArgs e) => Step(-1);
    private void Larger_Click(object sender, RoutedEventArgs e) => Step(1);

    private void Step(int direction)
    {
        if (!_ready || _layout is null) return;
        _layout = OcrPanel.StepFont(_layout, direction);
        ApplyFont();
        ApplyBox();
    }

    private void Zoom_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _layout is null) return;
        _layout = OcrPanel.ToggleEnlarge(_layout, Viewport());
        ApplyBox();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private ViewportSize Viewport()
    {
        var work = Work();
        var scale = Scale();
        return new ViewportSize(Math.Max(1, work.Width / scale), Math.Max(1, work.Height / scale));
    }

    private ScreenRect Work()
    {
        var primary = DisplayArea.Primary;
        if (primary is not null)
        {
            var work = primary.WorkArea;
            return new ScreenRect(work.X, work.Y, work.Width, work.Height);
        }
        var areas = AllWork();
        return areas.Count > 0 ? areas[0] : new ScreenRect(0, 0, 1280, 800);
    }

    private static List<ScreenRect> AllWork()
    {
        var list = new List<ScreenRect>();
        foreach (var area in DisplayArea.FindAll())
        {
            var work = area.WorkArea;
            list.Add(new ScreenRect(work.X, work.Y, work.Width, work.Height));
        }
        return list;
    }

    private double Scale()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        return dpi == 0 ? 1 : dpi / 96.0;
    }
}
