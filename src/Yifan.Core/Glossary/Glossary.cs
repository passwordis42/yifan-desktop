using System.Text.RegularExpressions;
using Yifan.Core.Text;

namespace Yifan.Core.Glossary;

public sealed record GlossaryEntry(string Id, string Source, string Target);

public sealed record GlossaryToken(string Token, string Source, string Target);

public sealed record AppliedGlossary(string Text, IReadOnlyList<GlossaryToken> Tokens);

public static class Glossary
{
    public const int MaxEntries = 200;
    public const int MaxSource = 80;
    public const int MaxTarget = 200;

    public static List<GlossaryEntry> NormalizeEntries(IEnumerable<GlossaryEntry>? entries)
    {
        var output = new List<GlossaryEntry>();
        if (entries is null) return output;
        foreach (var entry in entries)
        {
            var source = (entry.Source ?? "").Trim();
            var target = (entry.Target ?? "").Trim();
            if (source.Length == 0 || target.Length == 0) continue;
            if (source.Length > MaxSource || target.Length > MaxTarget) continue;
            var id = string.IsNullOrWhiteSpace(entry.Id)
                ? $"g{output.Count}-{source[..Math.Min(12, source.Length)]}"
                : entry.Id.Trim();
            output.Add(new GlossaryEntry(id, source, target));
            if (output.Count >= MaxEntries) break;
        }
        return output;
    }

    public static bool TryAdd(IReadOnlyList<GlossaryEntry> current, string source, string target, out List<GlossaryEntry> next, out string? error)
    {
        next = NormalizeEntries(current);
        var src = source.Trim();
        var dst = target.Trim();
        if (src.Length == 0 || dst.Length == 0)
        {
            error = "原文和固定译文都要填。";
            return false;
        }
        if (src.Length > MaxSource || dst.Length > MaxTarget)
        {
            error = $"原文最多 {MaxSource} 字，固定译文最多 {MaxTarget} 字。";
            return false;
        }
        if (next.Count >= MaxEntries)
        {
            error = $"术语本最多 {MaxEntries} 条。";
            return false;
        }
        next.Add(new GlossaryEntry($"g{next.Count}-{src[..Math.Min(12, src.Length)]}", src, dst));
        error = null;
        return true;
    }

    public static List<GlossaryEntry> Remove(IReadOnlyList<GlossaryEntry> current, string id) =>
        NormalizeEntries(current.Where(entry => entry.Id != id));

    /// <summary>长词优先、不重叠。拉丁词用边界，避免 key 命中 keyboard。</summary>
    public static AppliedGlossary Apply(string text, IReadOnlyList<GlossaryEntry> entries)
    {
        var sorted = NormalizeEntries(entries)
            .OrderByDescending(entry => entry.Source.Length)
            .ToList();
        var result = text;
        var tokens = new List<GlossaryToken>();
        foreach (var entry in sorted)
        {
            var matcher = BuildMatcher(entry.Source);
            if (!matcher.IsMatch(result)) continue;
            var token = $"[[YF{tokens.Count}]]";
            result = matcher.Replace(result, token);
            tokens.Add(new GlossaryToken(token, entry.Source, entry.Target));
        }
        return new AppliedGlossary(result, tokens);
    }

    public static string Restore(string text, IReadOnlyList<GlossaryToken> tokens)
    {
        var output = text;
        foreach (var item in tokens)
        {
            var match = Regex.Match(item.Token, @"(\d+)");
            if (!match.Success) continue;
            var index = match.Groups[1].Value;
            output = Regex.Replace(output, $@"\[\[\s*YF\s*{index}\s*\]\]", item.Target);
        }
        return output;
    }

    /// <summary>流式中间态：末尾还没写完的占位符先藏起来。</summary>
    public static string Present(string raw, IReadOnlyList<GlossaryToken> tokens, bool final)
    {
        var restored = Restore(raw, tokens);
        var stripped = Regex.Replace(restored, @"\[\[YF\d*$", "");
        return final ? stripped.TrimEnd() : stripped;
    }

    private static Regex BuildMatcher(string source)
    {
        var trimmed = source.Trim();
        var escaped = Regex.Replace(Regex.Escape(trimmed), @"\s+", @"\s+");
        var runes = trimmed.EnumerateRunes().ToList();
        if (runes.Count == 0)
            return new Regex("$^", RegexOptions.CultureInvariant);
        var first = runes[0];
        var last = runes[^1];
        var latinEdge = UnicodeScripts.IsLatin(first) || UnicodeScripts.IsNumber(first)
            || UnicodeScripts.IsLatin(last) || UnicodeScripts.IsNumber(last);
        if (!latinEdge)
            return new Regex(escaped, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var pre = UnicodeScripts.IsLetterOrNumber(first) ? @"(?<![\p{L}\p{N}_])" : "";
        var post = UnicodeScripts.IsLetterOrNumber(last) ? @"(?![\p{L}\p{N}_])" : "";
        return new Regex($"{pre}{escaped}{post}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
