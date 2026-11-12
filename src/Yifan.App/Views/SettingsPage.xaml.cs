using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Yifan.Core.Languages;
using Yifan.Core.Providers;
using Yifan.Core.Shortcuts;

namespace Yifan.Views;

public sealed partial class SettingsPage : UserControl
{
    private bool _loading;
    private string? _shown;
    private readonly Dictionary<string, string> _draftKeys = new();
    private readonly Dictionary<string, string> _draftModels = new();
    private readonly Dictionary<string, string> _draftCustom = new();
    private string? _listening;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    public void Reload()
    {
        var settings = App.Controller.Settings;
        _loading = true;
        if (ProviderBox.Items.Count == 0)
        {
            foreach (var id in ProviderCatalog.Ids)
                ProviderBox.Items.Add(new ComboBoxItem { Content = ProviderCatalog.All[id].Label, Tag = id });
            foreach (var language in Languages.Sources)
                SourceBox.Items.Add(new ComboBoxItem { Content = language.Label, Tag = language.Code });
            foreach (var language in Languages.Targets)
                TargetBox.Items.Add(new ComboBoxItem { Content = language.Label, Tag = language.Code });
        }
        SelectTag(ProviderBox, settings.Provider);
        SelectTag(SourceBox, settings.SourceLang);
        SelectTag(TargetBox, settings.TargetLang);
        StartupSwitch.IsOn = settings.StartOnLogin;
        _shown = settings.Provider;
        ShowProvider(settings.Provider);
        RenderGlossary();
        RenderHotkeys();
        _loading = false;
    }

    private void ShowProvider(string provider)
    {
        var profile = ProviderCatalog.Get(provider);
        var settings = App.Controller.Settings;
        var slot = settings.Providers[provider];
        KeyHint.Text = profile.KeyHint;
        ModelLabel.Text = profile.ModelLabel;
        ModelHint.Text = profile.ModelHint;
        CustomLabel.Text = profile.CustomLabel;
        CustomHint.Text = profile.CustomHint;
        CustomBox.PlaceholderText = profile.CustomPlaceholder;
        VolcanoNote.Visibility = provider == ProviderCatalog.Volcano ? Visibility.Visible : Visibility.Collapsed;
        KeyBox.Password = _draftKeys.TryGetValue(provider, out var key) ? key : slot.ApiKey;
        ModelBox.Items.Clear();
        foreach (var tier in profile.ModelTiers)
        {
            foreach (var modelId in tier.Models)
                ModelBox.Items.Add(new ComboBoxItem { Content = modelId, Tag = modelId });
        }
        var model = _draftModels.TryGetValue(provider, out var drafted) ? drafted : slot.Model;
        var custom = _draftCustom.TryGetValue(provider, out var draftedCustom) ? draftedCustom : "";
        if (!ProviderCatalog.IsCurated(provider, model) && model != profile.DefaultModel && custom.Length == 0)
            custom = model;
        if (custom.Length == 0 && !ProviderCatalog.IsCurated(provider, slot.Model))
            custom = slot.Model == profile.DefaultModel ? "" : slot.Model;
        CustomBox.Text = custom;
        var selected = ProviderCatalog.IsCurated(provider, model) ? model : profile.DefaultModel;
        SelectTag(ModelBox, selected);
        var quota = App.Controller.LastQuota;
        QuotaMain.Text = quota?.Main ?? "正在查询额度…";
        QuotaSub.Text = quota?.Sub ?? profile.EstimateNote;
        QuotaBadge.Text = quota?.Badge ?? "估算";
    }

    private void RememberShownDraft()
    {
        if (_shown is null) return;
        _draftKeys[_shown] = KeyBox.Password;
        _draftModels[_shown] = SelectedTag(ModelBox) ?? "";
        _draftCustom[_shown] = CustomBox.Text;
    }

    private void RenderGlossary()
    {
        GlossaryList.Children.Clear();
        foreach (var entry in App.Controller.Settings.Glossary)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var source = new TextBlock { Text = entry.Source, VerticalAlignment = VerticalAlignment.Center };
            var arrow = new TextBlock { Text = "→", Margin = new Thickness(4, 0, 4, 0), Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 111, 106, 96)) };
            var target = new TextBlock { Text = entry.Target, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var remove = new Button { Content = "删除", Tag = entry.Id };
            remove.Click += (_, _) =>
            {
                App.Controller.RemoveGlossary((string)remove.Tag);
                RenderGlossary();
            };
            Grid.SetColumn(source, 0);
            Grid.SetColumn(arrow, 1);
            Grid.SetColumn(target, 2);
            Grid.SetColumn(remove, 3);
            row.Children.Add(source);
            row.Children.Add(arrow);
            row.Children.Add(target);
            row.Children.Add(remove);
            GlossaryList.Children.Add(row);
        }
    }

    private void RenderHotkeys()
    {
        var shortcuts = App.Controller.Settings.Shortcuts;
        SelectionKeyText.Text = _listening == ShortcutIds.Selection ? "等待按键" : shortcuts.Selection;
        CropKeyText.Text = _listening == ShortcutIds.Crop ? "等待按键" : shortcuts.Crop;
        SelectionRebind.Content = _listening == ShortcutIds.Selection ? "取消" : "改键";
        CropRebind.Content = _listening == ShortcutIds.Crop ? "取消" : "改键";
        HotkeyNote.Text = _listening is null
            ? "两条互不冲突。与系统或其他软件冲突时可改，改完立即生效。主页显示实际生效的键。"
            : "正在监听新快捷键。与另一条或系统快捷键冲突时请换一组。松开后立即生效。";
    }

    private async void SelectionRebind_Click(object sender, RoutedEventArgs e) => await Rebind(ShortcutIds.Selection);
    private async void CropRebind_Click(object sender, RoutedEventArgs e) => await Rebind(ShortcutIds.Crop);

    private async Task Rebind(string id)
    {
        if (_listening == id)
        {
            App.Controller.CancelRebind();
            _listening = null;
            RenderHotkeys();
            return;
        }
        _listening = id;
        RenderHotkeys();
        var chord = await App.Controller.CaptureHotkeyAsync();
        _listening = null;
        if (chord is null) App.Controller.ReapplyHotkeys();
        else Status.Text = App.Controller.AssignHotkey(id, chord);
        RenderHotkeys();
    }

    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RememberShownDraft();
        var provider = SelectedTag(ProviderBox);
        if (provider is null) return;
        _shown = provider;
        ShowProvider(provider);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var provider = SelectedTag(ProviderBox) ?? ProviderCatalog.Qwen;
        _draftKeys[provider] = KeyBox.Password;
        _draftModels[provider] = SelectedTag(ModelBox) ?? "";
        _draftCustom[provider] = CustomBox.Text;
        var message = App.Controller.SaveForm(
            provider,
            KeyBox.Password,
            SelectedTag(ModelBox) ?? "",
            CustomBox.Text,
            SelectedTag(SourceBox) ?? "auto",
            SelectedTag(TargetBox) ?? "zh",
            _draftKeys,
            _draftModels,
            _draftCustom);
        Status.Text = message;
        Reload();
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        Status.Text = "正在测试连接…";
        var message = await App.Controller.TestConnectionAsync(
            SelectedTag(ProviderBox) ?? ProviderCatalog.Qwen,
            KeyBox.Password,
            SelectedTag(ModelBox) ?? "",
            CustomBox.Text);
        Status.Text = message;
        TestButton.IsEnabled = true;
        ShowProvider(SelectedTag(ProviderBox) ?? ProviderCatalog.Qwen);
    }

    private void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Controller.SetStartup(StartupSwitch.IsOn);
    }

    private void AddGlossary_Click(object sender, RoutedEventArgs e)
    {
        var message = App.Controller.AddGlossary(GlossarySource.Text, GlossaryTarget.Text);
        Status.Text = message ?? "";
        if (message is null)
        {
            GlossarySource.Text = "";
            GlossaryTarget.Text = "";
        }
        RenderGlossary();
    }

    private void RestoreHotkeys_Click(object sender, RoutedEventArgs e)
    {
        App.Controller.RestoreHotkeys();
        RenderHotkeys();
        Status.Text = "已恢复为 Alt+E / Alt+S。";
    }

    private static void SelectTag(ComboBox box, string tag)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if ((string?)item.Tag == tag)
            {
                box.SelectedItem = item;
                return;
            }
        }
    }

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
