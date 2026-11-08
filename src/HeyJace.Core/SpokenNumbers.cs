using System;
using System.Globalization;

namespace HeyJace.Core;

// Numeric contexts deliberately accept common dictation homophones. They are
// never global text replacements: a cosmetic or literal room called FOR stays FOR.
internal static class SpokenNumbers
{
    internal static int Digit(string value)
    {
        int digit = ColorSpec.Digit(value);
        if (digit >= 0) return digit;
        return value == "to" || value == "too" ? 2 : value == "for" ? 4 : -1;
    }

    internal static bool IsToken(string value, bool homophones = true) =>
        (homophones ? Digit(value) : ColorSpec.Digit(value)) >= 0 ||
        value == "hundred" || value == "and" || Small(value) >= 10 || Tens(value) >= 0 || IsDigits(value);

    internal static bool IsDigits(string value)
    {
        if (value.Length == 0) return false;
        foreach (char character in value) if (character < '0' || character > '9') return false;
        return true;
    }

    internal static bool TryInteger(string value, out int number)
    {
        number = -1;
        if (IsDigits(value)) return value.Length <= 3 && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        string[] words = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || words.Length > 5) return false;
        int hundreds = Digit(words[0]);
        if (words.Length >= 2 && words[1] == "hundred")
        {
            if (hundreds < 1 || hundreds > 9) return false;
            if (words.Length == 2) { number = hundreds * 100; return true; }
            int offset = words[2] == "and" ? 3 : 2;
            if (!TryUnderHundred(words, offset, out int rest)) return false;
            number = hundreds * 100 + rest;
            return true;
        }
        return TryUnderHundred(words, 0, out number);
    }

    // Returns the exact digit string: zero nine five must remain 095.
    internal static bool TrySequence(string value, out string digits)
    {
        digits = "";
        string[] words = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return false;
        foreach (string word in words)
        {
            if (IsDigits(word)) digits += word;
            else
            {
                int digit = Digit(word);
                if (digit < 0) { digits = ""; return false; }
                digits += (char)('0' + digit);
            }
            if (digits.Length > 10) { digits = ""; return false; }
        }
        return digits.Length > 0;
    }

    private static bool TryUnderHundred(string[] words, int offset, out int number)
    {
        number = -1;
        int count = words.Length - offset;
        if (count < 1 || count > 2) return false;
        int small = Small(words[offset]);
        if (count == 1 && small >= 0) { number = small; return true; }
        int tens = Tens(words[offset]);
        if (tens < 0) return false;
        if (count == 1) { number = tens; return true; }
        int ones = Digit(words[offset + 1]);
        if (ones < 1) return false;
        number = tens + ones;
        return true;
    }

    private static int Small(string value)
    {
        int digit = Digit(value);
        if (digit >= 0) return digit;
        switch (value)
        {
            case "ten": return 10; case "eleven": return 11; case "twelve": return 12;
            case "thirteen": return 13; case "fourteen": return 14; case "fifteen": return 15;
            case "sixteen": return 16; case "seventeen": return 17; case "eighteen": return 18; case "nineteen": return 19;
            default: return -1;
        }
    }

    private static int Tens(string value)
    {
        switch (value)
        {
            case "twenty": return 20; case "thirty": return 30; case "forty": return 40;
            case "fifty": return 50; case "sixty": return 60; case "seventy": return 70;
            case "eighty": return 80; case "ninety": return 90; default: return -1;
        }
    }
}
