using System.Text.RegularExpressions;
using Yifan.Core.Chat;
using Yifan.Core.Errors;
using Yifan.Core.Providers;
using Yifan.Core.Settings;

namespace Yifan.Core.Ocr;

public static class Ocr
{
    public const string CropTip = "拖拽框选要翻译的区域 · 松开后识别 · Esc 或点空白取消";
    public const string PanelTitle = "截图识译";
    public const int MinCropPx = 12;
    public const int MaxImageChars = 7_000_000;
    public const int LongestSide = 1600;

    public readonly record struct CssRect(double X, double Y, double Width, double Height);
    public readonly record struct CropPixels(int Sx, int Sy, int Sw, int Sh);

    public static CropPixels? ToPixels(CssRect rect, int imageWidth, int imageHeight, int viewportWidth, int viewportHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0) return null;
        if (imageWidth <= 0 || imageHeight <= 0) return null;
        if (rect.Width <= 0 || rect.Height <= 0) return null;
        var scaleX = imageWidth / (double)viewportWidth;
        var scaleY = imageHeight / (double)viewportHeight;
        var sx = (int)Math.Round(rect.X * scaleX);
        var sy = (int)Math.Round(rect.Y * scaleY);
        var sw = (int)Math.Round(rect.Width * scaleX);
        var sh = (int)Math.Round(rect.Height * scaleY);
        if (sx < 0)
        {
            sw += sx;
            sx = 0;
        }
        if (sy < 0)
        {
            sh += sy;
            sy = 0;
        }
        if (sx + sw > imageWidth) sw = imageWidth - sx;
        if (sy + sh > imageHeight) sh = imageHeight - sy;
        if (sw < 2 || sh < 2) return null;
        return new CropPixels(sx, sy, sw, sh);
    }

    public static CssRect NormalizeRect(double x0, double y0, double x1, double y1, double boundsWidth, double boundsHeight)
    {
        var left = Clamp(Math.Min(x0, x1), 0, boundsWidth);
        var top = Clamp(Math.Min(y0, y1), 0, boundsHeight);
        var right = Clamp(Math.Max(x0, x1), 0, boundsWidth);
        var bottom = Clamp(Math.Max(y0, y1), 0, boundsHeight);
        return new CssRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    public static bool IsLargeEnough(CssRect rect, double min = MinCropPx) =>
        rect.Width >= min && rect.Height >= min;

    public static bool IsImageDataUrl(string value)
    {
        if (value.Length < 32 || value.Length > MaxImageChars) return false;
        return Regex.IsMatch(value, @"^data:image\/(?:png|jpe?g|webp);base64,[a-z0-9+/=\s]+$", RegexOptions.IgnoreCase);
    }

    public static string BuildOcrPrompt() =>
        string.Join("\n",
        [
            "请只抄写图片里能看清的文字。",
            "保留原有换行。不要翻译，不要解释，不要补写图片里没有的句子。",
            "如果没有可辨认的文字，只输出：无文字",
        ]);

    public static (bool Ok, string Text, string Reason) ParseOcrText(string raw)
    {
        var text = raw.Trim().TrimStart('\uFEFF');
        text = Regex.Replace(text, @"^```[a-z]*\s*", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s*```$", "");
        text = text.Trim();
        if (text.Length == 0) return (false, "", "empty");
        if (Regex.IsMatch(text, @"^无文字[。．.]?$")) return (false, "", "no_text");
        return (true, text, "");
    }

    public static async Task<(string Source, string Translation)> RecognizeAndTranslateAsync(
        YifanClient client,
        AppSettings settings,
        string imageDataUrl,
        CancellationToken cancellationToken)
    {
        if (!IsImageDataUrl(imageDataUrl))
            throw new TranslateException("unknown", "识译失败", "选区图片太大或无效。没有改动页面。不会假翻。", true);
        var model = ProviderCatalog.VisionCallModel(
            settings.Provider,
            settings.Model,
            settings.VisionModel,
            settings.Providers.TryGetValue(ProviderCatalog.Qwen, out var qwen) ? qwen.VisionModel : "");
        if (model is null)
        {
            throw new TranslateException(
                "unknown",
                "识译失败",
                $"{ProviderCatalog.NoVisionOcrMessage}。不会假翻。页面正文没有改动。",
                true);
        }
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) throw TranslateException.MissingKey("ocr");
        var recognized = await client.CompleteVisionAsync(
            settings.Provider,
            settings.ApiKey.Trim(),
            model,
            BuildOcrPrompt(),
            imageDataUrl,
            cancellationToken).ConfigureAwait(false);
        var parsed = ParseOcrText(recognized);
        if (!parsed.Ok) throw TranslateException.NoText();
        var translation = "";
        try
        {
            await TranslationJob.RunAsync(
                client,
                settings,
                parsed.Text,
                sourceLang: null,
                targetLang: null,
                onText: text => translation = text,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TranslateException error)
        {
            throw new OcrTranslateException(parsed.Text, error);
        }
        var finalText = translation.Trim();
        if (finalText.Length == 0)
            throw new TranslateException("empty", "识译失败", "没有得到译文。不会假翻。", true);
        return (parsed.Text, finalText);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min) return min;
        return Math.Min(max, Math.Max(min, value));
    }
}
