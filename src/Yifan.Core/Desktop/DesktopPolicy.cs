using Yifan.Core.Languages;
using Yifan.Core.Providers;
using Yifan.Core.Quota;
using Yifan.Core.Settings;
using Yifan.Core.Shortcuts;

namespace Yifan.Core.Desktop;

/// <summary>第一版明确不做的能力。保持为 false，避免界面或热键把它们打开。</summary>
public static class ProductScope
{
    public const bool FullPageTranslation = false;
    public const bool InAppBrowser = false;
    public const bool ContextMenus = false;
    public const bool ForceSelect = false;
    public const bool CornerGestures = false;
    public const bool MacOs = false;
    public const bool Linux = false;
    public const bool Mobile = false;
    public const bool ExtensionSync = false;
    public const bool CustomBaseUrl = false;
    public const bool CloudAkSk = false;
    public const bool TranslationHistory = false;
    public const bool SelectionFloatResize = false;
    public const bool SelectionFloatFontStep = false;
    public const bool AutoTranslateOnShow = false;
    public const bool AutoUpdateInstall = false;
}

public enum HotkeyEffect
{
    Ignore,
    RejectNoVision,
    Run,
}

public static class HotkeyDispatch
{
    public static HotkeyEffect Decide(bool serviceEnabled, string action, bool providerHasVision)
    {
        if (!serviceEnabled) return HotkeyEffect.Ignore;
        if (action == ShortcutIds.Crop && !providerHasVision) return HotkeyEffect.RejectNoVision;
        return HotkeyEffect.Run;
    }
}

public enum CloseAction
{
    HideToTray,
    Quit,
}

public static class ShellPolicy
{
    public static CloseAction MainWindowClose => CloseAction.HideToTray;
    public const string TrayArgument = "--tray";
    public const string MutexName = @"Local\Yifan.Desktop.v0.1";
    public const string ShowEventName = @"Local\Yifan.Desktop.v0.1.Show";
    public const string RunValueName = "Yifan";
}

public sealed record HomeCard(string Title, string Body, bool Dimmed);

public sealed record HomeModel(
    string ServiceTitle,
    string ServiceBody,
    bool ServiceOn,
    string SelectionChord,
    string CropChord,
    HomeCard Selection,
    HomeCard Crop,
    string ProviderName,
    string ProviderLine,
    string LanguageLine,
    string LanguageHint,
    string GlossaryLine,
    string GlossaryHint,
    bool ShowOcrButton,
    bool OcrButtonEnabled,
    string? OcrWarning,
    bool ShowQuotaBadge,
    string QuotaBadge);

public static class HomeCopy
{
    public static HomeModel Build(AppSettings settings, QuotaView? quota)
    {
        var selection = settings.Shortcuts.Selection;
        var crop = settings.Shortcuts.Crop;
        var provider = ProviderCatalog.Get(settings.Provider);
        var vision = ProviderCatalog.SupportsVision(settings.Provider);
        var on = settings.ServiceEnabled;
        var glossaryCount = settings.Glossary.Count;
        string providerLine;
        string languageHint;
        var showBadge = false;
        var badge = "";
        if (!on)
        {
            providerLine = "设置保留，暂停不影响已保存的 Key";
            languageHint = "在设置里修改";
        }
        else if (quota is not null)
        {
            providerLine = $"{settings.Model} · {quota.Main}";
            languageHint = "在设置里修改；改语言后按新方向重翻";
            showBadge = true;
            badge = quota.Badge;
        }
        else
        {
            providerLine = settings.Model;
            languageHint = "在设置里修改；改语言后按新方向重翻";
        }

        var selectionCard = on
            ? new HomeCard("划词翻译", "优先取当前窗口的选中文字；取不到再用剪贴板。超过 5000 字会提示缩短，不发请求。", false)
            : new HomeCard("划词翻译", "已暂停，按键无响应。", true);
        var cropCard = !on
            ? new HomeCard("截图识译", "已暂停，按键无响应。", true)
            : !vision
                ? new HomeCard("截图识译不可用", "当前厂商没有可用的视觉模型。", true)
                : new HomeCard("截图识译", "框选屏幕上任意区域或窗口。Esc 或点空白取消。", false);

        return new HomeModel(
            on ? "翻译服务已开启" : "翻译服务已暂停",
            on
                ? $"在任意软件里选中文字，按 {selection} 翻译。取不到选中内容时会短暂读取剪贴板，用完还原。"
                : "全局热键不会触发划词翻译或截图识译。再打开开关后恢复，快捷键设置保持不变。",
            on,
            selection,
            crop,
            selectionCard,
            cropCard,
            provider.Label,
            providerLine,
            Languages.Languages.HomePair(settings.SourceLang, settings.TargetLang),
            languageHint,
            glossaryCount == 0 ? "尚未添加" : $"{glossaryCount} 条固定译法",
            "命中优先 · 到设置里增删",
            on,
            on && vision,
            on && !vision ? $"{ProviderCatalog.NoVisionOcrMessage}。请到设置里换成带视觉模型的厂商，不会偷偷换到别家。" : null,
            showBadge,
            badge);
    }

    public static bool ShowsRealHotkeys(HomeModel home) =>
        home.SelectionChord.Length > 0
        && home.CropChord.Length > 0
        && !home.ServiceBody.Contains("未设置", StringComparison.Ordinal)
        && !home.Selection.Body.Contains("未设置", StringComparison.Ordinal)
        && !home.Crop.Body.Contains("未设置", StringComparison.Ordinal);
}

public interface IClipboardGateway
{
    string? ReadText();
    void SetText(string text);
}

public static class ClipboardRestore
{
    public const int HoldBudgetMs = 400;

    public static async Task<T> WithRestoreAsync<T>(IClipboardGateway clipboard, Func<Task<T>> body)
    {
        var original = clipboard.ReadText();
        try
        {
            return await body().ConfigureAwait(false);
        }
        finally
        {
            clipboard.SetText(original ?? "");
        }
    }
}
