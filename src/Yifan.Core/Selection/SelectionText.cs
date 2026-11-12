namespace Yifan.Core.Selection;

public static class SelectionText
{
    public const int MaxChars = 5000;

    public static string Clean(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var text = raw.Replace('\u00a0', ' ');
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t]+\n", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    public static SelectionClass Classify(string? text)
    {
        var cleaned = Clean(text);
        if (cleaned.Length == 0) return SelectionClass.Empty;
        if (cleaned.Length > MaxChars) return SelectionClass.TooLong;
        return SelectionClass.Ok;
    }
}

public enum SelectionClass
{
    Empty,
    TooLong,
    Ok,
}

public enum SelectionOrigin
{
    None,
    Foreground,
    SimulatedCopy,
    Clipboard,
}

public sealed record AcquisitionResult(bool Ready, string Text, SelectionOrigin Origin, string Title, string Message)
{
    public static AcquisitionResult Ok(string text, SelectionOrigin origin) =>
        new(true, text, origin, "", "");

    public static AcquisitionResult Local(string title, string message) =>
        new(false, "", SelectionOrigin.None, title, message);
}

public sealed record AcquisitionInput(
    string? UiaText,
    bool Protected,
    string? ClipboardBefore,
    string? ClipboardAfterCopy,
    bool CopyAttempted);

/// <summary>
/// 优先前台选中文字。受保护窗口不改去读剪贴板。取不到才用剪贴板；模拟复制得到的新文本优先于原来的剪贴板。
/// </summary>
public static class SelectionAcquisition
{
    public const string EmptyTitle = "未能取词";
    public const string EmptyMessage = "没有选中文字，也读不到剪贴板。";
    public const string TooLongTitle = "未发送请求";
    public const string TooLongMessage = "选中文字超过 5000 字，请缩短后再译。";
    public const string ProtectedMessage = "这个窗口受保护或权限不够，读不到选中文字。不会假翻。";

    public static AcquisitionResult Resolve(AcquisitionInput input)
    {
        var uia = SelectionText.Clean(input.UiaText);
        if (input.Protected && uia.Length == 0)
            return AcquisitionResult.Local(EmptyTitle, ProtectedMessage);
        var uiaClass = SelectionText.Classify(uia);
        if (uiaClass == SelectionClass.TooLong)
            return AcquisitionResult.Local(TooLongTitle, TooLongMessage);
        if (uiaClass == SelectionClass.Ok)
            return AcquisitionResult.Ok(uia, SelectionOrigin.Foreground);

        var before = SelectionText.Clean(input.ClipboardBefore);
        var after = SelectionText.Clean(input.ClipboardAfterCopy);
        if (input.CopyAttempted && SelectionText.Classify(after) != SelectionClass.Empty && after != before)
        {
            if (SelectionText.Classify(after) == SelectionClass.TooLong)
                return AcquisitionResult.Local(TooLongTitle, TooLongMessage);
            return AcquisitionResult.Ok(after, SelectionOrigin.SimulatedCopy);
        }
        if (SelectionText.Classify(before) == SelectionClass.TooLong)
            return AcquisitionResult.Local(TooLongTitle, TooLongMessage);
        if (SelectionText.Classify(before) == SelectionClass.Ok)
            return AcquisitionResult.Ok(before, SelectionOrigin.Clipboard);
        return AcquisitionResult.Local(EmptyTitle, EmptyMessage);
    }
}
