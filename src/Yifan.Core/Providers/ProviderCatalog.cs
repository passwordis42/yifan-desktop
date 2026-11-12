namespace Yifan.Core.Providers;

/// <summary>
/// 五家厂商。端点固定，没有自定义 Base URL，也不收云厂商 AK/SK。
/// 与 chrome-llm-translate v0.1.14 的 providers.ts 对齐。
/// </summary>
public static class ProviderCatalog
{
    public const string Qwen = "qwen";
    public const string OpenAI = "openai";
    public const string Volcano = "volcano";
    public const string DeepSeek = "deepseek";
    public const string Kimi = "kimi";

    public const string DefaultModel = "qwen3.7-plus";
    public const string DefaultVisionModel = "qwen3-vl-plus";
    public const string NoVisionOcrMessage = "当前厂商不支持截图识译";

    public static readonly string[] Ids = [Qwen, OpenAI, Volcano, DeepSeek, Kimi];

    public static readonly ModelTier[] QwenTiers =
    [
        new("fast", "快速便宜", ["qwen3.5-flash", "qwen3.7-flash", "qwen3.8-flash"]),
        new("balanced", "均衡", ["qwen3.5-plus", "qwen3.6-plus", "qwen3.7-plus"]),
        new("high", "能力高", ["qwen3.7-max", "qwen3.8-max"]),
    ];

    public static readonly IReadOnlyDictionary<string, ProviderProfile> All = new Dictionary<string, ProviderProfile>
    {
        [Qwen] = new(
            Qwen, "千问（百炼）", "本地存储 · 直连百炼",
            "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions",
            "CNY", DefaultModel, QwenTiers,
            "模型", "默认均衡 · qwen3.7-plus",
            "自定义 model id", "可选手填", "例如 qwen-plus（留空则用上方精选）",
            VisionMode.Dedicated, DisableThinking: true, IncludeStreamUsage: true,
            "聊天 Key 查不到账户余额，仅本地估算", Balance: null),
        [OpenAI] = new(
            OpenAI, "OpenAI", "本地存储 · api.openai.com",
            "https://api.openai.com/v1/chat/completions",
            "USD", "gpt-4o-mini",
            [new("curated", "精选", ["gpt-4o-mini", "gpt-4o", "gpt-4.1"])],
            "模型", "默认 · gpt-4o-mini",
            "自定义 model id", "可选手填", "例如 gpt-4o-mini（留空则用上方精选）",
            VisionMode.Chat, DisableThinking: false, IncludeStreamUsage: true,
            "普通 sk- 查不到账单余额，仅本地估算", Balance: null),
        [Volcano] = new(
            Volcano, "火山引擎", "本地存储 · 方舟兼容端点",
            "https://ark.cn-beijing.volces.com/api/v3/chat/completions",
            "CNY", "doubao-seed-1.6",
            [new("curated", "精选示例", ["doubao-seed-1.6", "doubao-1.5-pro-32k", "doubao-1.5-lite-32k"])],
            "模型 / 接入点", "可填 ep-… 接入点",
            "自定义 model / ep id", "手填优先", "例如 ep-2024xxxxxxxx（方舟接入点）",
            VisionMode.Chat, DisableThinking: false, IncludeStreamUsage: false,
            "方舟 Key 查不到套餐余额，仅本地估算（本版不收 AK/SK）", Balance: null),
        [DeepSeek] = new(
            DeepSeek, "DeepSeek", "本地存储 · api.deepseek.com",
            "https://api.deepseek.com/chat/completions",
            "CNY", "deepseek-chat",
            [new("curated", "精选", ["deepseek-chat", "deepseek-reasoner"])],
            "模型", "默认 · deepseek-chat",
            "自定义 model id", "可选手填", "例如 deepseek-chat（留空则用上方精选）",
            VisionMode.None, DisableThinking: false, IncludeStreamUsage: true,
            "余额查询失败时只做本地估算",
            new BalanceSpec(
                "https://api.deepseek.com/user/balance",
                "deepseek",
                "来自 DeepSeek /user/balance · 可用余额",
                "填写后查询 DeepSeek /user/balance · 可用余额",
                "账户剩余")),
        [Kimi] = new(
            Kimi, "Kimi", "本地存储 · api.moonshot.cn",
            "https://api.moonshot.cn/v1/chat/completions",
            "CNY", "moonshot-v1-8k",
            [new("curated", "精选", ["moonshot-v1-8k", "moonshot-v1-32k", "moonshot-v1-128k"])],
            "模型", "默认 · moonshot-v1-8k",
            "自定义 model id", "可选手填", "例如 moonshot-v1-8k（留空则用上方精选）",
            VisionMode.None, DisableThinking: false, IncludeStreamUsage: false,
            "余额查询失败时只做本地估算",
            new BalanceSpec(
                "https://api.moonshot.cn/v1/users/me/balance",
                "kimi",
                "来自 Kimi /v1/users/me/balance · available_balance",
                "填写后查询 Kimi /v1/users/me/balance · available_balance",
                "可用余额")),
    };

    public static bool IsProviderId(string? value) =>
        value is not null && All.ContainsKey(value);

    public static ProviderProfile Get(string? id) =>
        id is not null && All.TryGetValue(id, out var profile) ? profile : All[Qwen];

    public static bool IsCurated(string provider, string model) =>
        Get(provider).ModelTiers.Any(tier => tier.Models.Contains(model));

    public static bool SupportsVision(string? provider)
    {
        if (string.IsNullOrEmpty(provider)) return All[Qwen].Vision != VisionMode.None;
        if (!IsProviderId(provider)) return false;
        return All[provider].Vision != VisionMode.None;
    }

    /// <summary>识译要用的 model id。这家没有视觉模型时返回 null，调用方不许换一家。</summary>
    public static string? VisionCallModel(string? provider, string? model, string? visionModel, string? qwenSlotVision)
    {
        var id = IsProviderId(provider) ? provider! : Qwen;
        var mode = All[id].Vision;
        if (mode == VisionMode.None) return null;
        if (mode == VisionMode.Dedicated)
        {
            var mirror = (visionModel ?? "").Trim();
            var slot = (qwenSlotVision ?? "").Trim();
            if (mirror.Length > 0) return mirror;
            if (slot.Length > 0) return slot;
            return DefaultVisionModel;
        }
        var text = (model ?? "").Trim();
        return text.Length > 0 ? text : null;
    }
}

public enum VisionMode
{
    Dedicated,
    Chat,
    None,
}

public sealed record ModelTier(string Id, string Label, string[] Models);

public sealed record BalanceSpec(
    string Url,
    string Parser,
    string SourceNote,
    string PendingNote,
    string Headline);

public sealed record ProviderProfile(
    string Id,
    string Label,
    string KeyHint,
    string ChatCompletionsUrl,
    string Currency,
    string DefaultModel,
    ModelTier[] ModelTiers,
    string ModelLabel,
    string ModelHint,
    string CustomLabel,
    string CustomHint,
    string CustomPlaceholder,
    VisionMode Vision,
    bool DisableThinking,
    bool IncludeStreamUsage,
    string EstimateNote,
    BalanceSpec? Balance);
