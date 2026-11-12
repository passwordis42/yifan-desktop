using Yifan.Core.Glossary;
using Yifan.Core.Languages;

namespace Yifan.Core.Prompt;

public static class PromptBuilder
{
    public static string BuildSystem(string sourceLang, string targetLang, IReadOnlyList<GlossaryToken> tokens)
    {
        var target = Languages.Languages.PromptName(targetLang);
        var sourceLine = sourceLang == "auto"
            ? "先判断源语言。"
            : $"源语言是{Languages.Languages.PromptName(sourceLang)}。";
        var lines = new List<string>
        {
            $"你是专业翻译。{sourceLine}把用户文本翻译成{target}。",
            "只输出译文本身，不要解释、不要加引号、不要加标题或前言。",
            "保留原有换行。代码、URL、版本号、已经是目标语言的专名保持原样。",
        };
        if (tokens.Count > 0)
        {
            lines.Add("用户文本里的占位符必须原样出现在译文的对应位置，不要翻译、不要改写、不要拆开括号。固定译法优先于你自己的措辞：");
            foreach (var token in tokens)
                lines.Add($"{token.Token} 表示「{token.Source}」，固定译法是「{token.Target}」。输出时只写 {token.Token}。");
        }
        return string.Join("\n", lines);
    }
}
