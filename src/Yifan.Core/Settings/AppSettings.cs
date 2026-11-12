using System.Text.RegularExpressions;
using Yifan.Core.Glossary;
using Yifan.Core.Languages;
using Yifan.Core.Providers;
using Yifan.Core.Shortcuts;

namespace Yifan.Core.Settings;

public sealed class ProviderSlot
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public string VisionModel { get; set; } = "";

    public ProviderSlot Clone() => new()
    {
        ApiKey = ApiKey,
        Model = Model,
        VisionModel = VisionModel,
    };
}

public sealed class AppSettings
{
    public bool ServiceEnabled { get; set; } = true;
    public bool StartOnLogin { get; set; }
    public string Provider { get; set; } = ProviderCatalog.Qwen;
    public Dictionary<string, ProviderSlot> Providers { get; set; } = AppSettingsFactory.EmptyProviders();
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = ProviderCatalog.DefaultModel;
    public string VisionModel { get; set; } = ProviderCatalog.DefaultVisionModel;
    public string SourceLang { get; set; } = "auto";
    public string TargetLang { get; set; } = "zh";
    public DesktopShortcuts Shortcuts { get; set; } = DesktopShortcuts.Default();
    public List<GlossaryEntry> Glossary { get; set; } = [];

    public AppSettings Clone()
    {
        var copy = new AppSettings
        {
            ServiceEnabled = ServiceEnabled,
            StartOnLogin = StartOnLogin,
            Provider = Provider,
            ApiKey = ApiKey,
            Model = Model,
            VisionModel = VisionModel,
            SourceLang = SourceLang,
            TargetLang = TargetLang,
            Shortcuts = Shortcuts.Clone(),
            Glossary = Glossary.Select(entry => entry with { }).ToList(),
            Providers = [],
        };
        foreach (var pair in Providers)
            copy.Providers[pair.Key] = pair.Value.Clone();
        return copy;
    }
}

public readonly record struct ModelChoice(bool Ok, string Model, string Source, string Message);

public static class AppSettingsFactory
{
    public const string ModelIdPattern = @"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$";
    public const string InvalidQwenModel = "模型 id 无效。请改成百炼的 model id 后再试——不会假翻。";

    public static Dictionary<string, ProviderSlot> EmptyProviders() => new()
    {
        [ProviderCatalog.Qwen] = new() { ApiKey = "", Model = ProviderCatalog.All[ProviderCatalog.Qwen].DefaultModel, VisionModel = ProviderCatalog.DefaultVisionModel },
        [ProviderCatalog.OpenAI] = new() { ApiKey = "", Model = ProviderCatalog.All[ProviderCatalog.OpenAI].DefaultModel, VisionModel = "" },
        [ProviderCatalog.Volcano] = new() { ApiKey = "", Model = ProviderCatalog.All[ProviderCatalog.Volcano].DefaultModel, VisionModel = "" },
        [ProviderCatalog.DeepSeek] = new() { ApiKey = "", Model = ProviderCatalog.All[ProviderCatalog.DeepSeek].DefaultModel, VisionModel = "" },
        [ProviderCatalog.Kimi] = new() { ApiKey = "", Model = ProviderCatalog.All[ProviderCatalog.Kimi].DefaultModel, VisionModel = "" },
    };

    public static AppSettings Default()
    {
        var providers = EmptyProviders();
        return new AppSettings
        {
            ServiceEnabled = true,
            StartOnLogin = false,
            Provider = ProviderCatalog.Qwen,
            Providers = providers,
            ApiKey = "",
            Model = ProviderCatalog.DefaultModel,
            VisionModel = ProviderCatalog.DefaultVisionModel,
            SourceLang = "auto",
            TargetLang = "zh",
            Shortcuts = DesktopShortcuts.Default(),
            Glossary = [],
        };
    }

    public static bool IsModelId(string? value) =>
        value is not null && Regex.IsMatch(value, ModelIdPattern);

    public static string NormalizeProviderModel(string provider, string? value)
    {
        var fallback = ProviderCatalog.Get(provider).DefaultModel;
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var trimmed = value.Trim();
        return IsModelId(trimmed) ? trimmed : fallback;
    }

    public static string NormalizeVisionModel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ProviderCatalog.DefaultVisionModel;
        var trimmed = value.Trim();
        return IsModelId(trimmed) ? trimmed : ProviderCatalog.DefaultVisionModel;
    }

    public static ModelChoice ResolveModelChoice(string selected, string customRaw, string provider)
    {
        var profile = ProviderCatalog.Get(provider);
        var custom = customRaw.Trim();
        if (custom.Length > 0)
        {
            if (!IsModelId(custom))
            {
                var message = provider == ProviderCatalog.Qwen
                    ? InvalidQwenModel
                    : $"模型 id 无效。请改成{profile.Label}的 model id 后再试——不会假翻。";
                return new ModelChoice(false, "", "", message);
            }
            return new ModelChoice(true, custom, "custom", "");
        }
        if (ProviderCatalog.IsCurated(provider, selected))
            return new ModelChoice(true, selected, "curated", "");
        return new ModelChoice(true, profile.DefaultModel, "curated", "");
    }

    public static AppSettings UpdateSlot(AppSettings settings, string provider, string? apiKey, string? model, string? visionModel)
    {
        var providers = CloneProviders(settings.Providers);
        var current = providers[provider];
        providers[provider] = new ProviderSlot
        {
            ApiKey = apiKey is not null ? apiKey.Trim() : current.ApiKey,
            Model = model ?? current.Model,
            VisionModel = provider == ProviderCatalog.Qwen && visionModel is not null ? visionModel : current.VisionModel,
        };
        var next = settings.Clone();
        next.Providers = providers;
        next.Provider = settings.Provider;
        return Normalize(next);
    }

    public static AppSettings Activate(AppSettings settings, string provider)
    {
        var next = settings.Clone();
        next.Provider = provider;
        return Normalize(next);
    }

    public static AppSettings Normalize(AppSettings? raw)
    {
        var providers = EmptyProviders();
        if (raw?.Providers is not null)
        {
            foreach (var id in ProviderCatalog.Ids)
            {
                if (raw.Providers.TryGetValue(id, out var slot) && slot is not null)
                    providers[id] = NormalizeSlot(id, slot);
            }
        }
        var provider = ProviderCatalog.IsProviderId(raw?.Provider) ? raw!.Provider : ProviderCatalog.Qwen;
        var active = providers[provider];
        return new AppSettings
        {
            ServiceEnabled = raw?.ServiceEnabled ?? true,
            StartOnLogin = raw?.StartOnLogin ?? false,
            Provider = provider,
            Providers = providers,
            ApiKey = active.ApiKey,
            Model = active.Model,
            VisionModel = MirrorVision(provider, providers),
            SourceLang = Languages.Languages.IsSource(raw?.SourceLang) ? raw!.SourceLang : "auto",
            TargetLang = Languages.Languages.IsTarget(raw?.TargetLang) ? raw!.TargetLang : "zh",
            Shortcuts = DesktopShortcuts.Normalize(raw?.Shortcuts),
            Glossary = Glossary.Glossary.NormalizeEntries(raw?.Glossary),
        };
    }

    /// <summary>
    /// 手填优先写进当前槽。五家互不覆盖。模型无效时不改已保存的设置。
    /// </summary>
    public static (bool Ok, AppSettings Settings, string Message) CommitProviderForm(
        AppSettings settings,
        string provider,
        string apiKey,
        string selectedModel,
        string customModel,
        string sourceLang,
        string targetLang)
    {
        if (!ProviderCatalog.IsProviderId(provider))
            return (false, settings, "不认识的厂商。");
        var choice = ResolveModelChoice(selectedModel, customModel, provider);
        if (!choice.Ok) return (false, settings, choice.Message);
        var next = UpdateSlot(settings, provider, apiKey, choice.Model, visionModel: null);
        next = Activate(next, provider);
        next.SourceLang = Languages.Languages.IsSource(sourceLang) ? sourceLang : next.SourceLang;
        next.TargetLang = Languages.Languages.IsTarget(targetLang) ? targetLang : next.TargetLang;
        return (true, Normalize(next), "");
    }

    private static ProviderSlot NormalizeSlot(string id, ProviderSlot slot) => new()
    {
        ApiKey = (slot.ApiKey ?? "").Trim(),
        Model = NormalizeProviderModel(id, slot.Model),
        VisionModel = id == ProviderCatalog.Qwen ? NormalizeVisionModel(slot.VisionModel) : "",
    };

    private static string MirrorVision(string provider, Dictionary<string, ProviderSlot> providers)
    {
        var mode = ProviderCatalog.Get(provider).Vision;
        if (mode == VisionMode.Dedicated) return providers[ProviderCatalog.Qwen].VisionModel;
        if (mode == VisionMode.Chat) return providers[provider].Model;
        return "";
    }

    private static Dictionary<string, ProviderSlot> CloneProviders(Dictionary<string, ProviderSlot> source)
    {
        var copy = EmptyProviders();
        foreach (var id in ProviderCatalog.Ids)
        {
            if (source.TryGetValue(id, out var slot) && slot is not null)
                copy[id] = slot.Clone();
        }
        return copy;
    }
}
