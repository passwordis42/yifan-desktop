namespace Yifan.Core.Shortcuts;

public static class ShortcutIds
{
    public const string Selection = "selection";
    public const string Crop = "crop";
    public static readonly string[] All = [Selection, Crop];
}

public static class ShortcutLabels
{
    public const string Selection = "划词翻译";
    public const string Crop = "截图识译";

    public static string Of(string id) => id == ShortcutIds.Crop ? Crop : Selection;
}

public sealed class DesktopShortcuts
{
    public string Selection { get; set; } = "Alt+E";
    public string Crop { get; set; } = "Alt+S";

    public string this[string id] => id == ShortcutIds.Crop ? Crop : Selection;

    public static DesktopShortcuts Default() => new();

    public DesktopShortcuts Clone() => new() { Selection = Selection, Crop = Crop };

    public static DesktopShortcuts Normalize(DesktopShortcuts? raw)
    {
        var defaults = Default();
        var next = Default();
        var used = new HashSet<string>();
        foreach (var id in ShortcutIds.All)
        {
            var text = raw is null ? null : id == ShortcutIds.Crop ? raw.Crop : raw.Selection;
            var parsed = text is null ? null : Hotkeys.Parse(text);
            var formatted = parsed is null ? null : Hotkeys.Format(parsed);
            if (formatted is not null && used.Add(formatted))
            {
                SetChord(next, id, formatted);
                continue;
            }
            var free = ShortcutIds.All
                .Select(slot => slot == ShortcutIds.Crop ? defaults.Crop : defaults.Selection)
                .FirstOrDefault(value => !used.Contains(value)) ?? (id == ShortcutIds.Crop ? defaults.Crop : defaults.Selection);
            SetChord(next, id, free);
            used.Add(free);
        }
        return next;
    }

    public static (bool Ok, DesktopShortcuts Shortcuts, string Message) Assign(DesktopShortcuts current, string id, string raw)
    {
        var parsed = Hotkeys.Parse(raw);
        if (parsed is null)
        {
            return (false, current, "请按下修饰键和主键，例如 Alt+E。当前键没有改。");
        }
        var formatted = Hotkeys.Format(parsed);
        foreach (var other in ShortcutIds.All)
        {
            if (other == id) continue;
            var existing = other == ShortcutIds.Crop ? current.Crop : current.Selection;
            if (existing == formatted)
            {
                return (false, current, $"这个组合键已经用于「{ShortcutLabels.Of(other)}」。没有改掉当前键。");
            }
        }
        var next = current.Clone();
        SetChord(next, id, formatted);
        return (true, next, "");
    }

    private static void SetChord(DesktopShortcuts target, string id, string formatted)
    {
        if (id == ShortcutIds.Crop) target.Crop = formatted;
        else target.Selection = formatted;
    }
}

public sealed record HotkeyChord(bool Ctrl, bool Alt, bool Shift, bool Meta, string Key);

public sealed record KeySnapshot(string Key, string Code, bool AltKey, bool CtrlKey, bool ShiftKey, bool MetaKey, bool Repeat = false);

public static class Hotkeys
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    private static readonly string[] NamedKeys =
    [
        "Enter", "Tab", "Space", "Backspace", "Delete", "Home", "End", "PageUp", "PageDown",
        "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight",
    ];

    private static readonly Dictionary<string, string> Punctuation = new()
    {
        ["Minus"] = "-",
        ["Equal"] = "=",
        ["BracketLeft"] = "[",
        ["BracketRight"] = "]",
        ["Backslash"] = "\\",
        ["Semicolon"] = ";",
        ["Quote"] = "'",
        ["Comma"] = ",",
        ["Period"] = ".",
        ["Slash"] = "/",
        ["Backquote"] = "`",
    };

    private static readonly HashSet<string> Allowed = BuildAllowed();

    public static string Format(HotkeyChord chord)
    {
        var parts = new List<string>();
        if (chord.Ctrl) parts.Add("Ctrl");
        if (chord.Alt) parts.Add("Alt");
        if (chord.Shift) parts.Add("Shift");
        if (chord.Meta) parts.Add("Cmd");
        parts.Add(chord.Key);
        return string.Join("+", parts);
    }

    public static HotkeyChord? Parse(string raw)
    {
        var parts = raw.Split('+').Select(part => part.Trim()).Where(part => part.Length > 0).ToList();
        if (parts.Count < 2) return null;
        var ctrl = false;
        var alt = false;
        var shift = false;
        var meta = false;
        var keys = new List<string>();
        foreach (var part in parts)
        {
            var upper = part.ToUpperInvariant();
            if (upper is "CTRL" or "CONTROL") ctrl = true;
            else if (upper is "ALT" or "OPTION") alt = true;
            else if (upper == "SHIFT") shift = true;
            else if (upper is "CMD" or "META" or "COMMAND" or "WIN") meta = true;
            else keys.Add(NormalizeKeyToken(part));
        }
        if (keys.Count != 1) return null;
        var key = keys[0];
        if (!Allowed.Contains(key)) return null;
        if (!ctrl && !alt && !meta) return null;
        return new HotkeyChord(ctrl, alt, shift, meta, key);
    }

    public static HotkeyChord? FromKey(KeySnapshot snapshot)
    {
        if (snapshot.Repeat) return null;
        if (IsModifier(snapshot)) return null;
        var key = TokenFromEvent(snapshot);
        if (key is null || !Allowed.Contains(key)) return null;
        if (!snapshot.CtrlKey && !snapshot.AltKey && !snapshot.MetaKey) return null;
        return new HotkeyChord(snapshot.CtrlKey, snapshot.AltKey, snapshot.ShiftKey, snapshot.MetaKey, key);
    }

    public static string? Match(KeySnapshot snapshot, DesktopShortcuts shortcuts)
    {
        var chord = FromKey(snapshot);
        if (chord is null) return null;
        var formatted = Format(chord);
        if (shortcuts.Selection == formatted) return ShortcutIds.Selection;
        if (shortcuts.Crop == formatted) return ShortcutIds.Crop;
        return null;
    }

    public static (uint Modifiers, uint VirtualKey)? ToWin32(string chord)
    {
        var parsed = Parse(chord);
        if (parsed is null) return null;
        var vk = VirtualKey(parsed.Key);
        if (vk == 0) return null;
        uint mod = ModNoRepeat;
        if (parsed.Ctrl) mod |= ModControl;
        if (parsed.Alt) mod |= ModAlt;
        if (parsed.Shift) mod |= ModShift;
        if (parsed.Meta) mod |= ModWin;
        return (mod, vk);
    }

    public static KeySnapshot? FromVirtualKey(uint vk, bool alt, bool ctrl, bool shift, bool meta, bool repeat)
    {
        var (key, code) = FromVk(vk);
        if (key is null || code is null) return null;
        return new KeySnapshot(key, code, alt, ctrl, shift, meta, repeat);
    }

    public static RebindKind ClassifyRebind(KeySnapshot snapshot)
    {
        if (snapshot.Key == "Escape" && !snapshot.AltKey && !snapshot.CtrlKey && !snapshot.ShiftKey && !snapshot.MetaKey)
            return RebindKind.Cancel;
        return FromKey(snapshot) is null ? RebindKind.Ignore : RebindKind.Accept;
    }

    private static uint VirtualKey(string key)
    {
        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z') return key[0];
        if (key.Length == 1 && key[0] is >= '0' and <= '9') return key[0];
        if (key.StartsWith('F') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 12) return (uint)(0x70 + f - 1);
        if (key.StartsWith("Numpad", StringComparison.Ordinal) && key.Length == 7 && key[6] is >= '0' and <= '9')
            return (uint)(0x60 + (key[6] - '0'));
        return key switch
        {
            "Space" => 0x20,
            "Enter" => 0x0D,
            "Tab" => 0x09,
            "Backspace" => 0x08,
            "Delete" => 0x2E,
            "Home" => 0x24,
            "End" => 0x23,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            "ArrowLeft" => 0x25,
            "ArrowUp" => 0x26,
            "ArrowRight" => 0x27,
            "ArrowDown" => 0x28,
            "-" => 0xBD,
            "=" => 0xBB,
            "[" => 0xDB,
            "]" => 0xDD,
            "\\" => 0xDC,
            ";" => 0xBA,
            "'" => 0xDE,
            "," => 0xBC,
            "." => 0xBE,
            "/" => 0xBF,
            "`" => 0xC0,
            _ => 0,
        };
    }

    private static (string? Key, string? Code) FromVk(uint vk)
    {
        if (vk is >= 0x41 and <= 0x5A)
        {
            var letter = ((char)vk).ToString();
            return (letter, "Key" + letter);
        }
        if (vk is >= 0x30 and <= 0x39)
        {
            var digit = ((char)vk).ToString();
            return (digit, "Digit" + digit);
        }
        if (vk is >= 0x70 and <= 0x7B) return ("F" + (vk - 0x70 + 1), "F" + (vk - 0x70 + 1));
        if (vk is >= 0x60 and <= 0x69) return ("Numpad" + (vk - 0x60), "Numpad" + (vk - 0x60));
        return vk switch
        {
            0x20 => ("Space", "Space"),
            0x0D => ("Enter", "Enter"),
            0x09 => ("Tab", "Tab"),
            0x08 => ("Backspace", "Backspace"),
            0x2E => ("Delete", "Delete"),
            0x24 => ("Home", "Home"),
            0x23 => ("End", "End"),
            0x21 => ("PageUp", "PageUp"),
            0x22 => ("PageDown", "PageDown"),
            0x25 => ("ArrowLeft", "ArrowLeft"),
            0x26 => ("ArrowUp", "ArrowUp"),
            0x27 => ("ArrowRight", "ArrowRight"),
            0x28 => ("ArrowDown", "ArrowDown"),
            0xBD => ("-", "Minus"),
            0xBB => ("=", "Equal"),
            0xDB => ("[", "BracketLeft"),
            0xDD => ("]", "BracketRight"),
            0xDC => ("\\", "Backslash"),
            0xBA => (";", "Semicolon"),
            0xDE => ("'", "Quote"),
            0xBC => (",", "Comma"),
            0xBE => (".", "Period"),
            0xBF => ("/", "Slash"),
            0xC0 => ("`", "Backquote"),
            0x1B => ("Escape", "Escape"),
            0x10 or 0xA0 or 0xA1 => ("Shift", "Shift"),
            0x11 or 0xA2 or 0xA3 => ("Control", "Control"),
            0x12 or 0xA4 or 0xA5 => ("Alt", "Alt"),
            0x5B or 0x5C => ("Meta", "Meta"),
            _ => (null, null),
        };
    }

    private static HashSet<string> BuildAllowed()
    {
        var set = new HashSet<string>(NamedKeys);
        foreach (var value in Punctuation.Values) set.Add(value);
        for (var c = 'A'; c <= 'Z'; c++) set.Add(c.ToString());
        for (var c = '0'; c <= '9'; c++) set.Add(c.ToString());
        for (var i = 1; i <= 12; i++) set.Add("F" + i);
        for (var i = 0; i <= 9; i++) set.Add("Numpad" + i);
        return set;
    }

    private static string NormalizeKeyToken(string part)
    {
        if (part.Length == 1 && char.IsAsciiLetter(part[0])) return part.ToUpperInvariant();
        if (System.Text.RegularExpressions.Regex.IsMatch(part, @"^f([1-9]|1[0-2])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return part.ToUpperInvariant();
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["space"] = "Space",
            ["enter"] = "Enter",
            ["return"] = "Enter",
            ["tab"] = "Tab",
            ["backspace"] = "Backspace",
            ["delete"] = "Delete",
            ["del"] = "Delete",
            ["home"] = "Home",
            ["end"] = "End",
            ["pageup"] = "PageUp",
            ["pagedown"] = "PageDown",
            ["arrowup"] = "ArrowUp",
            ["arrowdown"] = "ArrowDown",
            ["arrowleft"] = "ArrowLeft",
            ["arrowright"] = "ArrowRight",
            ["up"] = "ArrowUp",
            ["down"] = "ArrowDown",
            ["left"] = "ArrowLeft",
            ["right"] = "ArrowRight",
        };
        if (aliases.TryGetValue(part, out var named)) return named;
        if (System.Text.RegularExpressions.Regex.IsMatch(part, @"^numpad[0-9]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return "Numpad" + part[^1];
        return part;
    }

    private static string? TokenFromEvent(KeySnapshot snapshot)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(snapshot.Code, @"^Key[A-Z]$")) return snapshot.Code[3..];
        if (System.Text.RegularExpressions.Regex.IsMatch(snapshot.Code, @"^Digit[0-9]$")) return snapshot.Code[5..];
        if (System.Text.RegularExpressions.Regex.IsMatch(snapshot.Code, @"^Numpad[0-9]$")) return snapshot.Code;
        if (System.Text.RegularExpressions.Regex.IsMatch(snapshot.Code, @"^F([1-9]|1[0-2])$")) return snapshot.Code;
        if (Punctuation.TryGetValue(snapshot.Code, out var punct)) return punct;
        if (NamedKeys.Contains(snapshot.Code)) return snapshot.Code;
        if (snapshot.Code == "NumpadEnter") return "Enter";
        if (snapshot.Key.Length == 1 && char.IsAsciiLetterOrDigit(snapshot.Key[0])) return snapshot.Key.ToUpperInvariant();
        return null;
    }

    private static bool IsModifier(KeySnapshot snapshot) =>
        System.Text.RegularExpressions.Regex.IsMatch(snapshot.Key, @"^(Alt|Control|Shift|Meta|OS)(Left|Right)?$")
        || System.Text.RegularExpressions.Regex.IsMatch(snapshot.Code, @"^(Alt|Control|Shift|Meta|OS)(Left|Right)$");
}

public enum RebindKind
{
    Ignore,
    Cancel,
    Accept,
}
