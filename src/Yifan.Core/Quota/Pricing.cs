using Yifan.Core.Sse;

namespace Yifan.Core.Quota;

public sealed record TokenRate(double Input, double Output);

public sealed class ProviderPrice
{
    public required string Currency { get; init; }
    public required double Input { get; init; }
    public required double Output { get; init; }
    public required Dictionary<string, TokenRate> Models { get; init; }
}

/// <summary>
/// 本地估算用的公开标价粗表，单位是每 1,000,000 tokens。不是账单。
/// 与 chrome-llm-translate v0.1.14 pricing.ts 同一张表。
/// </summary>
public static class Pricing
{
    public static readonly IReadOnlyDictionary<string, ProviderPrice> Table = new Dictionary<string, ProviderPrice>
    {
        ["qwen"] = new()
        {
            Currency = "CNY",
            Input = 0.8,
            Output = 2,
            Models = new()
            {
                ["qwen3.5-flash"] = new(0.2, 2),
                ["qwen3.7-flash"] = new(0.2, 2),
                ["qwen3.8-flash"] = new(0.2, 2),
                ["qwen3.5-plus"] = new(0.8, 2),
                ["qwen3.6-plus"] = new(0.8, 2),
                ["qwen3.7-plus"] = new(0.8, 2),
                ["qwen3.7-max"] = new(2.4, 9.6),
                ["qwen3.8-max"] = new(2.4, 9.6),
                ["qwen3-vl-plus"] = new(1, 10),
            },
        },
        ["openai"] = new()
        {
            Currency = "USD",
            Input = 0.15,
            Output = 0.6,
            Models = new()
            {
                ["gpt-4o-mini"] = new(0.15, 0.6),
                ["gpt-4o"] = new(2.5, 10),
                ["gpt-4.1"] = new(2, 8),
            },
        },
        ["volcano"] = new()
        {
            Currency = "CNY",
            Input = 0.8,
            Output = 2,
            Models = new()
            {
                ["doubao-seed-1.6"] = new(0.8, 2),
                ["doubao-1.5-pro-32k"] = new(0.8, 2),
                ["doubao-1.5-lite-32k"] = new(0.3, 0.6),
            },
        },
        ["deepseek"] = new()
        {
            Currency = "CNY",
            Input = 2,
            Output = 8,
            Models = new()
            {
                ["deepseek-chat"] = new(2, 8),
                ["deepseek-reasoner"] = new(4, 16),
            },
        },
        ["kimi"] = new()
        {
            Currency = "CNY",
            Input = 12,
            Output = 12,
            Models = new()
            {
                ["moonshot-v1-8k"] = new(12, 12),
                ["moonshot-v1-32k"] = new(24, 24),
                ["moonshot-v1-128k"] = new(60, 60),
            },
        },
    };

    public static (string Currency, double Input, double Output) RateFor(string provider, string model)
    {
        var table = Table[provider];
        if (table.Models.TryGetValue(model, out var exact))
            return (table.Currency, exact.Input, exact.Output);
        return (table.Currency, table.Input, table.Output);
    }

    public static double EstimateCost(string provider, string model, TokenUsage usage)
    {
        var rate = RateFor(provider, model);
        return (usage.PromptTokens * rate.Input + usage.CompletionTokens * rate.Output) / 1_000_000d;
    }

    public static string FormatMoney(double amount, string currency)
    {
        var symbol = currency == "USD" ? "$" : "¥";
        if (!double.IsFinite(amount)) return $"{symbol}0.00";
        var abs = Math.Abs(amount);
        var digits = abs != 0 && abs < 0.01 ? 4 : 2;
        return symbol + amount.ToString($"F{digits}", System.Globalization.CultureInfo.InvariantCulture);
    }
}
