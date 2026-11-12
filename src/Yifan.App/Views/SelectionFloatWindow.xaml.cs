using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Yifan.Core.Languages;
using Yifan.Core.Layout;
using Yifan.Core.Selection;
using Yifan.Platform;

namespace Yifan.Views;

public sealed partial class SelectionFloatWindow : Window
{
    private readonly SelectionFloatSession _session = new();
    private SelectionFloatLayout? _layout;
    private bool _moved;
    private int _screenX;
    private int _screenY;
    private bool _applying;
    private bool _dragging;
    private int _dragOriginX;
    private int _dragOriginY;
    private int _windowOriginX;
    private int _windowOriginY;
    private int _expectedW;
    private int _expectedH;
    private (double Left, double Top, double Right, double Bottom)? _selectionPx;
    private string _mode = "translate";
    private bool _loadingLanguages;

    public event Action? CopyRequested;
    public event Action? RetryRequested;
    public event Action? OpenSettingsRequested;
    public event Action<string>? TargetChanged;
    public event Action? Dismissed;

    public SelectionFloatWindow()
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
        AppWindow.Changed += (_, args) =>
        {
            if (_applying || !args.DidSizeChange) return;
            if (AppWindow.Size.Width != _expectedW || AppWindow.Size.Height != _expectedH)
            {
                _applying = true;
                AppWindow.Resize(new SizeInt32(_expectedW, _expectedH));
                _applying = false;
            }
        };
        Closed += (_, _) => Dismissed?.Invoke();
        foreach (var language in Languages.Targets)
            TargetBox.Items.Add(new ComboBoxItem { Content = language.Label, Tag = language.Code });
    }

    public void Present(string? source, string pair, (double Left, double Top, double Right, double Bottom)? selection, string mode)
    {
        _selectionPx = selection;
        _moved = false;
        _screenX = 0;
        _screenY = 0;
        _mode = mode;
        SourceText.Text = source ?? "";
        SourceRule.Visibility = string.IsNullOrEmpty(source) ? Visibility.Collapsed : Visibility.Visible;
        PairLabel.Text = pair;
        TranslationText.Text = mode == "translate" ? "翻译中…" : "";
        ErrorBox.Visibility = Visibility.Collapsed;
        LanguageRow.Visibility = Visibility.Collapsed;
        RebuildActions();
        var anchor = DefaultAnchor();
        _layout = _session.Open(anchor, 180);
        _layout = SelectionFloat.Place(_layout, anchor, Viewport());
        ApplyLayout();
        Activate();
    }

    public void ShowTranslation(string text, bool streaming)
    {
        TranslationText.Text = streaming && text.Length == 0 ? "翻译中…" : text;
        ErrorBox.Visibility = Visibility.Collapsed;
        TranslationText.Visibility = Visibility.Visible;
        _mode = "translate";
        RebuildActions();
        NoteContent();
    }

    public void ShowError(string title, string body, bool remote)
    {
        ErrorTitle.Text = title;
        ErrorBody.Text = body;
        ErrorBox.Visibility = Visibility.Visible;
        TranslationText.Visibility = Visibility.Collapsed;
        _mode = remote ? "error" : "local";
        RebuildActions();
        NoteContent();
    }

    public void SelectTarget(string code)
    {
        _loadingLanguages = true;
        foreach (ComboBoxItem item in TargetBox.Items)
        {
            if ((string?)item.Tag == code)
            {
                TargetBox.SelectedItem = item;
                break;
            }
        }
        _loadingLanguages = false;
    }

    private void RebuildActions()
    {
        Actions.Children.Clear();
        if (_mode == "translate")
        {
            AddButton("复制译文", true, () => CopyRequested?.Invoke());
            AddButton("改语言", false, () =>
            {
                LanguageRow.Visibility = LanguageRow.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                NoteContent();
            });
            AddButton("关闭", false, Close);
        }
        else if (_mode == "error")
        {
            AddButton("打开设置", true, () => OpenSettingsRequested?.Invoke());
            AddButton("重试", false, () => RetryRequested?.Invoke());
            AddButton("关闭", false, Close);
        }
        else
        {
            AddButton("关闭", false, Close);
        }
    }

    private void AddButton(string label, bool primary, Action click)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 0) };
        if (primary)
        {
            button.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 47, 111, 237));
            button.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        }
        button.Click += (_, _) => click();
        Actions.Children.Add(button);
    }

    private void NoteContent()
    {
        if (_layout is null) return;
        UpdateLayout();
        var width = Math.Max(120, SelectionFloat.Width(Viewport().Width, _layout.Enlarged) - 36);
        SourceText.Measure(new Windows.Foundation.Size(width, 4000));
        TranslationText.Measure(new Windows.Foundation.Size(width, 4000));
        ErrorBox.Measure(new Windows.Foundation.Size(width, 4000));
        var body = SourceText.DesiredSize.Height + TranslationText.DesiredSize.Height + ErrorBox.DesiredSize.Height + 36;
        var chrome = 44 + (LanguageRow.Visibility == Visibility.Visible ? 40 : 0) + 52;
        var content = body + chrome;
        var anchor = _moved ? ((double, double)?)null : DefaultAnchor();
        _layout = SelectionFloat.NoteContent(_layout with { Moved = _moved }, content, Viewport());
        if (!_moved && anchor is not null)
            _layout = SelectionFloat.Place(_layout, anchor, Viewport());
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        if (_layout is null) return;
        var viewport = Viewport();
        var size = SelectionFloat.RenderedSize(_layout, viewport);
        var scale = Scale();
        var work = CurrentWork();
        var width = Math.Max(1, (int)Math.Round(size.Width * scale));
        var height = Math.Max(1, (int)Math.Round(size.Height * scale));
        var x = _moved ? _screenX : work.X + _layout.Left * scale;
        var y = _moved ? _screenY : work.Y + _layout.Top * scale;
        var fitted = ScreenClamp.PlaceOnScreen(new ScreenRect(x, y, width, height), AllWork(), 8);
        if (_moved)
        {
            _screenX = (int)Math.Round(fitted.X);
            _screenY = (int)Math.Round(fitted.Y);
        }
        _expectedW = Math.Max(1, (int)Math.Round(fitted.Width));
        _expectedH = Math.Max(1, (int)Math.Round(fitted.Height));
        _applying = true;
        AppWindow.MoveAndResize(new RectInt32((int)fitted.X, (int)fitted.Y, _expectedW, _expectedH));
        _applying = false;
        ZoomButton.Content = _layout.Enlarged ? "还原" : "放大";
    }

    private (double Left, double Top) DefaultAnchor()
    {
        var viewport = Viewport();
        var size = _layout is null
            ? (Width: SelectionFloat.Width(viewport.Width, false), Height: 180d)
            : SelectionFloat.RenderedSize(_layout, viewport);
        var work = CurrentWork();
        var scale = Scale();
        AnchorBox? box = null;
        if (_selectionPx is { } sel)
        {
            box = new AnchorBox(
                (sel.Left - work.X) / scale,
                (sel.Top - work.Y) / scale,
                (sel.Bottom - work.Y) / scale,
                (sel.Right - sel.Left) / scale,
                (sel.Bottom - sel.Top) / scale);
        }
        return FloatAnchor.Compute(box, size.Width, size.Height, viewport);
    }

    private ViewportSize Viewport()
    {
        var work = CurrentWork();
        var scale = Scale();
        return new ViewportSize(Math.Max(1, work.Width / scale), Math.Max(1, work.Height / scale));
    }

    private ScreenRect CurrentWork()
    {
        var areas = AllWork();
        if (areas.Count == 0) return new ScreenRect(0, 0, 1280, 800);
        if (_selectionPx is not { } sel) return areas[0];
        var point = new ScreenRect(sel.Left, sel.Top, 1, 1);
        return areas.OrderBy(area => Distance(point, area)).First();
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

    private static double Distance(ScreenRect point, ScreenRect area)
    {
        var cx = point.X;
        var cy = point.Y;
        var ax = Math.Clamp(cx, area.X, area.X + area.Width);
        var ay = Math.Clamp(cy, area.Y, area.Y + area.Height);
        var dx = cx - ax;
        var dy = cy - ay;
        return dx * dx + dy * dy;
    }

    private void Title_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        _dragging = true;
        NativeMethods.GetCursorPos(out var cursor);
        _dragOriginX = cursor.X;
        _dragOriginY = cursor.Y;
        _windowOriginX = AppWindow.Position.X;
        _windowOriginY = AppWindow.Position.Y;
        TitleBar.CapturePointer(e.Pointer);
    }

    private void Title_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _layout is null) return;
        NativeMethods.GetCursorPos(out var cursor);
        var proposed = new ScreenRect(
            _windowOriginX + (cursor.X - _dragOriginX),
            _windowOriginY + (cursor.Y - _dragOriginY),
            AppWindow.Size.Width,
            AppWindow.Size.Height);
        var fitted = ScreenClamp.PlaceOnScreen(proposed, AllWork(), 8);
        _moved = true;
        _screenX = (int)Math.Round(fitted.X);
        _screenY = (int)Math.Round(fitted.Y);
        _layout = _layout with { Moved = true };
        _applying = true;
        AppWindow.Move(new PointInt32(_screenX, _screenY));
        _applying = false;
    }

    private void Title_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        TitleBar.ReleasePointerCapture(e.Pointer);
        if (_layout is not null) _layout = _layout with { Moved = _moved };
    }

    private void Zoom_Click(object sender, RoutedEventArgs e)
    {
        if (_layout is null) return;
        var anchor = _moved ? ((double, double)?)null : DefaultAnchor();
        _layout = SelectionFloat.ToggleEnlarge(_layout with { Moved = _moved }, Viewport(), anchor);
        _moved = _layout.Moved || _moved;
        ApplyLayout();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TargetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingLanguages) return;
        if (TargetBox.SelectedItem is ComboBoxItem item && item.Tag is string code)
            TargetChanged?.Invoke(code);
    }
}
