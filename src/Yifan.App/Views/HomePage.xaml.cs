using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Yifan.Views;

public sealed partial class HomePage : UserControl
{
    private bool _loading;

    public HomePage()
    {
        InitializeComponent();
    }

    public void Reload()
    {
        var controller = App.Controller;
        var model = controller.Home();
        _loading = true;
        ServiceTitle.Text = model.ServiceTitle;
        ServiceBody.Text = model.ServiceBody;
        ServiceSwitch.IsOn = model.ServiceOn;
        SelectionChord.Text = model.SelectionChord;
        CropChord.Text = model.CropChord;
        SelectionTitle.Text = model.Selection.Title;
        SelectionBody.Text = model.Selection.Body;
        CropTitle.Text = model.Crop.Title;
        CropBody.Text = model.Crop.Body;
        SelectionCard.Opacity = model.Selection.Dimmed ? 0.55 : 1;
        CropCard.Opacity = model.Crop.Dimmed ? 0.72 : 1;
        ProviderName.Text = model.ProviderName;
        ProviderLine.Text = model.ProviderLine;
        LanguageLine.Text = model.LanguageLine;
        LanguageHint.Text = model.LanguageHint;
        GlossaryLine.Text = model.GlossaryLine;
        GlossaryHint.Text = model.GlossaryHint;
        ExtraRow.Visibility = model.ShowOcrButton ? Visibility.Visible : Visibility.Collapsed;
        OcrButton.IsEnabled = model.OcrButtonEnabled;
        OcrButton.Style = (Style)Application.Current.Resources[model.OcrButtonEnabled ? "SoftButton" : "QuietButton"];
        OcrHint.Text = model.OcrButtonEnabled ? $"也可按 {model.CropChord}，与按钮同一入口" : "";
        WarnBanner.Visibility = model.OcrWarning is null ? Visibility.Collapsed : Visibility.Visible;
        WarnText.Text = model.OcrWarning ?? "";
        var warning = controller.HotkeyWarning;
        HotkeyWarning.Text = warning ?? "";
        HotkeyWarning.Visibility = string.IsNullOrEmpty(warning) ? Visibility.Collapsed : Visibility.Visible;
        Permission.Text = model.ServiceOn
            ? "一翻需要全局热键、读取选中文字和框选屏幕，仅用于当次翻译，发往你配置的当前厂商。API Key 只存在本机。关掉本窗口后从托盘可再打开或退出。"
            : "关掉本窗口不会退出，只会回到托盘。要彻底退出，请用托盘菜单里的「退出」。";
        _loading = false;
    }

    private void ServiceSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Controller.SetService(ServiceSwitch.IsOn);
        Reload();
    }

    private void OcrButton_Click(object sender, RoutedEventArgs e) => App.Controller.RequestCrop();
}
