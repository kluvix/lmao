using System;
using System.Text.RegularExpressions;

namespace HeyJace.Core;

/// <summary>The game's three color digits, red then green then blue, each divided by nine.</summary>
public readonly struct ColorSpec
{
    public float R { get; }
    public float G { get; }
    public float B { get; }
    public string Label { get; }

    private ColorSpec(float r, float g, float b, string label)
    { R = r; G = g; B = b; Label = label; }

    public static bool TryParse(string text, out ColorSpec color)
    {
        color = default;
        if (text == null || text.Length > 64) return false;
        foreach (char c in text) if (char.IsControl(c) || c == '\u2028' || c == '\u2029') return false;
        string value = text.Trim().ToLowerInvariant();
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
            case "zero": case "nought": case "naught": case "oh": return 0;
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
}
