using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Yifan.Core.Chat;
using Yifan.Core.Desktop;
using Yifan.Core.Errors;
using Yifan.Core.Glossary;
using Yifan.Core.Languages;
using Yifan.Core.Providers;
using Yifan.Core.Quota;
using Yifan.Core.Selection;
using Yifan.Core.Settings;
using Yifan.Core.Shortcuts;
using Yifan.Core.Storage;
using Yifan.Platform;
using Yifan.Views;
using GlossaryBook = Yifan.Core.Glossary.Glossary;
using LanguageList = Yifan.Core.Languages.Languages;
using OcrJob = Yifan.Core.Ocr.Ocr;

namespace Yifan.Services;

public sealed class AppController
{
    private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread();
    private AppSettings _settings = AppSettingsFactory.Default();
    private string _settingsPath = "";
    private FileUsageLedger? _usage;
    private YifanClient? _client;
    private MainWindow? _main;
    private HotkeyHost? _hotkeys;
    private TrayIcon? _tray;
    private EventWaitHandle? _showEvent;
    private Thread? _listener;
    private volatile bool _quit;
    private SelectionFloatWindow? _float;
    private OcrResultWindow? _ocr;
    private CancellationTokenSource? _translateCts;
    private CancellationTokenSource? _ocrCts;
    private string _activeText = "";
    private string _latestTranslation = "";
    private string _ocrSource = "";
    private string _ocrTranslation = "";
    private readonly HttpClient _http = new();
    private (double Left, double Top, double Right, double Bottom)? _anchor;

    public AppSettings Settings => _settings;
    public QuotaView? LastQuota { get; private set; }

    public string? HotkeyWarning
    {
        get
        {
            var parts = new[] { _hotkeys?.SelectionWarning, _hotkeys?.CropWarning }
                .Where(part => !string.IsNullOrWhiteSpace(part));
            var text = string.Join(" ", parts);
            return text.Length == 0 ? null : text;
        }
    }

    public Window Start(string[] args)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var exeDirectory = AppContext.BaseDirectory;
        _settingsPath = AppPaths.RequireSettingsPath(appData, exeDirectory);
        _settings = SettingsStore.Load(_settingsPath);
        var log = new Yifan.Core.Logging.FileLog(AppPaths.LogPath(appData));
        FileLogHolder.Current = log;
        log.Info("一翻 0.1.0 启动");
        _usage = new FileUsageLedger(AppPaths.UsagePath(appData));
        _client = new YifanClient(_http, _usage);
        _main = new MainWindow();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_main);
        _hotkeys = new HotkeyHost(hwnd);
        _hotkeys.Pressed += OnHotkey;
        _hotkeys.Apply(_settings.Shortcuts, _settings.ServiceEnabled);
        var icon = Path.Combine(exeDirectory, "Assets", "yifan.ico");
        _tray = new TrayIcon(hwnd, icon)
        {
            ServiceEnabled = _settings.ServiceEnabled,
            CropChord = _settings.Shortcuts.Crop,
            CropEnabled = ProviderCatalog.SupportsVision(_settings.Provider),
        };
        _tray.OpenMain += () => _main.ShowPage("home");
        _tray.Screenshot += RequestCrop;
        _tray.ToggleService += () => SetService(!_settings.ServiceEnabled);
        _tray.Quit += Quit;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShellPolicy.ShowEventName);
        _listener = new Thread(ListenForShow) { IsBackground = true, Name = "Yifan.Show" };
        _listener.Start();
        RefreshSurfaces();
        _ = RefreshQuotaAsync();
        if (!args.Any(arg => string.Equals(arg, ShellPolicy.TrayArgument, StringComparison.OrdinalIgnoreCase)))
            _main.ShowPage("home");
        return _main;
    }

    public HomeModel Home() => HomeCopy.Build(_settings, LastQuota);

    public void SetService(bool enabled)
    {
        _settings.ServiceEnabled = enabled;
        Persist();
        _hotkeys?.Apply(_settings.Shortcuts, _settings.ServiceEnabled);
        RefreshSurfaces();
        FileLogHolder.Current?.Info(enabled ? "翻译服务打开" : "翻译服务暂停");
    }

    public void SetStartup(bool enabled)
    {
        _settings.StartOnLogin = enabled;
        Persist();
        StartupRegistration.Apply(enabled, Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Yifan.exe"));
    }

    public string SaveForm(
        string provider,
        string key,
        string selected,
        string custom,
        string source,
        string target,
        IReadOnlyDictionary<string, string> keys,
        IReadOnlyDictionary<string, string> models,
        IReadOnlyDictionary<string, string> customs)
    {
        var next = _settings.Clone();
        foreach (var id in ProviderCatalog.Ids)
        {
            string? useKey;
            string useModel;
            string useCustom;
            if (id == provider)
            {
                useKey = key;
                useModel = selected;
                useCustom = custom;
            }
            else if (keys.ContainsKey(id) || customs.ContainsKey(id) || models.ContainsKey(id))
            {
                useKey = keys.TryGetValue(id, out var draftedKey) ? draftedKey : next.Providers[id].ApiKey;
                useModel = models.TryGetValue(id, out var draftedModel) ? draftedModel : next.Providers[id].Model;
                useCustom = customs.TryGetValue(id, out var draftedCustom) ? draftedCustom : "";
            }
            else
            {
                continue;
            }
            var choice = AppSettingsFactory.ResolveModelChoice(useModel, useCustom, id);
            if (!choice.Ok) return choice.Message;
            next = AppSettingsFactory.UpdateSlot(next, id, useKey, choice.Model, null);
        }
        next = AppSettingsFactory.Activate(next, provider);
        next.SourceLang = source;
        next.TargetLang = target;
        _settings = AppSettingsFactory.Normalize(next);
        Persist();
        _hotkeys?.Apply(_settings.Shortcuts, _settings.ServiceEnabled);
        _ = RefreshQuotaAsync();
        RefreshSurfaces();
        return "已保存。";
    }

    public async Task<string> TestConnectionAsync(string provider, string key, string selected, string custom)
    {
        var choice = AppSettingsFactory.ResolveModelChoice(selected, custom, provider);
        if (!choice.Ok) return choice.Message;
        if (string.IsNullOrWhiteSpace(key)) return TranslateException.MissingKey("settings").Message;
        try
        {
            await _client!.TestConnectionAsync(provider, key.Trim(), choice.Model, CancellationToken.None);
            await RefreshQuotaAsync();
            return "连接成功。";
        }
        catch (TranslateException error)
        {
            return error.Message;
        }
        catch (Exception)
        {
            return TranslateException.NetworkFailure("settings").Message;
        }
    }

    public string? AddGlossary(string source, string target)
    {
        if (!GlossaryBook.TryAdd(_settings.Glossary, source, target, out var next, out var error))
            return error;
        _settings.Glossary = next;
        Persist();
        RefreshSurfaces();
        return null;
    }

    public void RemoveGlossary(string id)
    {
        _settings.Glossary = GlossaryBook.Remove(_settings.Glossary, id);
        Persist();
        RefreshSurfaces();
    }

    public void RestoreHotkeys()
    {
        _settings.Shortcuts = DesktopShortcuts.Default();
        Persist();
        _hotkeys?.Apply(_settings.Shortcuts, _settings.ServiceEnabled);
        RefreshSurfaces();
    }

    public string AssignHotkey(string id, string chord)
    {
        var assigned = DesktopShortcuts.Assign(_settings.Shortcuts, id, chord);
        if (!assigned.Ok) return assigned.Message;
        _settings.Shortcuts = assigned.Shortcuts;
        Persist();
        _hotkeys?.Apply(_settings.Shortcuts, _settings.ServiceEnabled);
        RefreshSurfaces();
        return "已改为 " + (id == ShortcutIds.Crop ? _settings.Shortcuts.Crop : _settings.Shortcuts.Selection) + "。";
    }

    public Task<string?> CaptureHotkeyAsync()
    {
        _hotkeys?.Apply(_settings.Shortcuts, serviceEnabled: false);
        return RebindHook.CaptureAsync();
    }

    public void ReapplyHotkeys() => _hotkeys?.Apply(_settings.Shortcuts, _settings.ServiceEnabled);

    public void CancelRebind() => RebindHook.Cancel();

    public void RequestCrop()
    {
        var effect = HotkeyDispatch.Decide(_settings.ServiceEnabled, ShortcutIds.Crop, ProviderCatalog.SupportsVision(_settings.Provider));
        if (effect == HotkeyEffect.Ignore)
        {
            _main?.ShowPage("home");
            return;
        }
        if (effect == HotkeyEffect.RejectNoVision)
        {
            _main?.ShowPage("home");
            return;
        }
        _ = CropAsync();
    }

    private void OnHotkey(string action)
    {
        ClipboardWin.ReleaseModifiers(alt: true, ctrl: false, shift: false, meta: false);
        var vision = ProviderCatalog.SupportsVision(_settings.Provider);
        var effect = HotkeyDispatch.Decide(_settings.ServiceEnabled, action, vision);
        if (effect == HotkeyEffect.Ignore) return;
        if (action == ShortcutIds.Selection)
        {
            _ = TranslateSelectionAsync();
            return;
        }
        if (effect == HotkeyEffect.RejectNoVision)
        {
            _main?.ShowPage("home");
            return;
        }
        _ = CropAsync();
    }

    private async Task TranslateSelectionAsync()
    {
        var probe = await Task.Run(SelectionProbe.Read).ConfigureAwait(true);
        string? before = null;
        string? after = null;
        var attempted = false;
        if (!probe.Protected && SelectionText.Classify(probe.Text) == SelectionClass.Empty)
        {
            var bag = ClipboardWin.CaptureBag();
            var clipboard = new ClipboardWin();
            before = clipboard.ReadText();
            try
            {
                attempted = true;
                ClipboardWin.SendCopy();
                for (var i = 0; i < 4; i++)
                {
                    await Task.Delay(40).ConfigureAwait(true);
                    after = clipboard.ReadText();
                    if (!string.IsNullOrEmpty(after) && after != before) break;
                }
            }
            finally
            {
                ClipboardWin.RestoreBag(bag);
            }
        }
        var decision = SelectionAcquisition.Resolve(new AcquisitionInput(probe.Text, probe.Protected, before, after, attempted));
        (double, double, double, double)? anchor = probe.Left is double left && probe.Top is double top && probe.Right is double right && probe.Bottom is double bottom
            ? (left, top, right, bottom)
            : null;
        EnsureFloat();
        _anchor = anchor;
        if (!decision.Ready)
        {
            _activeText = "";
            _float!.Present(null, Pair(), anchor, "local");
            _float.ShowError(decision.Title, decision.Message, remote: false);
            _float.SelectTarget(_settings.TargetLang);
            FileLogHolder.Current?.Info("划词未发送请求");
            return;
        }
        _activeText = decision.Text;
        _float!.Present(decision.Text, Pair(), anchor, "translate");
        _float.SelectTarget(_settings.TargetLang);
        await RunTranslationAsync().ConfigureAwait(true);
    }

    private async Task RunTranslationAsync()
    {
        if (_float is null || _activeText.Length == 0 || _client is null) return;
        _translateCts?.Cancel();
        _translateCts = new CancellationTokenSource();
        var token = _translateCts.Token;
        _latestTranslation = "";
        _float.ShowTranslation("", streaming: true);
        try
        {
            await TranslationJob.RunAsync(
                _client,
                _settings,
                _activeText,
                _settings.SourceLang,
                _settings.TargetLang,
                text =>
                {
                    _latestTranslation = text;
                    _queue.TryEnqueue(() => _float?.ShowTranslation(text, streaming: true));
                },
                token).ConfigureAwait(true);
            _float?.ShowTranslation(_latestTranslation, streaming: false);
            _ = RefreshQuotaAsync();
        }
        catch (OperationCanceledException)
        {
            // 关掉浮层或改语言会取消上一次。
        }
        catch (TranslateException error)
        {
            var remote = !error.Message.Contains("5000", StringComparison.Ordinal);
            _float?.ShowError(error.Title, error.Message, remote);
        }
    }

    private void EnsureFloat()
    {
        if (_float is not null) return;
        _float = new SelectionFloatWindow();
        _float.CopyRequested += () =>
        {
            if (_latestTranslation.Trim().Length == 0) return;
            new ClipboardWin().SetText(_latestTranslation);
        };
        _float.RetryRequested += () => _ = RunTranslationAsync();
        _float.OpenSettingsRequested += () => _main?.ShowPage("settings");
        _float.TargetChanged += code =>
        {
            if (!LanguageList.IsTarget(code)) return;
            _settings.TargetLang = code;
            Persist();
            _float.SelectTarget(code);
            if (_activeText.Length > 0) _ = RunTranslationAsync();
        };
        _float.Dismissed += () =>
        {
            _translateCts?.Cancel();
            _float = null;
            _activeText = "";
            _latestTranslation = "";
        };
    }

    private async Task CropAsync()
    {
        _ocrCts?.Cancel();
        _ocr?.Close();
        _ocr = null;
        var pick = await CropOverlay.PickAsync().ConfigureAwait(true);
        if (pick.Cancelled || _quit) return;
        EnsureOcr();
        if (pick.DataUrl is null)
        {
            _ocrSource = "";
            _ocrTranslation = "";
            _ocr!.ShowResult(null, null, "识译失败", pick.Error ?? "截不到这块屏幕。不会假翻。", null, pick.Width, pick.Height);
            return;
        }
        _ocr!.ShowLoading();
        _ocrCts = new CancellationTokenSource();
        var token = _ocrCts.Token;
        try
        {
            var result = await OcrJob.RecognizeAndTranslateAsync(_client!, _settings, pick.DataUrl, token).ConfigureAwait(true);
            if (token.IsCancellationRequested || _ocr is null) return;
            _ocrSource = result.Source;
            _ocrTranslation = result.Translation;
            _ocr.ShowResult(result.Source, result.Translation, null, null, pick.DataUrl, pick.Width, pick.Height);
            _ = RefreshQuotaAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (OcrTranslateException error)
        {
            _ocrSource = error.RecognizedText;
            _ocrTranslation = "";
            _ocr?.ShowResult(error.RecognizedText, null, error.Title, error.Message, pick.DataUrl, pick.Width, pick.Height);
        }
        catch (TranslateException error)
        {
            _ocr?.ShowResult(null, null, error.Title, error.Message, pick.DataUrl, pick.Width, pick.Height);
        }
    }

    private void EnsureOcr()
    {
        if (_ocr is not null) return;
        _ocr = new OcrResultWindow();
        _ocr.CopySourceRequested += () =>
        {
            if (_ocrSource.Length > 0) new ClipboardWin().SetText(_ocrSource);
        };
        _ocr.CopyTranslationRequested += () =>
        {
            if (_ocrTranslation.Length > 0) new ClipboardWin().SetText(_ocrTranslation);
        };
        _ocr.Dismissed += () =>
        {
            _ocrCts?.Cancel();
            _ocr = null;
        };
    }

    private string Pair() => LanguageList.PairLabel(_settings.SourceLang, _settings.TargetLang, _activeText);

    private async Task RefreshQuotaAsync()
    {
        if (_client is null || _usage is null) return;
        try
        {
            var usage = _usage.Current.ByProvider[_settings.Provider];
            LastQuota = await QuotaMathLoad(_settings.Provider, _settings.ApiKey, usage).ConfigureAwait(true);
            RefreshSurfaces();
        }
        catch (Exception exception)
        {
            FileLogHolder.Current?.Error(exception, "额度查询失败");
        }
    }

    private Task<QuotaView> QuotaMathLoad(string provider, string apiKey, ProviderUsage usage)
    {
        return global::Yifan.Core.Quota.Quota.LoadViewAsync(provider, apiKey, usage, _http, CancellationToken.None);
    }

    private void RefreshSurfaces()
    {
        if (_main is null) return;
        _main.Home.Reload();
        if (_main.SettingsPage.Visibility == Visibility.Visible)
            _main.SettingsPage.Reload();
        if (_tray is not null)
        {
            _tray.ServiceEnabled = _settings.ServiceEnabled;
            _tray.CropChord = _settings.Shortcuts.Crop;
            _tray.CropEnabled = ProviderCatalog.SupportsVision(_settings.Provider);
            _tray.UpdateTip(_settings.ServiceEnabled ? "一翻 · 翻译服务已开启" : "一翻 · 翻译服务已暂停");
        }
    }

    private void Persist()
    {
        if (_settingsPath.Length == 0) return;
        SettingsStore.Save(_settingsPath, _settings);
    }

    private void ListenForShow()
    {
        while (!_quit && _showEvent is not null)
        {
            if (_showEvent.WaitOne(400) && !_quit)
                _queue.TryEnqueue(() => _main?.ShowPage("home"));
        }
    }

    private void Quit()
    {
        _quit = true;
        FileLogHolder.Current?.Info("一翻退出");
        _translateCts?.Cancel();
        _ocrCts?.Cancel();
        try { _showEvent?.Set(); } catch (Exception) { }
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _client?.Dispose();
        _http.Dispose();
        if (_main is not null)
        {
            _main.AllowExit();
            _main.Close();
        }
        Application.Current.Exit();
    }
}
