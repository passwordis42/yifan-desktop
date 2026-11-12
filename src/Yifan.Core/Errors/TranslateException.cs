using System.Text.Json;

namespace Yifan.Core.Errors;

public static class TranslateErrorCodes
{
    public const string NoKey = "no_key";
    public const string Auth = "auth";
    public const string Network = "network";
    public const string Quota = "quota";
    public const string Empty = "empty";
    public const string Unknown = "unknown";
}

public class TranslateException : Exception
{
    public TranslateException(string code, string title, string message, bool fatal)
        : base(message)
    {
        Code = code;
        Title = title;
        Fatal = fatal;
    }

    public string Code { get; }
    public string Title { get; }
    public bool Fatal { get; }

    public static TranslateException MissingKey(string where)
    {
        if (where == "settings")
            return new TranslateException(TranslateErrorCodes.NoKey, "连接失败", "请先填写 API Key。不会假翻。", true);
        if (where == "ocr")
            return new TranslateException(TranslateErrorCodes.NoKey, "识译失败", "请先在设置中填写 API Key。不会假翻。", true);
        return new TranslateException(TranslateErrorCodes.NoKey, "翻译失败", "请先在设置中填写 API Key。不会假翻。", true);
    }

    public static TranslateException HttpFailure(string where, int status, string body, string apiKey)
    {
        var detail = SanitizeDetail(ExtractApiMessage(body), apiKey);
        if (status is 401 or 403)
        {
            var modelDenied = status == 403 && detail.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(detail, @"api[- ]?key|unauthorized", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (modelDenied)
                return new TranslateException(TranslateErrorCodes.Auth, TitleFor(where), $"{detail}——不会假翻。", true);
            return new TranslateException(TranslateErrorCodes.Auth, TitleFor(where), BodyFor(where), true);
        }
        if (status == 402)
        {
            return new TranslateException(
                TranslateErrorCodes.Quota,
                TitleFor(where),
                detail.Length > 0 ? $"{detail}——不会假翻。" : "额度不足或账户欠费。请检查该厂商账单——不会假翻。",
                true);
        }
        if (status == 429)
        {
            return new TranslateException(
                TranslateErrorCodes.Quota,
                TitleFor(where),
                detail.Length > 0 ? $"{detail}——不会假翻。" : "请求过于频繁或额度不足。请稍后再试——不会假翻。",
                true);
        }
        if (status >= 500)
        {
            return new TranslateException(TranslateErrorCodes.Network, TitleFor(where), "模型服务暂时不可用。请稍后再试——不会假翻。", true);
        }
        return new TranslateException(
            TranslateErrorCodes.Unknown,
            TitleFor(where),
            detail.Length > 0 ? $"{detail}——不会假翻。" : BodyFor(where),
            true);
    }

    public static TranslateException NetworkFailure(string where, bool timedOut = false)
    {
        var message = timedOut ? "网络超时。请检查网络后重试——不会假翻。" : BodyFor(where);
        return new TranslateException(TranslateErrorCodes.Network, TitleFor(where), message, true);
    }

    public static TranslateException EmptyCompletion(string where)
    {
        var message = where switch
        {
            "settings" => "模型没有返回内容。不会假翻。",
            "ocr" => "没有识别出文字。不会假翻。",
            _ => "模型没有返回译文。不会假翻。",
        };
        return new TranslateException(TranslateErrorCodes.Empty, TitleFor(where), message, where != "float");
    }

    public static TranslateException NoText() =>
        new(TranslateErrorCodes.Empty, "识译失败", "这一块没有识别出文字。不会假翻。", true);

    public static TranslateException TooLong() =>
        new(TranslateErrorCodes.Unknown, "翻译失败", "选区超过 5000 字，请缩短后再翻。不会假翻。", false);

    public static string ExtractApiMessage(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('<')) return "";
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var nested) && nested.ValueKind == JsonValueKind.String)
                return nested.GetString() ?? "";
            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString() ?? "";
            return "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    public static string SanitizeDetail(string detail, string apiKey)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(detail, @"\s+", " ").Trim();
        if (text.Length == 0) return "";
        if (!string.IsNullOrEmpty(apiKey) && apiKey.Length > 6)
            text = text.Replace(apiKey, "***", StringComparison.Ordinal);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"sk-[A-Za-z0-9_-]{6,}", "sk-***");
        if (text.Length > 160) text = text[..160] + "…";
        return text;
    }

    private static string TitleFor(string where) => where switch
    {
        "settings" => "连接失败",
        "ocr" => "识译失败",
        _ => "翻译失败",
    };

    private static string BodyFor(string where) => where switch
    {
        "settings" => "API Key 无效或网络不可达。请检查后重试——不会假翻。",
        "ocr" => "API Key 无效或网络不可达。请到设置中检查后重试——不会假翻。页面正文没有改动。",
        _ => "API Key 无效或网络不可达。请到设置中检查后重试——不会假翻。",
    };
}

/// <summary>字已经识出来，但译文这一步失败。面板可以展示原文，不能编译文。</summary>
public sealed class OcrTranslateException : TranslateException
{
    public OcrTranslateException(string source, TranslateException cause)
        : base(cause.Code, "识译失败", cause.Message, cause.Fatal)
    {
        RecognizedText = source;
    }

    public string RecognizedText { get; }
}
