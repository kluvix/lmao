using System.Text;

namespace HeyJace.Speech;

// Spoken forms are recognition hints only. Semantic results always carry the
// original owned wardrobe label, including its punctuation and numeric spelling.
internal static class CosmeticSpeechAliases
{
    private const int MaximumNames = 4096;
    private const int MaximumAliasLength = 96;
    private static readonly string[] Digits =
        { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };
    private static readonly string[] CosmeticSuffixes =
        { "HEADPHONES", "HEADBAND", "BACKPACK", "NECKLACE", "UMBRELLA", "GLASSES", "EARRINGS", "GLOVES", "SHIRT", "BADGE", "BOOTS", "MASK", "EARS", "HAT", "CAP", "BOW" };

    internal static IReadOnlyDictionary<string, string> Build(IEnumerable<string> canonicalNames)
    {
        ArgumentNullException.ThrowIfNull(canonicalNames);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in canonicalNames)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) ||
                name.IndexOfAny(new[] { '\u2028', '\u2029', '|' }) >= 0) continue;
            names.Add(name);
            if (names.Count == MaximumNames) break;
        }

        var aliases = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (string canonical in names.OrderBy(name => name, StringComparer.Ordinal))
        {
            string normal = Normalize(canonical);
            // At most two forms per item: normalized spelling and spoken digits.
            string spaced = Normalize(SpaceSuffix(canonical));
            string[] candidates = { normal, spaced == normal ? ExpandNumbers(normal) : ExpandNumbers(spaced) };
            foreach (string alias in candidates.Distinct(StringComparer.Ordinal))
            {
                if (alias.Length is < 1 or > MaximumAliasLength || !alias.Any(char.IsLetterOrDigit) || ambiguous.Contains(alias)) continue;
                if (!aliases.TryGetValue(alias, out string? previous)) aliases.Add(alias, canonical);
                else if (!string.Equals(previous, canonical, StringComparison.OrdinalIgnoreCase))
                {
                    aliases.Remove(alias);
                    ambiguous.Add(alias);
                }
            }
        }
        return aliases;
    }

    private static string Normalize(string name)
    {
        var result = new StringBuilder();
        for (int index = 0; index < name.Length; index++)
        {
            char current = name[index];
            if (current is '\'' or '\u2018' or '\u2019' or '\u02bc' &&
                index > 0 && index + 1 < name.Length && char.IsLetter(name[index - 1]) && char.IsLetter(name[index + 1]))
            {
                // Removing an internal apostrophe keeps the word pronounceable.
                continue;
            }
            if (char.IsLetterOrDigit(current))
            {
                if (index > 0)
                {
                    char previous = name[index - 1];
                    bool numericBoundary = char.IsLetter(previous) && char.IsDigit(current) ||
                        char.IsDigit(previous) && char.IsLetter(current);
                    bool wordBoundary = char.IsUpper(current) && (char.IsLower(previous) ||
                        char.IsUpper(previous) && index + 1 < name.Length && char.IsLower(name[index + 1]));
                    if (numericBoundary || wordBoundary) Space(result);
                }
                result.Append(char.ToLowerInvariant(current));
            }
            else if (current == '&')
            {
                Space(result);
                result.Append("and ");
            }
            else Space(result);
        }
        return result.ToString().Trim();
    }

    private static string SpaceSuffix(string name)
    {
        foreach (string suffix in CosmeticSuffixes)
        {
            int prefixLength = name.Length - suffix.Length;
            if (prefixLength < 2 || !name.EndsWith(suffix, StringComparison.Ordinal)) continue;
            string prefix = name.Substring(0, prefixLength);
            if (prefix.Any(character => !char.IsUpper(character) && !char.IsDigit(character))) continue;
            return prefix + " " + suffix;
        }
        return name;
    }

    private static void Space(StringBuilder result)
    {
        if (result.Length > 0 && result[result.Length - 1] != ' ') result.Append(' ');
    }

    private static string ExpandNumbers(string alias)
    {
        return string.Join(" ", alias.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(token =>
        {
            if (!token.All(character => character is >= '0' and <= '9')) return token;
            return string.Join(" ", token.Select(character => Digits[character - '0']));
        }));
    }
}
