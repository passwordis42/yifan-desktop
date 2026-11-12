using System.Text.Json;
using Yifan.Core.Providers;
using Yifan.Core.Sse;

namespace Yifan.Core.Quota;

public sealed class ProviderUsage
{
    public int LastPromptTokens { get; set; }
    public int LastCompletionTokens { get; set; }
    public double? LastCost { get; set; }
    public double CumulativeCost { get; set; }
    public int CallsWithUsage { get; set; }
    public int CallsWithoutUsage { get; set; }

    public static ProviderUsage Empty() => new();

    public ProviderUsage Clone() => new()
    {
        LastPromptTokens = LastPromptTokens,
        LastCompletionTokens = LastCompletionTokens,
        LastCost = LastCost,
        CumulativeCost = CumulativeCost,
        CallsWithUsage = CallsWithUsage,
        CallsWithoutUsage = CallsWithoutUsage,
    };
}

public sealed class UsageLedger
{
    public Dictionary<string, ProviderUsage> ByProvider { get; set; } = EmptyMap();

    public static UsageLedger Empty() => new() { ByProvider = EmptyMap() };

    public static Dictionary<string, ProviderUsage> EmptyMap() => new()
    {
        [ProviderCatalog.Qwen] = ProviderUsage.Empty(),
        [ProviderCatalog.OpenAI] = ProviderUsage.Empty(),
        [ProviderCatalog.Volcano] = ProviderUsage.Empty(),
        [ProviderCatalog.DeepSeek] = ProviderUsage.Empty(),
        [ProviderCatalog.Kimi] = ProviderUsage.Empty(),
    };
}

public sealed record QuotaView(string Tone, string Main, string Sub, string Badge, string Label);

public static class Quota
{
    public static ProviderUsage NormalizeUsage(JsonElement? raw)
    {
        var usage = ProviderUsage.Empty();
        if (raw is null || raw.Value.ValueKind != JsonValueKind.Object) return usage;
        var record = raw.Value;
        usage.LastPromptTokens = NonNegative(record, "lastPromptTokens");
        usage.LastCompletionTokens = NonNegative(record, "lastCompletionTokens");
        usage.LastCost = record.TryGetProperty("lastCost", out var cost) && cost.ValueKind == JsonValueKind.Number && cost.TryGetDouble(out var number) && double.IsFinite(number)
            ? number
            : null;
        usage.CumulativeCost = ReadNonNegative(record, "cumulativeCost");
        usage.CallsWithUsage = NonNegative(record, "callsWithUsage");
        usage.CallsWithoutUsage = NonNegative(record, "callsWithoutUsage");
        return usage;
    }

    public static UsageLedger NormalizeLedger(JsonElement? raw)
    {
        var ledger = UsageLedger.Empty();
        if (raw is null || raw.Value.ValueKind != JsonValueKind.Object) return ledger;
        if (!raw.Value.TryGetProperty("byProvider", out var by) || by.ValueKind != JsonValueKind.Object)
            return ledger;
        foreach (var id in ProviderCatalog.Ids)
        {
            JsonElement? slot = by.TryGetProperty(id, out var item) ? item : null;
            ledger.ByProvider[id] = NormalizeUsage(slot);
        }
        return ledger;
    }

    public static ProviderUsage NextUsage(string provider, string model, ProviderUsage previous, TokenUsage? usage)
    {
        if (usage is null)
        {
            var copy = previous.Clone();
            copy.LastPromptTokens = 0;
            copy.LastCompletionTokens = 0;
            copy.LastCost = null;
            copy.CallsWithoutUsage = previous.CallsWithoutUsage + 1;
            return copy;
        }
        var cost = Pricing.EstimateCost(provider, model, usage);
        return new ProviderUsage
        {
            LastPromptTokens = usage.PromptTokens,
            LastCompletionTokens = usage.CompletionTokens,
            LastCost = cost,
            CumulativeCost = previous.CumulativeCost + cost,
            CallsWithUsage = previous.CallsWithUsage + 1,
            CallsWithoutUsage = previous.CallsWithoutUsage,
        };
    }

    public static QuotaView EstimateView(string provider, ProviderUsage usage)
    {
        return new QuotaView("estimate", EstimateMain(provider, usage), EstimateSub(provider, usage), "估算", "额度估算");
    }

    public static (double Amount, string Currency)? ParseDeepseekBalance(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("balance_infos", out var infos) || infos.ValueKind != JsonValueKind.Array)
            return null;
        var rows = new List<(double Amount, string Currency)>();
        foreach (var item in infos.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var amount = AsAmount(item, "total_balance");
            if (amount is null) continue;
            var currency = item.TryGetProperty("currency", out var cur) ? (cur.GetString() ?? "").ToUpperInvariant() : "";
            if (currency is not ("CNY" or "USD")) continue;
            rows.Add((amount.Value, currency));
        }
        var cny = rows.FirstOrDefault(row => row.Currency == "CNY");
        if (rows.Count == 0) return null;
        return cny.Currency == "CNY" ? cny : rows[0];
    }

    public static double? ParseKimiBalance(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object) return null;
        if (json.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var codeNumber) && codeNumber != 0)
            return null;
        if (json.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.False)
            return null;
        if (json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            var nested = AsAmount(data, "available_balance");
            if (nested is not null) return nested;
        }
        return AsAmount(json, "available_balance");
    }

    public static async Task<(bool Ok, double Amount, string Currency, string Reason)> QueryBalanceAsync(
        string provider,
        string apiKey,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        var spec = ProviderCatalog.Get(provider).Balance;
        if (spec is null) return (false, 0, "CNY", "这家不能用聊天 Key 查余额");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, spec.Url);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, 0, "CNY", "余额查询失败：网络超时");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return (false, 0, "CNY", "余额查询失败：网络不可达");
        }
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return (false, 0, "CNY", BalanceHttpReason((int)response.StatusCode));
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (spec.Parser == "deepseek")
            {
                var parsed = ParseDeepseekBalance(doc.RootElement);
                if (parsed is null) return (false, 0, "CNY", "余额查询失败：返回里没有可用余额");
                return (true, parsed.Value.Amount, parsed.Value.Currency, "");
            }
            var kimi = ParseKimiBalance(doc.RootElement);
            if (kimi is null) return (false, 0, "CNY", "余额查询失败：返回里没有可用余额");
            return (true, kimi.Value, "CNY", "");
        }
        catch (JsonException)
        {
            return (false, 0, "CNY", "余额查询失败：返回无法解析");
        }
    }

    public static async Task<QuotaView> LoadViewAsync(string provider, string apiKey, ProviderUsage usage, HttpClient http, CancellationToken cancellationToken)
    {
        var profile = ProviderCatalog.Get(provider);
        if (profile.Balance is null) return EstimateView(provider, usage);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new QuotaView("estimate", "尚未填写 API Key", profile.Balance.PendingNote, "余额", "账户余额");
        }
        var result = await QueryBalanceAsync(provider, apiKey.Trim(), http, cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            var estimate = EstimateView(provider, usage);
            return estimate with { Sub = $"{result.Reason}，已改为本地估算" };
        }
        return new QuotaView(
            "balance",
            $"{profile.Balance.Headline} {Pricing.FormatMoney(result.Amount, result.Currency)}",
            profile.Balance.SourceNote,
            "余额",
            "账户余额");
    }

    private static string EstimateMain(string provider, ProviderUsage usage)
    {
        var currency = ProviderCatalog.Get(provider).Currency;
        var total = Pricing.FormatMoney(usage.CumulativeCost, currency);
        if (usage.CallsWithUsage == 0 && usage.CallsWithoutUsage == 0)
            return $"本次暂无用量 · 本机累计估算 {Pricing.FormatMoney(0, currency)}";
        if (usage.LastCost is null && usage.CallsWithUsage == 0)
            return $"本次未返回用量 · 本机累计 {usage.CallsWithoutUsage} 次（未估金额）";
        if (usage.LastCost is null) return $"本次未返回用量 · 本机累计估算 {total}";
        return $"本次约 {Pricing.FormatMoney(usage.LastCost.Value, currency)} · 本机累计估算 {total}";
    }

    private static string EstimateSub(string provider, ProviderUsage usage)
    {
        var note = ProviderCatalog.Get(provider).EstimateNote;
        if (usage.LastCost is null && usage.CallsWithoutUsage > 0)
            return $"本次响应没有 usage，未估这次金额。{note}";
        return note;
    }

    private static string BalanceHttpReason(int status) => status switch
    {
        401 or 403 => "余额查询失败：Key 无效或无权限",
        402 => "余额查询失败：额度不足或未开通",
        429 => "余额查询失败：请求过于频繁",
        >= 500 => "余额查询失败：服务暂时不可用",
        _ => $"余额查询失败：HTTP {status}",
    };

    private static double? AsAmount(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)) return number;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
            return parsed;
        return null;
    }

    private static int NonNegative(JsonElement record, string name)
    {
        if (!record.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number) return 0;
        if (!value.TryGetInt32(out var number) || number < 0) return 0;
        return number;
    }

    private static double ReadNonNegative(JsonElement record, string name)
    {
        if (!record.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number) return 0;
        if (!value.TryGetDouble(out var number) || !double.IsFinite(number) || number < 0) return 0;
        return number;
    }
}

public interface IUsageLedgerStore
{
    UsageLedger Current { get; }
    void Remember(string provider, string model, TokenUsage? usage);
}

public sealed class MemoryUsageLedger : IUsageLedgerStore
{
    public UsageLedger Current { get; private set; } = UsageLedger.Empty();

    public void Remember(string provider, string model, TokenUsage? usage)
    {
        if (!ProviderCatalog.IsProviderId(provider)) return;
        Current.ByProvider[provider] = Quota.NextUsage(provider, model, Current.ByProvider[provider], usage);
    }

    public void Replace(UsageLedger ledger) => Current = ledger;
}
