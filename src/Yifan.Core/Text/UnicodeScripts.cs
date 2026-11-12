using System.Globalization;
using System.Text;

namespace Yifan.Core.Text;

/// <summary>
/// .NET 正则没有 \p{Script=…}。.NET 8 用码位区间对齐 JS 的 Unicode 脚本判断。
/// </summary>
public static class UnicodeScripts
{
    public static bool IsLatin(Rune rune)
    {
        var v = rune.Value;
        if (v is (>= 0x0041 and <= 0x005A) or (>= 0x0061 and <= 0x007A)) return true;
        if (v is (>= 0x00C0 and <= 0x00D6) or (>= 0x00D8 and <= 0x00F6) or (>= 0x00F8 and <= 0x00FF)) return true;
        if (v is (>= 0x0100 and <= 0x024F)) return true;
        if (v is (>= 0x0250 and <= 0x02AF)) return true;
        if (v is (>= 0x1E00 and <= 0x1EFF)) return true;
        if (v is (>= 0x2C60 and <= 0x2C7F)) return true;
        if (v is (>= 0xA720 and <= 0xA7FF)) return true;
        if (v is (>= 0xAB30 and <= 0xAB6F)) return true;
        return false;
    }

    public static bool IsNumber(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.LetterNumber
            or UnicodeCategory.OtherNumber;
    }

    public static bool IsLetterOrNumber(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.LetterNumber
            or UnicodeCategory.OtherNumber;
    }

    public static bool IsHan(Rune rune)
    {
        var v = rune.Value;
        return v is (>= 0x3400 and <= 0x4DBF)
            or (>= 0x4E00 and <= 0x9FFF)
            or (>= 0xF900 and <= 0xFAFF)
            or (>= 0x20000 and <= 0x2A6DF)
            or (>= 0x2A700 and <= 0x2B73F)
            or (>= 0x2B740 and <= 0x2B81F)
            or (>= 0x2B820 and <= 0x2CEAF)
            or (>= 0x2CEB0 and <= 0x2EBEF)
            or (>= 0x30000 and <= 0x3134F);
    }

    public static bool IsHiragana(Rune rune) => rune.Value is >= 0x3040 and <= 0x309F;
    public static bool IsKatakana(Rune rune) =>
        rune.Value is (>= 0x30A0 and <= 0x30FF) or (>= 0x31F0 and <= 0x31FF) or (>= 0xFF66 and <= 0xFF9D);
    public static bool IsHangul(Rune rune) =>
        rune.Value is (>= 0x1100 and <= 0x11FF) or (>= 0xAC00 and <= 0xD7AF) or (>= 0x3130 and <= 0x318F);
    public static bool IsCyrillic(Rune rune) => rune.Value is (>= 0x0400 and <= 0x04FF) or (>= 0x0500 and <= 0x052F);
    public static bool IsArabic(Rune rune) =>
        rune.Value is (>= 0x0600 and <= 0x06FF) or (>= 0x0750 and <= 0x077F) or (>= 0x08A0 and <= 0x08FF)
            or (>= 0xFB50 and <= 0xFDFF) or (>= 0xFE70 and <= 0xFEFF);
    public static bool IsThai(Rune rune) => rune.Value is >= 0x0E00 and <= 0x0E7F;
}
