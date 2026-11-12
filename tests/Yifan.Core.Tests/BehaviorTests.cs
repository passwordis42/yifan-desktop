using System.Net;
using Xunit;
using System.Text;
using System.Text.Json;
using GlossaryBook = Yifan.Core.Glossary.Glossary;
using OcrJob = Yifan.Core.Ocr.Ocr;
using QuotaMath = Yifan.Core.Quota.Quota;
using Yifan.Core.Chat;
using Yifan.Core.Desktop;
using Yifan.Core.Errors;
using Yifan.Core.Glossary;
using Yifan.Core.Languages;
using Yifan.Core.Layout;
using Yifan.Core.Logging;
using Yifan.Core.Ocr;
using Yifan.Core.Prompt;
using Yifan.Core.Providers;
using Yifan.Core.Quota;
using Yifan.Core.Selection;
using Yifan.Core.Settings;
using Yifan.Core.Shortcuts;
using Yifan.Core.Sse;
using Yifan.Core.Storage;

namespace Yifan.Core.Tests;

public class ProviderTests
{
    [Fact]
    public void Five_providers_have_fixed_endpoints_and_no_custom_base_url()
    {
        Assert.Equal(5, ProviderCatalog.Ids.Length);
        Assert.DoesNotContain("baseUrl", JsonSerializer.Serialize(ProviderCatalog.All.Keys));
        Assert.Contains("dashscope.aliyuncs.com", ProviderCatalog.All[ProviderCatalog.Qwen].ChatCompletionsUrl);
        Assert.Contains("api.openai.com", ProviderCatalog.All[ProviderCatalog.OpenAI].ChatCompletionsUrl);
        Assert.Contains("ark.cn-beijing.volces.com", ProviderCatalog.All[ProviderCatalog.Volcano].ChatCompletionsUrl);
        Assert.Contains("api.deepseek.com/chat/completions", ProviderCatalog.All[ProviderCatalog.DeepSeek].ChatCompletionsUrl);
        Assert.Contains("api.moonshot.cn", ProviderCatalog.All[ProviderCatalog.Kimi].ChatCompletionsUrl);
        Assert.Null(ProviderCatalog.All[ProviderCatalog.Qwen].Balance);
        Assert.NotNull(ProviderCatalog.All[ProviderCatalog.DeepSeek].Balance);
        Assert.NotNull(ProviderCatalog.All[ProviderCatalog.Kimi].Balance);
        Assert.Contains("不收 AK/SK", ProviderCatalog.All[ProviderCatalog.Volcano].EstimateNote);
    }

    [Theory]
    [InlineData("qwen", true)]
    [InlineData("openai", true)]
    [InlineData("volcano", true)]
    [InlineData("deepseek", false)]
    [InlineData("kimi", false)]
    public void Vision_support_does_not_switch_provider(string provider, bool vision)
    {
        Assert.Equal(vision, ProviderCatalog.SupportsVision(provider));
        if (!vision)
            Assert.Null(ProviderCatalog.VisionCallModel(provider, "deepseek-chat", "", ""));
    }

    [Fact]
    public void Qwen_vision_stays_on_the_dedicated_model()
    {
        Assert.Equal("qwen3-vl-plus", ProviderCatalog.VisionCallModel("qwen", "qwen3.7-plus", "", ""));
        Assert.Equal("qwen3-vl-max", ProviderCatalog.VisionCallModel("qwen", "qwen3.7-plus", "qwen3-vl-max", ""));
        Assert.Equal("gpt-4o-mini", ProviderCatalog.VisionCallModel("openai", "gpt-4o-mini", "qwen3-vl-plus", ""));
    }
}

public class SettingsTests
{
    [Fact]
    public void Slots_do_not_overwrite_each_other()
    {
        var settings = AppSettingsFactory.Default();
        settings = AppSettingsFactory.UpdateSlot(settings, ProviderCatalog.Qwen, "sk-qwen", "qwen3.7-plus", null);
        settings = AppSettingsFactory.UpdateSlot(settings, ProviderCatalog.DeepSeek, "sk-deep", "deepseek-chat", null);
        settings = AppSettingsFactory.Activate(settings, ProviderCatalog.DeepSeek);
        Assert.Equal("sk-deep", settings.ApiKey);
        Assert.Equal("deepseek-chat", settings.Model);
        Assert.Equal("", settings.VisionModel);
        Assert.Equal("sk-qwen", settings.Providers[ProviderCatalog.Qwen].ApiKey);
        settings = AppSettingsFactory.UpdateSlot(settings, ProviderCatalog.DeepSeek, "sk-deep-2", "deepseek-reasoner", "should-ignore");
        Assert.Equal("sk-qwen", settings.Providers[ProviderCatalog.Qwen].ApiKey);
        Assert.Equal("", settings.Providers[ProviderCatalog.DeepSeek].VisionModel);
        Assert.Equal("deepseek-reasoner", settings.Providers[ProviderCatalog.DeepSeek].Model);
    }

    [Fact]
    public void Legacy_record_lands_in_the_qwen_slot()
    {
        var json = """{"apiKey":"sk-old","model":"qwen-plus","sourceLang":"en","targetLang":"ja"}""";
        var settings = SettingsStore.Parse(json);
        Assert.Equal(ProviderCatalog.Qwen, settings.Provider);
        Assert.Equal("sk-old", settings.Providers[ProviderCatalog.Qwen].ApiKey);
        Assert.Equal("qwen-plus", settings.Model);
        Assert.Equal("en", settings.SourceLang);
        Assert.Equal("ja", settings.TargetLang);
        Assert.Equal("Alt+E", settings.Shortcuts.Selection);
        Assert.True(settings.ServiceEnabled);
        Assert.False(settings.StartOnLogin);
    }

    [Fact]
    public void Invalid_custom_model_refuses_to_save()
    {
        var settings = AppSettingsFactory.Default();
        var result = AppSettingsFactory.CommitProviderForm(settings, "qwen", "sk-1", "qwen3.7-plus", "not a model", "auto", "zh");
        Assert.False(result.Ok);
        Assert.Contains("不会假翻", result.Message);
        Assert.Equal("", settings.ApiKey);
    }

    [Fact]
    public void Volcano_accepts_endpoint_id()
    {
        var choice = AppSettingsFactory.ResolveModelChoice("doubao-seed-1.6", "ep-20240101abcd", ProviderCatalog.Volcano);
        Assert.True(choice.Ok);
        Assert.Equal("ep-20240101abcd", choice.Model);
        Assert.Equal("custom", choice.Source);
    }

    [Fact]
    public void Round_trip_keeps_keys_out_of_the_exe_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "yifan-tests", Guid.NewGuid().ToString("n"));
        var exe = Path.Combine(root, "app");
        var appData = Path.Combine(root, "appdata");
        Directory.CreateDirectory(exe);
        var path = AppPaths.RequireSettingsPath(appData, exe);
        Assert.False(AppPaths.IsUnder(path, exe));
        var settings = AppSettingsFactory.UpdateSlot(AppSettingsFactory.Default(), "openai", "sk-openai", "gpt-4o", null);
        settings = AppSettingsFactory.Activate(settings, "openai");
        settings.Glossary = [new GlossaryEntry("1", "LLM", "大语言模型")];
        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path);
        Assert.Equal("sk-openai", loaded.Providers["openai"].ApiKey);
        Assert.Equal("sk-openai", loaded.ApiKey);
        Assert.Equal("", loaded.Providers["qwen"].ApiKey);
        Assert.Equal("大语言模型", loaded.Glossary[0].Target);
        Assert.Contains("sk-openai", File.ReadAllText(path));
        Assert.Throws<InvalidOperationException>(() => AppPaths.RequireSettingsPath(null, exe));
    }
}

public class GlossaryTests
{
    [Fact]
    public void Longer_term_wins_and_latin_words_do_not_match_inside_words()
    {
        var entries = new List<GlossaryEntry>
        {
            new("1", "key", "键"),
            new("2", "keyboard", "键盘"),
            new("3", "transformer", "变换器（模型）"),
        };
        var applied = GlossaryBook.Apply("Press the keyboard key.", entries);
        Assert.Contains("[[YF0]]", applied.Text);
        Assert.Contains("[[YF1]]", applied.Text);
        Assert.DoesNotContain("keyboard", applied.Text, StringComparison.OrdinalIgnoreCase);
        var restored = GlossaryBook.Restore("按下 [[YF0]] 的 [[YF1]]", applied.Tokens);
        Assert.Equal("按下 键盘 的 键", restored);
    }

    [Fact]
    public void Streaming_hides_an_unfinished_placeholder()
    {
        var tokens = new[] { new GlossaryToken("[[YF0]]", "LLM", "大语言模型") };
        Assert.Equal("一个 ", GlossaryBook.Present("一个 [[YF", tokens, false));
        Assert.Equal("一个 大语言模型", GlossaryBook.Present("一个 [[YF0]]", tokens, true));
    }

    [Fact]
    public void Fixed_translation_is_in_the_prompt()
    {
        var applied = GlossaryBook.Apply("LLM matters", [new("1", "LLM", "大语言模型")]);
        var prompt = PromptBuilder.BuildSystem("auto", "zh", applied.Tokens);
        Assert.Contains("[[YF0]]", prompt);
        Assert.Contains("大语言模型", prompt);
        Assert.Contains("固定译法优先", prompt);
        Assert.Contains("先判断源语言", prompt);
    }
}

public class QuotaTests
{
    [Fact]
    public void Estimate_is_labeled_and_uses_the_price_table()
    {
        var usage = new TokenUsage(1000, 500);
        var cost = Pricing.EstimateCost("qwen", "qwen3.7-plus", usage);
        Assert.Equal(0.0018, cost, 6);
        Assert.Equal("¥0.0018", Pricing.FormatMoney(cost, "CNY"));
        Assert.Equal("$1.28", Pricing.FormatMoney(1.28, "USD"));
        var view = QuotaMath.EstimateView("qwen", new ProviderUsage { LastCost = 0.02, CumulativeCost = 1.28, CallsWithUsage = 2 });
        Assert.Equal("估算", view.Badge);
        Assert.Contains("本次约 ¥0.02", view.Main);
        Assert.Contains("本机累计估算 ¥1.28", view.Main);
        Assert.Contains("仅本地估算", view.Sub);
    }

    [Fact]
    public void Missing_usage_does_not_invent_a_price()
    {
        var next = QuotaMath.NextUsage("openai", "gpt-4o-mini", ProviderUsage.Empty(), null);
        Assert.Null(next.LastCost);
        Assert.Equal(0, next.CumulativeCost);
        Assert.Equal(1, next.CallsWithoutUsage);
        var view = QuotaMath.EstimateView("openai", next);
        Assert.Contains("未估金额", view.Main);
        Assert.Contains("没有 usage", view.Sub);
    }

    [Fact]
    public void Deepseek_and_kimi_balances_parse_and_fall_back()
    {
        using var deep = JsonDocument.Parse("""{"balance_infos":[{"currency":"USD","total_balance":"1"},{"currency":"CNY","total_balance":"12.40"}]}""");
        var parsed = QuotaMath.ParseDeepseekBalance(deep.RootElement);
        Assert.Equal(12.40, parsed!.Value.Amount);
        Assert.Equal("CNY", parsed.Value.Currency);
        using var kimi = JsonDocument.Parse("""{"code":0,"data":{"available_balance":"3.5"}}""");
        Assert.Equal(3.5, QuotaMath.ParseKimiBalance(kimi.RootElement));
        using var bad = JsonDocument.Parse("""{"code":1,"data":{"available_balance":"3.5"}}""");
        Assert.Null(QuotaMath.ParseKimiBalance(bad.RootElement));
    }

    [Fact]
    public async Task Balance_failure_becomes_an_honest_estimate()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler);
        var view = await QuotaMath.LoadViewAsync("deepseek", "sk-x", ProviderUsage.Empty(), http, CancellationToken.None);
        Assert.Equal("估算", view.Badge);
        Assert.Contains("已改为本地估算", view.Sub);
        Assert.Contains("Key 无效", view.Sub);
    }
}

public class ErrorTests
{
    [Theory]
    [InlineData(401, "float")]
    [InlineData(402, "ocr")]
    [InlineData(429, "settings")]
    public void Http_failures_are_chinese_and_not_fake(int status, string where)
    {
        var error = TranslateException.HttpFailure(where, status, """{"error":{"message":"insufficient"}}""", "sk-secret123");
        Assert.Contains("不会假翻", error.Message);
        Assert.DoesNotContain("sk-secret123", error.Message);
        if (status == 402) Assert.Equal(TranslateErrorCodes.Quota, error.Code);
    }

    [Fact]
    public void Keys_are_scrubbed_from_logs()
    {
        var text = LogRedactor.Redact("boom sk-abcdefghijklmnopqrstuvwxyz Bearer abcdefghijklmnop");
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", text);
        Assert.Contains("sk-***", text);
        Assert.Contains("Bearer ***", text);
    }
}

public class SseAndChatTests
{
    [Fact]
    public async Task Stream_parser_reads_deltas_and_usage()
    {
        var payload = "data: {\"choices\":[{\"delta\":{\"content\":\"你\"}}]}\n\n" +
                      "data: {\"choices\":[{\"delta\":{\"content\":\"好\"}}],\"usage\":{\"prompt_tokens\":3,\"completion_tokens\":2}}\n\n" +
                      "data: [DONE]\n\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        var text = "";
        var (received, usage) = await SseParser.ReadStreamAsync(stream, chunk => text += chunk, CancellationToken.None);
        Assert.True(received);
        Assert.Equal("你好", text);
        Assert.Equal(3, usage!.PromptTokens);
        Assert.Equal(2, usage.CompletionTokens);
    }

    [Fact]
    public void Chat_body_matches_each_provider()
    {
        var qwen = JsonDocument.Parse(ChatBodies.BuildChatJson("qwen3.7-plus", "sys", "hi", true, null, "qwen"));
        Assert.False(qwen.RootElement.GetProperty("enable_thinking").GetBoolean());
        Assert.True(qwen.RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
        Assert.Equal(0.2, qwen.RootElement.GetProperty("temperature").GetDouble());

        var reasoner = JsonDocument.Parse(ChatBodies.BuildChatJson("deepseek-reasoner", null, "hi", true, 8, "deepseek"));
        Assert.False(reasoner.RootElement.TryGetProperty("temperature", out _));
        Assert.False(reasoner.RootElement.TryGetProperty("max_tokens", out _));
        Assert.False(reasoner.RootElement.TryGetProperty("enable_thinking", out _));

        var volcano = JsonDocument.Parse(ChatBodies.BuildChatJson("ep-1", null, "hi", true, null, "volcano"));
        Assert.False(volcano.RootElement.TryGetProperty("stream_options", out _));

        var vision = JsonDocument.Parse(ChatBodies.BuildVisionJson("qwen3-vl-plus", "看图", "data:image/png;base64,aaaa", "qwen"));
        Assert.False(vision.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal("image_url", vision.RootElement.GetProperty("messages")[0].GetProperty("content")[1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Over_limit_and_missing_key_do_not_call_the_network()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("should not send"));
        var ledger = new MemoryUsageLedger();
        using var client = new YifanClient(handler, ledger);
        var settings = AppSettingsFactory.Default();
        await Assert.ThrowsAsync<TranslateException>(() =>
            TranslationJob.RunAsync(client, settings, new string('a', 5001), null, null, _ => { }, CancellationToken.None));
        settings = AppSettingsFactory.UpdateSlot(settings, "qwen", "", "qwen3.7-plus", null);
        await Assert.ThrowsAsync<TranslateException>(() =>
            TranslationJob.RunAsync(client, settings, "hello", null, null, _ => { }, CancellationToken.None));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task Glossary_hit_is_restored_from_the_stream()
    {
        var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"[[YF0]] 很重要\"}}]}\n\ndata: [DONE]\n\n";
        var handler = new StubHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(body);
            var system = doc.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
            var user = doc.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
            Assert.Contains("[[YF0]]", system);
            Assert.Contains("大语言模型", system);
            Assert.Contains("[[YF0]]", user);
            Assert.Contains("dashscope.aliyuncs.com", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
            };
        });
        var settings = AppSettingsFactory.UpdateSlot(AppSettingsFactory.Default(), "qwen", "sk-testkey", "qwen3.7-plus", null);
        settings.Glossary = [new GlossaryEntry("1", "LLM", "大语言模型")];
        using var client = new YifanClient(handler, new MemoryUsageLedger());
        var shown = "";
        await TranslationJob.RunAsync(client, settings, "LLM matters", null, null, text => shown = text, CancellationToken.None);
        Assert.Equal("大语言模型 很重要", shown);
        Assert.Equal(1, client.Usage.Current.ByProvider["qwen"].CallsWithoutUsage + client.Usage.Current.ByProvider["qwen"].CallsWithUsage);
    }

    [Fact]
    public async Task Unauthorized_is_a_chinese_error()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":{"message":"invalid api key sk-testkey"}}"""),
        });
        var settings = AppSettingsFactory.UpdateSlot(AppSettingsFactory.Default(), "openai", "sk-testkey", "gpt-4o-mini", null);
        settings = AppSettingsFactory.Activate(settings, "openai");
        using var client = new YifanClient(handler, new MemoryUsageLedger());
        var error = await Assert.ThrowsAsync<TranslateException>(() =>
            TranslationJob.RunAsync(client, settings, "hello", null, null, _ => { }, CancellationToken.None));
        Assert.Contains("不会假翻", error.Message);
        Assert.DoesNotContain("sk-testkey", error.Message);
    }
}

public class OcrTests
{
    [Fact]
    public void Empty_and_no_text_are_not_translations()
    {
        Assert.False(OcrJob.ParseOcrText("").Ok);
        Assert.Equal("no_text", OcrJob.ParseOcrText("无文字").Reason);
        Assert.Equal("hello", OcrJob.ParseOcrText("```text\nhello\n```").Text);
    }

    [Fact]
    public void Crop_math_clamps_inside_the_image()
    {
        var rect = OcrJob.NormalizeRect(10, 20, 110, 80, 200, 100);
        var pixels = OcrJob.ToPixels(rect, 400, 200, 200, 100);
        Assert.Equal(20, pixels!.Value.Sx);
        Assert.Equal(40, pixels.Value.Sy);
        Assert.False(OcrJob.IsLargeEnough(new OcrJob.CssRect(0, 0, 4, 4)));
        Assert.False(OcrJob.IsImageDataUrl("data:text/plain;base64,aaaa"));
    }

    [Fact]
    public async Task Provider_without_vision_is_not_swapped()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("should not send"));
        var settings = AppSettingsFactory.Activate(AppSettingsFactory.Default(), "deepseek");
        settings = AppSettingsFactory.UpdateSlot(settings, "deepseek", "sk-deep", "deepseek-chat", null);
        settings = AppSettingsFactory.Activate(settings, "deepseek");
        using var client = new YifanClient(handler, new MemoryUsageLedger());
        var error = await Assert.ThrowsAsync<TranslateException>(() =>
            OcrJob.RecognizeAndTranslateAsync(client, settings, "data:image/png;base64," + new string('a', 40), CancellationToken.None));
        Assert.Contains(ProviderCatalog.NoVisionOcrMessage, error.Message);
        Assert.Equal(0, handler.Count);
        Assert.Equal("qwen", settings.Providers["qwen"].Model.Length > 0 ? "qwen" : "");
        Assert.Equal("deepseek", settings.Provider);
    }

    [Fact]
    public void Loading_disables_chrome_and_result_enables_it()
    {
        Assert.False(OcrPanel.Chrome("loading").Drag);
        Assert.False(OcrPanel.Chrome("loading").Resize);
        Assert.False(OcrPanel.Chrome("loading").Font);
        Assert.False(OcrPanel.Chrome("loading").Enlarge);
        var ready = OcrPanel.Chrome("ready");
        Assert.True(ready.Drag && ready.Resize && ready.Font && ready.Enlarge);
    }

    [Fact]
    public void Ocr_window_font_and_enlarge_are_not_tied_and_reset_each_open()
    {
        var viewport = new ViewportSize(1280, 800);
        var session = new OcrPanelSession();
        var opened = session.Open(viewport, 360);
        Assert.Equal(13, opened.FontPx);
        Assert.False(opened.Enlarged);
        Assert.InRange(opened.Box.Width, 280, 400);
        Assert.True(opened.Box.Left + opened.Box.Width <= 1280 - 16 + 1);
        var larger = OcrPanel.StepFont(opened, 1);
        Assert.Equal(14, larger.FontPx);
        var enlarged = OcrPanel.ToggleEnlarge(larger, viewport);
        Assert.True(enlarged.Enlarged);
        Assert.Equal(14, enlarged.FontPx);
        Assert.InRange(enlarged.Box.Width, 540, 560);
        var restored = OcrPanel.ToggleEnlarge(enlarged, viewport);
        Assert.False(restored.Enlarged);
        Assert.Equal(opened.SessionDefault.Width, restored.Box.Width);
        Assert.Equal(14, restored.FontPx);
        session.Close();
        var again = session.Open(viewport, 360);
        Assert.False(again.Enlarged);
        Assert.Equal(13, again.FontPx);
    }

    [Fact]
    public void Resize_respects_minimum_and_drag_stays_on_screen()
    {
        var viewport = new ViewportSize(800, 600);
        var layout = OcrPanel.Create(viewport, 300);
        var resized = OcrPanel.Resize(layout, "se", -5000, -5000, viewport);
        Assert.True(resized.Box.Width >= Math.Min(OcrPanel.MinWidth, viewport.Width));
        Assert.True(resized.Box.Height >= Math.Min(OcrPanel.MinHeight, viewport.Height));
        var dragged = OcrPanel.Drag(layout, -80, -60, viewport);
        Assert.True(dragged.Box.Left >= OcrPanel.Margin - 0.1);
        Assert.True(dragged.Box.Top >= OcrPanel.Margin - 0.1);
        Assert.True(dragged.Box.Left + dragged.Box.Width <= viewport.Width - OcrPanel.Margin + 1);
        Assert.True(dragged.Moved);
        var shoved = OcrPanel.Drag(dragged, 5000, 5000, viewport);
        Assert.True(shoved.Box.Left + shoved.Box.Width <= viewport.Width - OcrPanel.Margin + 1);
        Assert.True(shoved.Box.Top + shoved.Box.Height <= viewport.Height - OcrPanel.Margin + 1);
    }
}

public class SelectionFloatTests
{
    [Fact]
    public void Short_text_follows_content_and_long_text_caps()
    {
        var viewport = new ViewportSize(1280, 800);
        var max = SelectionFloat.MaxHeight(viewport.Height);
        Assert.Equal(800 - 48, max);
        var shortHeight = SelectionFloat.Height(220, viewport.Height, false);
        Assert.Equal(220, shortHeight.Height);
        Assert.False(shortHeight.Scrolls);
        var longHeight = SelectionFloat.Height(2000, viewport.Height, false);
        Assert.Equal(max, longHeight.Height);
        Assert.True(longHeight.Scrolls);
        Assert.Equal(380, SelectionFloat.Width(1280, false));
        Assert.Equal(640, SelectionFloat.Width(1280, true));
        Assert.Equal(max, SelectionFloat.Height(100, viewport.Height, true).Height);
    }

    [Fact]
    public void Drag_does_not_snap_back_and_enlarge_does_not_change_the_session_memory()
    {
        var viewport = new ViewportSize(1280, 800);
        var session = new SelectionFloatSession();
        var anchor = (40d, 80d);
        var opened = session.Commit(SelectionFloat.Place(session.Open(anchor, 200), anchor, viewport));
        Assert.False(opened.Enlarged);
        Assert.False(opened.Moved);
        var dragged = SelectionFloat.Drag(opened, 120, 30, viewport);
        Assert.True(dragged.Moved);
        var grown = SelectionFloat.NoteContent(dragged, 500, viewport);
        Assert.Equal(dragged.Left, grown.Left);
        Assert.Equal(dragged.Top, grown.Top);
        var enlarged = SelectionFloat.ToggleEnlarge(grown, viewport, anchor);
        Assert.True(enlarged.Enlarged);
        Assert.Equal(SelectionFloat.SourceFontPx, 13);
        Assert.Equal(SelectionFloat.TranslationFontPx, 15);
        Assert.False(SelectionFloatChrome.Resize);
        Assert.False(SelectionFloatChrome.FontStep);
        session.Close();
        Assert.Null(session.Current);
        var reopened = session.Open(anchor, 180);
        Assert.False(reopened.Enlarged);
        Assert.False(reopened.Moved);
    }

    [Fact]
    public void Release_stays_inside_a_visible_work_area()
    {
        var areas = new List<ScreenRect>
        {
            new(0, 0, 800, 600),
            new(800, 0, 800, 600),
        };
        var parked = ScreenClamp.PlaceOnScreen(new ScreenRect(1500, 700, 380, 200), areas, 8);
        Assert.True(parked.X >= 800 + 8);
        Assert.True(parked.Y + parked.Height <= 600 - 8 + 0.1);
        Assert.True(parked.X + parked.Width <= 1600 - 8 + 0.1);
    }
}

public class HotkeyTests
{
    [Fact]
    public void Defaults_restore_and_conflicts_keep_the_old_chord()
    {
        var shortcuts = DesktopShortcuts.Default();
        Assert.Equal("Alt+E", shortcuts.Selection);
        Assert.Equal("Alt+S", shortcuts.Crop);
        var conflict = DesktopShortcuts.Assign(shortcuts, ShortcutIds.Selection, "Alt+S");
        Assert.False(conflict.Ok);
        Assert.Equal("Alt+E", conflict.Shortcuts.Selection);
        Assert.Contains("截图识译", conflict.Message);
        var changed = DesktopShortcuts.Assign(shortcuts, ShortcutIds.Selection, "Ctrl+Shift+E");
        Assert.True(changed.Ok);
        Assert.Equal("Ctrl+Shift+E", changed.Shortcuts.Selection);
        Assert.Equal("Alt+E", DesktopShortcuts.Default().Selection);
        var broken = DesktopShortcuts.Normalize(new DesktopShortcuts { Selection = "nope", Crop = "Alt+E" });
        Assert.NotEqual(broken.Selection, broken.Crop);
        Assert.DoesNotContain("未设置", broken.Selection);
    }

    [Fact]
    public void Win32_mapping_and_rebind_cancel()
    {
        var mapped = Hotkeys.ToWin32("Alt+E");
        Assert.Equal(Hotkeys.ModAlt | Hotkeys.ModNoRepeat, mapped!.Value.Modifiers);
        Assert.Equal((uint)'E', mapped.Value.VirtualKey);
        var snapshot = Hotkeys.FromVirtualKey((uint)'S', alt: true, ctrl: false, shift: false, meta: false, repeat: false);
        Assert.Equal(ShortcutIds.Crop, Hotkeys.Match(snapshot!, DesktopShortcuts.Default()));
        var escape = Hotkeys.FromVirtualKey(0x1B, false, false, false, false, false);
        Assert.Equal(RebindKind.Cancel, Hotkeys.ClassifyRebind(escape!));
    }

    [Fact]
    public void Master_switch_blocks_both_hotkeys_without_forgetting_keys()
    {
        var settings = AppSettingsFactory.Default();
        settings.Shortcuts = DesktopShortcuts.Assign(settings.Shortcuts, ShortcutIds.Selection, "Ctrl+E").Shortcuts;
        settings.ServiceEnabled = false;
        Assert.Equal(HotkeyEffect.Ignore, HotkeyDispatch.Decide(settings.ServiceEnabled, ShortcutIds.Selection, true));
        Assert.Equal(HotkeyEffect.Ignore, HotkeyDispatch.Decide(settings.ServiceEnabled, ShortcutIds.Crop, true));
        Assert.Equal("Ctrl+E", settings.Shortcuts.Selection);
        settings.ServiceEnabled = true;
        Assert.Equal(HotkeyEffect.Run, HotkeyDispatch.Decide(true, ShortcutIds.Selection, true));
        Assert.Equal(HotkeyEffect.RejectNoVision, HotkeyDispatch.Decide(true, ShortcutIds.Crop, false));
        var home = HomeCopy.Build(settings, null);
        Assert.True(HomeCopy.ShowsRealHotkeys(home));
        settings.ServiceEnabled = false;
        var paused = HomeCopy.Build(settings, QuotaMath.EstimateView("qwen", ProviderUsage.Empty()));
        Assert.Contains("已暂停", paused.Selection.Body);
        Assert.Equal("Ctrl+E", paused.SelectionChord);
        Assert.Contains("设置保留", paused.ProviderLine);
        Assert.DoesNotContain("未设置", paused.ServiceBody);
    }
}

public class AcquisitionTests
{
    [Fact]
    public void Empty_selection_and_clipboard_are_honest()
    {
        var result = SelectionAcquisition.Resolve(new AcquisitionInput(null, false, "", "", true));
        Assert.False(result.Ready);
        Assert.Equal("没有选中文字，也读不到剪贴板。", result.Message);
        Assert.Equal("未能取词", result.Title);
    }

    [Fact]
    public void Over_5000_does_not_become_a_translation()
    {
        var result = SelectionAcquisition.Resolve(new AcquisitionInput(new string('a', 5001), false, "short", "short", false));
        Assert.False(result.Ready);
        Assert.Equal(SelectionAcquisition.TooLongTitle, result.Title);
        Assert.Contains("5000", result.Message);
    }

    [Fact]
    public void Protected_window_does_not_fall_through_to_the_clipboard()
    {
        var result = SelectionAcquisition.Resolve(new AcquisitionInput("", true, "secret clipboard", "secret clipboard", false));
        Assert.False(result.Ready);
        Assert.Contains("受保护", result.Message);
        Assert.Contains("不会假翻", result.Message);
    }

    [Fact]
    public async Task Simulated_copy_is_preferred_and_original_clipboard_is_restored()
    {
        var result = SelectionAcquisition.Resolve(new AcquisitionInput("", false, "old", "selected sentence", true));
        Assert.True(result.Ready);
        Assert.Equal(SelectionOrigin.SimulatedCopy, result.Origin);
        Assert.Equal("selected sentence", result.Text);
        var clipboard = new FakeClipboard("old");
        var seen = await ClipboardRestore.WithRestoreAsync(clipboard, async () =>
        {
            clipboard.SetText("selected sentence");
            await Task.Yield();
            return clipboard.ReadText();
        });
        Assert.Equal("selected sentence", seen);
        Assert.Equal("old", clipboard.ReadText());
    }

    [Fact]
    public void Foreground_selection_wins_over_clipboard()
    {
        var result = SelectionAcquisition.Resolve(new AcquisitionInput("from the window", false, "clipboard", "clipboard", false));
        Assert.Equal(SelectionOrigin.Foreground, result.Origin);
        Assert.Equal("from the window", result.Text);
    }
}

public class ScopeTests
{
    [Fact]
    public void Out_of_scope_flags_stay_off_and_settings_have_no_those_fields()
    {
        Assert.False(ProductScope.FullPageTranslation);
        Assert.False(ProductScope.ContextMenus);
        Assert.False(ProductScope.ForceSelect);
        Assert.False(ProductScope.CornerGestures);
        Assert.False(ProductScope.CustomBaseUrl);
        Assert.False(ProductScope.CloudAkSk);
        Assert.False(ProductScope.TranslationHistory);
        Assert.False(ProductScope.ExtensionSync);
        Assert.False(ProductScope.AutoUpdateInstall);
        Assert.False(ProductScope.SelectionFloatResize);
        Assert.Equal(CloseAction.HideToTray, ShellPolicy.MainWindowClose);
        var names = typeof(AppSettings).GetProperties().Select(property => property.Name).ToHashSet();
        Assert.DoesNotContain("BaseUrl", names);
        Assert.DoesNotContain("History", names);
        Assert.DoesNotContain("GestureEnabled", names);
        Assert.DoesNotContain("ForceSelectEnabled", names);
        Assert.Contains("Alt+E", HomeCopy.Build(AppSettingsFactory.Default(), null).SelectionChord);
        Assert.Contains("Alt+S", HomeCopy.Build(AppSettingsFactory.Default(), null).CropChord);
    }

    [Fact]
    public void Language_pair_and_guess()
    {
        Assert.Equal("自动检测 → 中文", Languages.Languages.HomePair("auto", "zh"));
        Assert.Equal("EN → 中文", Languages.Languages.PairLabel("en", "zh"));
        Assert.Equal("ja", Languages.Languages.GuessSourceCode("これはテストです"));
        Assert.Equal("en", Languages.Languages.GuessSourceCode("Hello there"));
    }

    [Fact]
    public void Log_file_is_written_under_app_data()
    {
        var root = Path.Combine(Path.GetTempPath(), "yifan-log", Guid.NewGuid().ToString("n"));
        var log = new FileLog(AppPaths.LogPath(root));
        log.Error(new InvalidOperationException("sk-supersecretvalue"), "failed");
        var text = File.ReadAllText(AppPaths.LogPath(root));
        Assert.Contains("ERROR", text);
        Assert.DoesNotContain("supersecretvalue", text);
        Assert.False(AppPaths.IsUnder(AppPaths.LogPath(root), Path.Combine(root, "exe")));
    }
}

file sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public int Count { get; private set; }
    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Count++;
        return Task.FromResult(_responder(request));
    }
}

file sealed class FakeClipboard : IClipboardGateway
{
    private string _text;
    public FakeClipboard(string text) => _text = text;
    public string? ReadText() => _text;
    public void SetText(string text) => _text = text;
}
