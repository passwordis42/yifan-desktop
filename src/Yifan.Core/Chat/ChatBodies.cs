using System.Text.Json.Nodes;
using Yifan.Core.Providers;

namespace Yifan.Core.Chat;

public static class ChatBodies
{
    public static string BuildChatJson(string model, string? system, string user, bool stream, int? maxTokens, string provider)
    {
        var profile = ProviderCatalog.Get(provider);
        var messages = new JsonArray();
        if (!string.IsNullOrEmpty(system))
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = system });
        }
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = user });
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = stream,
        };
        if (model != "deepseek-reasoner") body["temperature"] = 0.2;
        if (profile.DisableThinking) body["enable_thinking"] = false;
        if (stream && profile.IncludeStreamUsage)
            body["stream_options"] = new JsonObject { ["include_usage"] = true };
        if (maxTokens is > 0 && model != "deepseek-reasoner") body["max_tokens"] = maxTokens.Value;
        return body.ToJsonString();
    }

    public static string BuildVisionJson(string model, string prompt, string imageDataUrl, string provider, int maxTokens = 2048)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = prompt },
                        new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject { ["url"] = imageDataUrl },
                        },
                    },
                },
            },
            ["stream"] = false,
            ["temperature"] = 0.1,
            ["max_tokens"] = maxTokens,
        };
        if (ProviderCatalog.Get(provider).DisableThinking) body["enable_thinking"] = false;
        return body.ToJsonString();
    }

    public static string CompletionText(JsonNode? json)
    {
        var choice = json?["choices"]?[0];
        var content = choice?["message"]?["content"];
        if (content is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        if (content is JsonArray parts)
        {
            var joined = "";
            foreach (var part in parts)
            {
                if (part?["text"] is JsonValue textValue && textValue.TryGetValue<string>(out var piece))
                    joined += piece;
            }
            return joined;
        }
        return "";
    }

    public static bool IsSuccessfulCompletion(JsonNode? json)
    {
        if (json is null) return false;
        if (json["error"] is not null) return false;
        return json["choices"] is JsonArray choices && choices.Count > 0;
    }
}
