using Yifan.Core.Chat;
using Yifan.Core.Errors;
using Yifan.Core.Glossary;
using Yifan.Core.Languages;
using Yifan.Core.Prompt;
using Yifan.Core.Providers;
using Yifan.Core.Selection;
using Yifan.Core.Settings;

namespace Yifan.Core.Chat;

public static class TranslationJob
{
    public static async Task RunAsync(
        YifanClient client,
        AppSettings settings,
        string text,
        string? sourceLang,
        string? targetLang,
        Action<string> onText,
        CancellationToken cancellationToken)
    {
        var cleaned = text.Trim();
        if (cleaned.Length == 0) throw TranslateException.EmptyCompletion("float");
        if (cleaned.Length > SelectionText.MaxChars) throw TranslateException.TooLong();
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) throw TranslateException.MissingKey("float");

        var source = Languages.Languages.IsSource(sourceLang) ? sourceLang! : settings.SourceLang;
        var target = Languages.Languages.IsTarget(targetLang) ? targetLang! : settings.TargetLang;
        var protectedText = Glossary.Glossary.Apply(cleaned, settings.Glossary);
        var system = PromptBuilder.BuildSystem(source, target, protectedText.Tokens);
        var raw = "";
        var shown = "";
        await client.StreamChatAsync(
            settings.Provider,
            settings.ApiKey.Trim(),
            settings.Model,
            system,
            protectedText.Text,
            "float",
            chunk =>
            {
                raw += chunk;
                var next = Glossary.Glossary.Present(raw, protectedText.Tokens, final: false);
                if (next.Length > 0 && next != shown)
                {
                    shown = next;
                    onText(next);
                }
            },
            cancellationToken).ConfigureAwait(false);

        var finalText = Glossary.Glossary.Present(raw, protectedText.Tokens, final: true).Trim();
        if (finalText.Length == 0) throw TranslateException.EmptyCompletion("float");
        if (finalText != shown) onText(finalText);
    }
}
