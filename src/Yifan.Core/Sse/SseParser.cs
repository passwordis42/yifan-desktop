using System.Text.Json;

namespace Yifan.Core.Sse;

public sealed record TokenUsage(int PromptTokens, int CompletionTokens);

public sealed record ParsedEvent(string Content, bool Done, string? Error, TokenUsage? Usage);

public static class SseParser
{
    public static TokenUsage? ReadTokenUsage(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return null;
        var prompt = AsCount(usage, "prompt_tokens") ?? AsCount(usage, "input_tokens");
        var completion = AsCount(usage, "completion_tokens") ?? AsCount(usage, "output_tokens");
        if (prompt is null && completion is null) return null;
        return new TokenUsage(prompt ?? 0, completion ?? 0);
    }

    public static (List<string> Events, string Remainder) ParseBuffer(string buffer)
    {
        var normalized = buffer.Replace("\r\n", "\n").Replace("\r", "\n");
        var parts = normalized.Split("\n\n").ToList();
        var rest = parts.Count > 0 ? parts[^1] : "";
        if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
        return (parts.Where(part => part.Trim().Length > 0).ToList(), rest);
    }

    public static ParsedEvent ParseEvent(string raw)
    {
        var content = "";
        var done = false;
        string? error = null;
        TokenUsage? usage = null;
        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = trimmed[5..].Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]")
            {
                done = true;
                continue;
            }
            try
            {
                using var doc = JsonDocument.Parse(data);
                var json = doc.RootElement;
                var next = ReadTokenUsage(json);
                if (next is not null) usage = next;
                if (json.TryGetProperty("error", out var err) && err.ValueKind != JsonValueKind.Null && err.ValueKind != JsonValueKind.Undefined)
                {
                    error = err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                        ? message.GetString() ?? "请求被拒绝"
                        : "请求被拒绝";
                }
                if (json.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object
                        && delta.TryGetProperty("content", out var deltaContent) && deltaContent.ValueKind == JsonValueKind.String)
                        content += deltaContent.GetString();
                    else if (choice.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object
                        && msg.TryGetProperty("content", out var messageContent) && messageContent.ValueKind == JsonValueKind.String)
                        content += messageContent.GetString();
                }
            }
            catch (JsonException)
            {
                // 半包或心跳跳过。
            }
        }
        return new ParsedEvent(content, done, error, usage);
    }

    public static async Task<(bool Received, TokenUsage? Usage)> ReadStreamAsync(
        Stream stream,
        Action<string> onDelta,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        var buffer = "";
        var received = false;
        TokenUsage? usage = null;
        void Handle(string rawEvent)
        {
            var parsed = ParseEvent(rawEvent);
            if (parsed.Usage is not null) usage = parsed.Usage;
            if (parsed.Error is not null) throw new InvalidOperationException(parsed.Error);
            if (parsed.Content.Length > 0)
            {
                received = true;
                onDelta(parsed.Content);
            }
        }

        while (true)
        {
            var chunk = new char[4096];
            var count = await reader.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            buffer += new string(chunk, 0, count);
            var parsed = ParseBuffer(buffer);
            buffer = parsed.Remainder;
            foreach (var rawEvent in parsed.Events) Handle(rawEvent);
        }
        if (buffer.Trim().Length > 0) Handle(buffer);
        return (received, usage);
    }

    private static int? AsCount(JsonElement usage, string name)
    {
        if (!usage.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0) return number;
        return null;
    }
}
