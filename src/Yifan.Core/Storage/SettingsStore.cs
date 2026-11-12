using System.Text.Json;
using System.Text.Json.Nodes;
using Yifan.Core.Glossary;
using Yifan.Core.Providers;
using Yifan.Core.Quota;
using Yifan.Core.Settings;
using Yifan.Core.Shortcuts;
using Yifan.Core.Sse;

namespace Yifan.Core.Storage;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) return AppSettingsFactory.Default();
        try
        {
            var text = File.ReadAllText(path);
            return Parse(text);
        }
        catch (Exception)
        {
            return AppSettingsFactory.Default();
        }
    }

    public static void Save(string path, AppSettings settings)
    {
        var normalized = AppSettingsFactory.Normalize(settings);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var json = Serialize(normalized);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    public static AppSettings Parse(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return AppSettingsFactory.Default();
        }
        if (node is not JsonObject obj) return AppSettingsFactory.Default();
        var settings = AppSettingsFactory.Default();
        settings.ServiceEnabled = ReadBool(obj, "serviceEnabled", true);
        settings.StartOnLogin = ReadBool(obj, "startOnLogin", false);
        settings.SourceLang = ReadString(obj, "sourceLang") ?? "auto";
        settings.TargetLang = ReadString(obj, "targetLang") ?? "zh";
        settings.Provider = ReadString(obj, "provider") ?? ProviderCatalog.Qwen;

        var providers = AppSettingsFactory.EmptyProviders();
        var hasMap = obj["providers"] is JsonObject map && ProviderCatalog.Ids.Any(id => map[id] is JsonObject);
        if (hasMap && obj["providers"] is JsonObject providerMap)
        {
            foreach (var id in ProviderCatalog.Ids)
            {
                if (providerMap[id] is JsonObject slot)
                {
                    providers[id] = new ProviderSlot
                    {
                        ApiKey = ReadString(slot, "apiKey") ?? "",
                        Model = ReadString(slot, "model") ?? providers[id].Model,
                        VisionModel = ReadString(slot, "visionModel") ?? "",
                    };
                }
            }
        }
        else
        {
            providers[ProviderCatalog.Qwen] = new ProviderSlot
            {
                ApiKey = ReadString(obj, "apiKey") ?? "",
                Model = ReadString(obj, "model") ?? providers[ProviderCatalog.Qwen].Model,
                VisionModel = ReadString(obj, "visionModel") ?? ProviderCatalog.DefaultVisionModel,
            };
            settings.Provider = ProviderCatalog.Qwen;
        }
        settings.Providers = providers;

        if (obj["shortcuts"] is JsonObject shortcuts)
        {
            settings.Shortcuts = new DesktopShortcuts
            {
                Selection = ReadString(shortcuts, "selection") ?? "Alt+E",
                Crop = ReadString(shortcuts, "crop") ?? "Alt+S",
            };
        }

        if (obj["glossary"] is JsonArray glossary)
        {
            var entries = new List<GlossaryEntry>();
            foreach (var item in glossary)
            {
                if (item is not JsonObject row) continue;
                entries.Add(new GlossaryEntry(
                    ReadString(row, "id") ?? "",
                    ReadString(row, "source") ?? "",
                    ReadString(row, "target") ?? ""));
            }
            settings.Glossary = entries;
        }
        return AppSettingsFactory.Normalize(settings);
    }

    public static string Serialize(AppSettings settings)
    {
        var normalized = AppSettingsFactory.Normalize(settings);
        var providers = new JsonObject();
        foreach (var id in ProviderCatalog.Ids)
        {
            var slot = normalized.Providers[id];
            providers[id] = new JsonObject
            {
                ["apiKey"] = slot.ApiKey,
                ["model"] = slot.Model,
                ["visionModel"] = slot.VisionModel,
            };
        }
        var glossary = new JsonArray();
        foreach (var entry in normalized.Glossary)
        {
            glossary.Add(new JsonObject
            {
                ["id"] = entry.Id,
                ["source"] = entry.Source,
                ["target"] = entry.Target,
            });
        }
        var root = new JsonObject
        {
            ["serviceEnabled"] = normalized.ServiceEnabled,
            ["startOnLogin"] = normalized.StartOnLogin,
            ["provider"] = normalized.Provider,
            ["providers"] = providers,
            ["apiKey"] = normalized.ApiKey,
            ["model"] = normalized.Model,
            ["visionModel"] = normalized.VisionModel,
            ["sourceLang"] = normalized.SourceLang,
            ["targetLang"] = normalized.TargetLang,
            ["shortcuts"] = new JsonObject
            {
                ["selection"] = normalized.Shortcuts.Selection,
                ["crop"] = normalized.Shortcuts.Crop,
            },
            ["glossary"] = glossary,
        };
        return root.ToJsonString(Json);
    }

    private static string? ReadString(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue value) return null;
        if (value.TryGetValue<string>(out var text)) return text;
        return null;
    }

    private static bool ReadBool(JsonObject obj, string name, bool fallback)
    {
        if (obj[name] is not JsonValue value) return fallback;
        if (value.TryGetValue<bool>(out var flag)) return flag;
        return fallback;
    }
}

public sealed class FileUsageLedger : IUsageLedgerStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public FileUsageLedger(string path)
    {
        _path = path;
        Current = Load();
    }

    public UsageLedger Current { get; private set; }

    public void Remember(string provider, string model, TokenUsage? usage)
    {
        if (!ProviderCatalog.IsProviderId(provider)) return;
        lock (_gate)
        {
            Current.ByProvider[provider] = global::Yifan.Core.Quota.Quota.NextUsage(provider, model, Current.ByProvider[provider], usage);
            Save();
        }
    }

    private UsageLedger Load()
    {
        if (!File.Exists(_path)) return UsageLedger.Empty();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            return global::Yifan.Core.Quota.Quota.NormalizeLedger(doc.RootElement);
        }
        catch (Exception)
        {
            return UsageLedger.Empty();
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var by = new JsonObject();
        foreach (var id in ProviderCatalog.Ids)
        {
            var usage = Current.ByProvider[id];
            var node = new JsonObject
            {
                ["lastPromptTokens"] = usage.LastPromptTokens,
                ["lastCompletionTokens"] = usage.LastCompletionTokens,
                ["cumulativeCost"] = usage.CumulativeCost,
                ["callsWithUsage"] = usage.CallsWithUsage,
                ["callsWithoutUsage"] = usage.CallsWithoutUsage,
            };
            if (usage.LastCost is null) node["lastCost"] = null;
            else node["lastCost"] = usage.LastCost.Value;
            by[id] = node;
        }
        var json = new JsonObject { ["byProvider"] = by }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _path, overwrite: true);
    }
}
