using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace SensConverter.Core;

/// <summary>
/// A global hotkey, parsed from text like "F8" or "Ctrl+Shift+F9" into the modifier flags and
/// virtual-key code that RegisterHotKey wants. Kept free of WinForms so it can be unit tested.
/// </summary>
public sealed class Hotkey : IEquatable<Hotkey>
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    private Hotkey(uint modifiers, uint virtualKey, string display)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        Display = display;
    }

    public uint Modifiers { get; }

    public uint VirtualKey { get; }

    /// <summary>Canonical text form, e.g. "Ctrl+F8".</summary>
    public string Display { get; }

    public static Hotkey Default { get; } = new Hotkey(0, 0x77, "F8");

    public static bool TryParse(string? text, [NotNullWhen(true)] out Hotkey? hotkey, out string error)
    {
        hotkey = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "No key given.";
            return false;
        }

        string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "No key given.";
            return false;
        }

        uint modifiers = 0;
        string? keyName = null;

        foreach (string part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModWin;
                    break;
                default:
                    if (keyName is not null)
                    {
                        error = "Only one non-modifier key is allowed.";
                        return false;
                    }

                    keyName = part;
                    break;
            }
        }

        if (keyName is null)
        {
            error = "A modifier on its own is not a hotkey - add a key such as F8.";
            return false;
        }

        if (!KeyCodes.TryGetValue(keyName, out uint vk))
        {
            error = "Unrecognised key: " + keyName;
            return false;
        }

        hotkey = new Hotkey(modifiers, vk, BuildDisplay(modifiers, keyName));
        return true;
    }

    public static Hotkey ParseOrDefault(string? text)
        => TryParse(text, out Hotkey? hotkey, out _) ? hotkey : Default;

    private static string BuildDisplay(uint modifiers, string keyName)
    {
        List<string> parts = new();
        if ((modifiers & ModControl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & ModAlt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & ModShift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & ModWin) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(CanonicalKeyName(keyName));
        return string.Join("+", parts);
    }

    private static string CanonicalKeyName(string keyName)
    {
        foreach (string name in KeyCodes.Keys)
        {
            if (string.Equals(name, keyName, StringComparison.OrdinalIgnoreCase))
            {
                return name.Length <= 3
                    ? name.ToUpperInvariant()
                    : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
            }
        }

        return keyName.ToUpperInvariant();
    }

    /// <summary>Key names this program accepts, mapped to Windows virtual-key codes.</summary>
    public static IReadOnlyDictionary<string, uint> KeyCodes { get; } = BuildKeyCodes();

    private static Dictionary<string, uint> BuildKeyCodes()
    {
        Dictionary<string, uint> map = new(StringComparer.OrdinalIgnoreCase);

        for (uint i = 1; i <= 24; i++)
        {
            map["F" + i.ToString(CultureInfo.InvariantCulture)] = 0x70 + (i - 1);
        }

        for (char c = 'A'; c <= 'Z'; c++)
        {
            map[c.ToString()] = c;
        }

        for (char c = '0'; c <= '9'; c++)
        {
            map[c.ToString()] = c;
        }

        for (uint i = 0; i <= 9; i++)
        {
            map["Numpad" + i.ToString(CultureInfo.InvariantCulture)] = 0x60 + i;
        }

        map["Space"] = 0x20;
        map["Tab"] = 0x09;
        map["Enter"] = 0x0D;
        map["Backspace"] = 0x08;
        map["Insert"] = 0x2D;
        map["Delete"] = 0x2E;
        map["Home"] = 0x24;
        map["End"] = 0x23;
        map["PageUp"] = 0x21;
        map["PageDown"] = 0x22;
        map["Pause"] = 0x13;
        map["ScrollLock"] = 0x91;
        map["NumLock"] = 0x90;
        map["Up"] = 0x26;
        map["Down"] = 0x28;
        map["Left"] = 0x25;
        map["Right"] = 0x27;
        map["Multiply"] = 0x6A;
        map["Add"] = 0x6B;
        map["Subtract"] = 0x6D;
        map["Divide"] = 0x6F;
        map["Decimal"] = 0x6E;

        return map;
    }

    public bool Equals(Hotkey? other)
        => other is not null && other.Modifiers == Modifiers && other.VirtualKey == VirtualKey;

    public override bool Equals(object? obj) => Equals(obj as Hotkey);

    public override int GetHashCode() => HashCode.Combine(Modifiers, VirtualKey);

    public override string ToString() => Display;
}
