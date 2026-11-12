using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Yifan.Core.Errors;
using Yifan.Core.Providers;
using Yifan.Core.Quota;
using Yifan.Core.Sse;

namespace Yifan.Core.Chat;

public sealed class YifanClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly IUsageLedgerStore _usage;
    private readonly bool _ownsClient;

    public YifanClient(HttpMessageHandler handler, IUsageLedgerStore usage, bool disposeHandler = false)
    {
        _http = new HttpClient(handler, disposeHandler);
        _usage = usage;
        _ownsClient = true;
    }

    public YifanClient(HttpClient http, IUsageLedgerStore usage)
    {
        _http = http;
        _usage = usage;
        _ownsClient = false;
    }

    public IUsageLedgerStore Usage => _usage;

    public async Task<TokenUsage?> StreamChatAsync(
        string provider,
        string apiKey,
        string model,
        string system,
        string user,
        string where,
        Action<string> onDelta,
        CancellationToken cancellationToken)
    {
        var profile = ProviderCatalog.Get(provider);
        using var response = await SendAsync(
            profile.ChatCompletionsUrl,
            apiKey,
            ChatBodies.BuildChatJson(model, system, user, stream: true, maxTokens: null, provider),
            TimeSpan.FromSeconds(45),
            where,
            cancellationToken).ConfigureAwait(false);
        if (response.Content is null) throw TranslateException.EmptyCompletion(where);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (received, usage) = await SseParser.ReadStreamAsync(stream, onDelta, cancellationToken).ConfigureAwait(false);
            if (!received) throw TranslateException.EmptyCompletion(where);
            _usage.Remember(provider, model, usage);
            return usage;
        }
        catch (TranslateException)
        {
            throw;
        }
        catch (Exception error) when (IsAbort(error))
        {
            throw MapFetch(error, cancellationToken, where);
        }
        catch (Exception error)
        {
            var message = TranslateException.SanitizeDetail(error.Message, apiKey);
            if (System.Text.RegularExpressions.Regex.IsMatch(message, @"api[- ]?key|unauthorized|invalid api", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw TranslateException.HttpFailure(where, 401, "", apiKey);
            throw TranslateException.HttpFailure(where, 400, JsonNode.Parse($"{{\"message\":{System.Text.Json.JsonSerializer.Serialize(message.Length > 0 ? message : "流式响应无法解析")}}}")!.ToJsonString(), apiKey);
        }
    }

    public async Task TestConnectionAsync(string provider, string apiKey, string model, CancellationToken cancellationToken)
    {
        var profile = ProviderCatalog.Get(provider);
        using var response = await SendAsync(
            profile.ChatCompletionsUrl,
            apiKey,
            ChatBodies.BuildChatJson(model, system: null, user: "请只回复 OK", stream: false, maxTokens: 8, provider),
            TimeSpan.FromSeconds(20),
            "settings",
            cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        JsonNode? json;
        try
        {
            json = JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            throw TranslateException.EmptyCompletion("settings");
        }
        if (!ChatBodies.IsSuccessfulCompletion(json)) throw TranslateException.EmptyCompletion("settings");
        TokenUsage? usage = null;
        if (json is not null)
        {
            var element = System.Text.Json.JsonDocument.Parse(json.ToJsonString()).RootElement;
            usage = SseParser.ReadTokenUsage(element);
        }
        _usage.Remember(provider, model, usage);
    }

    public async Task<string> CompleteVisionAsync(
        string provider,
        string apiKey,
        string model,
        string prompt,
        string imageDataUrl,
        CancellationToken cancellationToken)
    {
        var profile = ProviderCatalog.Get(provider);
        using var response = await SendAsync(
            profile.ChatCompletionsUrl,
            apiKey,
            ChatBodies.BuildVisionJson(model, prompt, imageDataUrl, provider),
            TimeSpan.FromSeconds(45),
            "ocr",
            cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        JsonNode? json;
        try
        {
            json = JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            throw TranslateException.EmptyCompletion("ocr");
        }
        if (!ChatBodies.IsSuccessfulCompletion(json)) throw TranslateException.EmptyCompletion("ocr");
        var content = ChatBodies.CompletionText(json).Trim();
        if (content.Length == 0) throw TranslateException.EmptyCompletion("ocr");
        TokenUsage? usage = null;
        if (json is not null)
            usage = SseParser.ReadTokenUsage(System.Text.Json.JsonDocument.Parse(json.ToJsonString()).RootElement);
        _usage.Remember(provider, model, usage);
        return content;
    }

    private async Task<HttpResponseMessage> SendAsync(
        string url,
        string apiKey,
        string json,
        TimeSpan timeout,
        string where,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                response.Dispose();
                throw TranslateException.HttpFailure(where, (int)response.StatusCode, body, apiKey);
            }
            return response;
        }
        catch (TranslateException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw MapFetch(error, cancellationToken, where);
        }
    }

    private static Exception MapFetch(Exception error, CancellationToken userToken, string where)
    {
        if (userToken.IsCancellationRequested)
            return error is OperationCanceledException oce ? oce : new OperationCanceledException(userToken);
        if (error is OperationCanceledException or TimeoutException)
            return TranslateException.NetworkFailure(where, timedOut: true);
        return TranslateException.NetworkFailure(where, timedOut: false);
    }

    private static bool IsAbort(Exception error) =>
        error is OperationCanceledException or TimeoutException;

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
