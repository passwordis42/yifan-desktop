using Yifan.Core.Text;

namespace Yifan.Core.Languages;

public sealed record LanguageOption(string Code, string Label, string Short, string PromptName);

public static class Languages
{
    public static readonly LanguageOption[] Targets =
    [
        new("zh", "中文", "中文", "简体中文"),
        new("en", "English", "EN", "英语"),
        new("ja", "日本語", "日语", "日语"),
        new("zh-Hant", "繁体中文", "繁体", "繁体中文"),
        new("ko", "한국어", "韩语", "韩语"),
        new("fr", "Français", "FR", "法语"),
        new("de", "Deutsch", "DE", "德语"),
        new("es", "Español", "ES", "西班牙语"),
        new("pt", "Português", "PT", "葡萄牙语"),
        new("ru", "Русский", "RU", "俄语"),
        new("it", "Italiano", "IT", "意大利语"),
        new("vi", "Tiếng Việt", "VI", "越南语"),
        new("th", "ไทย", "TH", "泰语"),
        new("id", "Bahasa Indonesia", "ID", "印尼语"),
        new("ar", "العربية", "AR", "阿拉伯语"),
    ];

    public static readonly LanguageOption[] Sources =
        [new("auto", "自动检测", "自动", "自动检测"), .. Targets];

    private static readonly HashSet<string> TargetCodes = Targets.Select(item => item.Code).ToHashSet();
    private static readonly HashSet<string> SourceCodes = Sources.Select(item => item.Code).ToHashSet();

    public static bool IsTarget(string? code) => code is not null && TargetCodes.Contains(code);
    public static bool IsSource(string? code) => code is not null && SourceCodes.Contains(code);

    public static LanguageOption? ByCode(string code) =>
        Sources.FirstOrDefault(item => item.Code == code);

    public static string ShortLabel(string code) => ByCode(code)?.Short ?? code;
    public static string PromptName(string code) => ByCode(code)?.PromptName ?? code;
    public static string FullLabel(string code) => ByCode(code)?.Label ?? code;

    /// <summary>源语言为自动时，看文字脚本猜一个短标签，猜不出就写「自动」。</summary>
    public static string? GuessSourceCode(string text)
    {
        var sample = text.Length > 500 ? text[..500] : text;
        var han = 0;
        var kana = 0;
        var hangul = 0;
        var cyrillic = 0;
        var arabic = 0;
        var thai = 0;
        var latin = 0;
        foreach (var rune in sample.EnumerateRunes())
        {
            if (UnicodeScripts.IsHiragana(rune) || UnicodeScripts.IsKatakana(rune)) kana++;
            else if (UnicodeScripts.IsHangul(rune)) hangul++;
            else if (UnicodeScripts.IsHan(rune)) han++;
            else if (UnicodeScripts.IsCyrillic(rune)) cyrillic++;
            else if (UnicodeScripts.IsArabic(rune)) arabic++;
            else if (UnicodeScripts.IsThai(rune)) thai++;
            else if (UnicodeScripts.IsLatin(rune)) latin++;
        }
        if (kana >= 2) return "ja";
        (string Code, int Count)[] ranked =
        [
            ("ko", hangul),
            ("zh", han),
            ("ru", cyrillic),
            ("ar", arabic),
            ("th", thai),
            ("en", latin),
        ];
        Array.Sort(ranked, (a, b) => b.Count.CompareTo(a.Count));
        if (ranked[0].Count < 2) return null;
        return ranked[0].Code;
    }

    public static string PairLabel(string sourceLang, string targetLang, string? sample = null)
    {
        var guessed = sourceLang == "auto" && sample is not null ? GuessSourceCode(sample) : null;
        var left = sourceLang == "auto" ? ShortLabel(guessed ?? "auto") : ShortLabel(sourceLang);
        return $"{left} → {ShortLabel(targetLang)}";
    }

    public static string HomePair(string sourceLang, string targetLang) =>
        $"{FullLabel(sourceLang)} → {FullLabel(targetLang)}";
}
