using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HeyJace.Core;

/// <summary>A game-independent color. Vanilla color digits are divided by nine.</summary>
public readonly struct ColorSpec
{
    public float R { get; }
    public float G { get; }
    public float B { get; }
    public string Label { get; }

    private ColorSpec(float r, float g, float b, string label)
    { R = r; G = g; B = b; Label = label; }

    private static readonly Dictionary<string, string> Palette = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "000000", ["white"] = "FFFFFF", ["red"] = "FF0000",
        ["green"] = "00FF00", ["blue"] = "0000FF", ["yellow"] = "FFFF00",
        ["cyan"] = "00FFFF", ["aqua"] = "00FFFF", ["magenta"] = "FF00FF",
        ["pink"] = "FF69B4", ["purple"] = "800080", ["orange"] = "FF8000",
        ["brown"] = "804000", ["gray"] = "808080", ["grey"] = "808080",
        ["lime"] = "80FF00", ["teal"] = "008080", ["navy"] = "000080",
        ["maroon"] = "800000", ["olive"] = "808000", ["gold"] = "FFD700",
        ["violet"] = "EE82EE", ["indigo"] = "4B0082", ["coral"] = "FF7F50"
    };

    public static bool TryParse(string text, out ColorSpec color)
    {
        color = default;
        if (text == null || text.Length > 64) return false;
        foreach (char c in text) if (char.IsControl(c) || c == '\u2028' || c == '\u2029') return false;
        string value = text.Trim().ToLowerInvariant();
        if (Palette.TryGetValue(value, out string? hex))
        {
            color = FromHex(hex, value == "grey" ? "gray" : value);
            return true;
        }

        bool explicitHex = value.StartsWith("hex ", StringComparison.Ordinal) || value.StartsWith("#", StringComparison.Ordinal);
        if (value.StartsWith("hex ", StringComparison.Ordinal)) value = value.Substring(4).Trim();
        if (value.StartsWith("#", StringComparison.Ordinal)) value = value.Substring(1);
        if (value.Length == 6 && IsHex(value))
        {
            color = FromHex(value, "#" + value.ToUpperInvariant());
            return true;
        }
        if (explicitHex) return false;

        string digits = value;
        if (digits.Length != 3 || !IsDigits(digits))
        {
            string[] components = Regex.Split(value, @"[\s,]+", RegexOptions.CultureInvariant);
            if (components.Length != 3) return false;
            digits = "";
            foreach (string component in components)
            {
                int digit = Digit(component);
                if (digit < 0) return false;
                digits += (char)('0' + digit);
            }
        }
        color = new ColorSpec((digits[0] - '0') / 9f, (digits[1] - '0') / 9f, (digits[2] - '0') / 9f, digits);
        return true;
    }

    internal static int Digit(string value)
    {
        if (value.Length == 1 && value[0] >= '0' && value[0] <= '9') return value[0] - '0';
        switch (value)
        {
            case "zero": case "oh": return 0;
            case "one": return 1;
            case "two": return 2;
            case "three": return 3;
            case "four": return 4;
            case "five": return 5;
            case "six": return 6;
            case "seven": return 7;
            case "eight": return 8;
            case "nine": case "niner": return 9;
            default: return -1;
        }
    }

    private static bool IsDigits(string value)
    {
        foreach (char c in value) if (c < '0' || c > '9') return false;
        return true;
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value)
            if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F')) return false;
        return true;
    }

    private static ColorSpec FromHex(string value, string label) => new ColorSpec(
        byte.Parse(value.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f,
        byte.Parse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f,
        byte.Parse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f, label);
}
