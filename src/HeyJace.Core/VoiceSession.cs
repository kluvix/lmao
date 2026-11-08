using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HeyJace.Core;

/// <summary>Consumes recognized speech; only an initial wake phrase can authorize one command.</summary>
public sealed class VoiceSession
{
    public const float MinimumConfidence = 0.65f;
    // Short dictated codes and names score lower than the fixed wake grammar.
    public const float MinimumFollowUpConfidence = 0.4f;
    public const double CommandWindowSeconds = 10;
    private const string Help = "Say that again.";
    private static readonly Regex Wake = new Regex(@"^hey[\s,]+jace(?=$|[\s,.!?:;])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ClockTime = new Regex(@"^(1[0-2]|0?[1-9]|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)(?::00)?\s*([ap])\.?\s*m\.?$", RegexOptions.CultureInvariant);
    private static readonly string[] TimeNames = { "dawn", "day", "noon", "afternoon", "sunset", "evening", "night", "midnight", "auto" };
    private static readonly Dictionary<string, char> SpokenLetters = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
    {
        ["alpha"] = 'A', ["alfa"] = 'A', ["bravo"] = 'B', ["charlie"] = 'C', ["delta"] = 'D',
        ["echo"] = 'E', ["foxtrot"] = 'F', ["golf"] = 'G', ["hotel"] = 'H', ["india"] = 'I',
        ["juliet"] = 'J', ["juliett"] = 'J', ["kilo"] = 'K', ["lima"] = 'L', ["mike"] = 'M',
        ["november"] = 'N', ["oscar"] = 'O', ["papa"] = 'P', ["quebec"] = 'Q', ["romeo"] = 'R',
        ["sierra"] = 'S', ["tango"] = 'T', ["uniform"] = 'U', ["victor"] = 'V', ["whiskey"] = 'W',
        ["whisky"] = 'W', ["xray"] = 'X', ["x-ray"] = 'X', ["yankee"] = 'Y', ["zulu"] = 'Z',
        ["ay"] = 'A', ["bee"] = 'B', ["be"] = 'B', ["see"] = 'C', ["sea"] = 'C', ["dee"] = 'D',
        ["ee"] = 'E', ["eff"] = 'F', ["gee"] = 'G', ["aitch"] = 'H', ["haitch"] = 'H',
        ["eye"] = 'I', ["jay"] = 'J', ["kay"] = 'K', ["el"] = 'L', ["ell"] = 'L', ["em"] = 'M',
        ["en"] = 'N', ["oh"] = 'O', ["pee"] = 'P', ["cue"] = 'Q', ["queue"] = 'Q', ["are"] = 'R',
        ["ess"] = 'S', ["tee"] = 'T', ["you"] = 'U', ["vee"] = 'V', ["ex"] = 'X', ["why"] = 'Y',
        ["zee"] = 'Z', ["zed"] = 'Z'
    };

    private double? lastTime;
    private double? expiresAt;
    private CommandKind? pending;

    public CommandKind? PendingKind => pending;

    // Alternate recognition hypotheses can be checked without consuming a wake
    // or pending answer. All session state is value data; Process mutates no
    // shared dictionaries or other referenced objects.
    public IReadOnlyList<VoiceEvent> Preview(string utterance, float confidence, double time) =>
        ((VoiceSession)MemberwiseClone()).Process(utterance, confidence, time);

    public bool IsAwaitingCommand(double now) => expiresAt.HasValue && !double.IsNaN(now) && !double.IsInfinity(now) &&
        (!lastTime.HasValue || now >= lastTime.Value) && now <= expiresAt.Value;

    public void Reset()
    {
        lastTime = null;
        ClearIntent();
    }

    public IReadOnlyList<VoiceEvent> Process(string utterance, float confidence, double time)
    {
        var events = new List<VoiceEvent>(2);
        if (double.IsNaN(time) || double.IsInfinity(time)) { Reset(); return events; }
        if (lastTime.HasValue && time < lastTime.Value)
        {
            ClearIntent();
            lastTime = time;
            return events;
        }
        lastTime = time;
        if (float.IsNaN(confidence) || float.IsInfinity(confidence) || confidence < 0 || confidence > 1 ||
            utterance == null || utterance.Length > 256 || HasControls(utterance))
        { ClearIntent(); return events; }
        if (expiresAt.HasValue && time > expiresAt.Value) ClearIntent();

        string text = Normalize(utterance);
        Match wake = Wake.Match(text);
        float minimum = wake.Success || !expiresAt.HasValue ? MinimumConfidence : MinimumFollowUpConfidence;
        // Uncertain background speech must not erase an authorized pending answer.
        if (confidence < minimum) return events;
        if (wake.Success)
        {
            pending = null;
            expiresAt = time + CommandWindowSeconds;
            events.Add(new VoiceEvent(VoiceEventKind.Wake));
            text = text.Substring(wake.Length).TrimStart(' ', ',', '.', '!', '?', ':', ';');
            if (text.Length == 0) return events;
        }
        else if (!expiresAt.HasValue || text.Length == 0) return events;

        if (!pending.HasValue) text = StripPolitePrefix(text);

        if (text.Equals("cancel", StringComparison.OrdinalIgnoreCase) || text.Equals("never mind", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            ClearIntent();
            events.Add(new VoiceEvent(VoiceEventKind.Prompt, "Cancelled."));
            return events;
        }

        if (pending.HasValue)
        {
            CommandKind kind = pending.Value;
            string answer = text;
            if (kind != CommandKind.Equip && kind != CommandKind.Unequip &&
                TryCommand(StripPolitePrefix(text), out CommandKind repeatedKind, out string repeatedValue) && repeatedKind == kind)
                answer = repeatedValue;
            if (TryArgument(kind, answer, out string argument))
            {
                ClearIntent();
                events.Add(new VoiceEvent(VoiceEventKind.Command, command: new JaceCommand(kind, argument)));
            }
            else
            {
                // The prompt asks for the same answer again. Preserve the
                // authorized command and give the reply a fresh full window.
                expiresAt = time + CommandWindowSeconds;
                events.Add(new VoiceEvent(VoiceEventKind.Prompt, ArgumentHelp(kind)));
            }
            return events;
        }

        if (!TryCommand(text, out CommandKind commandKind, out string value))
        {
            // Dictation can briefly return an incomplete or wrong prefix. The
            // next attempt belongs to this wake; do not recite a command list.
            expiresAt = time + CommandWindowSeconds;
            events.Add(new VoiceEvent(VoiceEventKind.Prompt, Help));
            return events;
        }
        if (value.Length == 0)
        {
            pending = commandKind;
            expiresAt = time + CommandWindowSeconds;
            events.Add(new VoiceEvent(VoiceEventKind.Prompt, ArgumentHelp(commandKind)));
        }
        else if (TryArgument(commandKind, value, out string argument))
        {
            ClearIntent();
            events.Add(new VoiceEvent(VoiceEventKind.Command, command: new JaceCommand(commandKind, argument)));
        }
        else
        {
            pending = commandKind;
            expiresAt = time + CommandWindowSeconds;
            events.Add(new VoiceEvent(VoiceEventKind.Prompt, ArgumentHelp(commandKind)));
        }
        return events;
    }

    private void ClearIntent() { expiresAt = null; pending = null; }

    private static bool TryCommand(string text, out CommandKind kind, out string value)
    {
        // Outfit is a separate command and must precede the generic "equip".
        foreach (string prefix in new[] { "equip outfit number", "equip outfit", "put on outfit number", "put on outfit", "wear outfit number", "wear outfit" })
            if (text.Equals(prefix, StringComparison.OrdinalIgnoreCase) || text.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
            {
                kind = CommandKind.EquipOutfit;
                value = text.Substring(prefix.Length).Trim();
                return true;
            }
        // Longest prefixes go first so the optional words never become part of an argument.
        string[][] prefixes =
        {
            new[] { "join the room code", "join room code", "joined room code", "join the room", "join the code", "joined code", "join code", "joined room", "join room" },
            new[]
            {
                "change my colour code to", "change my color code to", "set my colour code to", "set my color code to",
                "change my colour two", "change my color two", "set my colour two", "set my color two", "change colour two", "change color two",
                "change my colour to", "change my color to", "change my colour", "change colour to", "set my colour to",
                "change my color", "change color to", "set my color to", "change colour", "set my colour", "set colour to",
                "change color", "set my color", "set color to", "set colour", "set color"
            },
            new[] { "change the time to", "change the time", "set the time to", "change time to", "set the time", "change time", "set time to", "set time" },
            new[] { "put on", "equip", "wear" }, new[] { "take off", "unequip", "remove" }
        };
        for (int i = 0; i < prefixes.Length; i++)
            foreach (string prefix in prefixes[i])
                if (text.Equals(prefix, StringComparison.OrdinalIgnoreCase) || text.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                {
                    string candidate = text.Substring(prefix.Length).Trim();
                    // "two" can be the dictated preposition "to" or the
                    // first RGB digit. Consume it as a prefix only when what
                    // remains is already a complete three-channel color.
                    if ((CommandKind)i == CommandKind.SetColor && prefix.EndsWith(" two", StringComparison.Ordinal) &&
                        !ColorSpec.TryParse(candidate, out _)) continue;
                    kind = (CommandKind)i;
                    value = candidate;
                    return true;
                }
        kind = default;
        value = "";
        return false;
    }

    private static bool TryArgument(CommandKind kind, string text, out string argument)
    {
        argument = "";
        switch (kind)
        {
            case CommandKind.JoinRoom: return TryRoom(text, out argument);
            case CommandKind.SetColor:
                if (!ColorSpec.TryParse(text, out ColorSpec color)) return false;
                argument = color.Label;
                return true;
            case CommandKind.SetTime:
                string time = text.ToLowerInvariant();
                switch (time)
                {
                    case "morning": case "sunrise": time = "dawn"; break;
                    case "midday": time = "noon"; break;
                    case "normal": case "default": case "automatic": case "restore": case "reset": time = "auto"; break;
                }
                foreach (string name in TimeNames)
                    if (time == name) { argument = name; return true; }
                Match clock = ClockTime.Match(time);
                if (clock.Success)
                {
                    string hour = clock.Groups[1].Value;
                    int hourNumber = ColorSpec.Digit(hour);
                    if (hourNumber < 0)
                        switch (hour)
                        {
                            case "ten": hourNumber = 10; break;
                            case "eleven": hourNumber = 11; break;
                            case "twelve": hourNumber = 12; break;
                            default: hourNumber = int.Parse(hour, CultureInfo.InvariantCulture); break;
                        }
                    argument = hourNumber.ToString(CultureInfo.InvariantCulture) + clock.Groups[2].Value + "m";
                    return true;
                }
                int index = ColorSpec.Digit(time);
                if (index < 0 && !int.TryParse(time, NumberStyles.None, CultureInfo.InvariantCulture, out index)) return false;
                if (index < 0 || index > 255) return false;
                argument = index.ToString(CultureInfo.InvariantCulture);
                return true;
            case CommandKind.Equip:
            case CommandKind.Unequip:
                if (text.Length < 1 || text.Length > 64) return false;
                foreach (char c in text)
                    if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_' && c != '\'' && c != '&' && c != '(' && c != ')' && c != '.') return false;
                if (!ContainsLetterOrDigit(text)) return false;
                argument = text;
                return true;
            case CommandKind.EquipOutfit:
                return TryOutfitNumber(text, out argument);
            default: return false;
        }
    }

    private static bool TryOutfitNumber(string text, out string argument)
    {
        argument = "";
        string value = text.ToLowerInvariant();
        value = Regex.Replace(value, @"(?<=[a-z])-(?=[a-z])", " ", RegexOptions.CultureInvariant);
        if (value.StartsWith("number ", StringComparison.Ordinal)) value = value.Substring(7);
        if (!SpokenNumbers.TryInteger(value, out int number) || number < 1 || number > 999) return false;
        argument = number.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryRoom(string value, out string argument)
    {
        argument = "";
        string text = value.ToLowerInvariant().Replace(',', ' ');
        // Preserve literal single-word codes (FOR is not changed to 4) while
        // retaining the existing NATO/single-digit spelling convention.
        int singleDigit = ColorSpec.Digit(text);
        if (singleDigit >= 0) { argument = singleDigit.ToString(CultureInfo.InvariantCulture); return true; }
        if (SpokenLetters.TryGetValue(text, out char singleLetter)) { argument = singleLetter.ToString(); return true; }
        if (text.Length >= 1 && text.Length <= 10 && IsRoomLiteral(text))
        { argument = text.ToUpperInvariant(); return true; }

        text = Regex.Replace(text.Replace("double you", "w").Replace("x ray", "xray"), @"(?<=[a-z])-(?=[a-z])", " ", RegexOptions.CultureInvariant);
        string[] tokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 1 || tokens.Length > 12) return false;
        var code = new StringBuilder();
        for (int i = 0; i < tokens.Length;)
        {
            string token = tokens[i];
            if (SpokenNumbers.IsToken(token, homophones: i > 0))
            {
                int end = i + 1;
                while (end < tokens.Length && SpokenNumbers.IsToken(tokens[end])) end++;
                string numbers = string.Join(" ", tokens, i, end - i);
                if (SpokenNumbers.TrySequence(numbers, out string digits)) code.Append(digits);
                else if (SpokenNumbers.TryInteger(numbers, out int number)) code.Append(number.ToString(CultureInfo.InvariantCulture));
                else return false;
                i = end;
            }
            else if (token.Length == 1 && IsRoomLiteral(token)) { code.Append(char.ToUpperInvariant(token[0])); i++; }
            else if (SpokenLetters.TryGetValue(token, out char letter)) { code.Append(letter); i++; }
            else
            {
                // Dictation splits a mixed code into a word and numbers, e.g.
                // Jace 34 / Jace three four. Permit one initial word only and
                // require an explicit numeric suffix. Arbitrary sentences and
                // multiword commands must never be concatenated into a room.
                if (i != 0 || tokens.Length < 2 || !IsRoomLiteral(token) ||
                    token.Length > 9 || !SpokenNumbers.IsToken(tokens[1])) return false;
                code.Append(token.ToUpperInvariant());
                i++;
            }
            if (code.Length > 10) return false;
        }
        if (code.Length == 0) return false;
        argument = code.ToString();
        return true;
    }

    private static bool IsRoomLiteral(string text)
    {
        foreach (char c in text) if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9')) return false;
        return true;
    }

    private static bool ContainsLetterOrDigit(string text)
    {
        foreach (char c in text) if (char.IsLetterOrDigit(c)) return true;
        return false;
    }

    private static bool HasControls(string text)
    {
        foreach (char c in text) if (char.IsControl(c) || c == '\u2028' || c == '\u2029') return true;
        return false;
    }

    private static string StripPolitePrefix(string text)
    {
        // Only strip phrases at the start of an already authorized command.
        // Cosmetic names and literal code arguments retain their spelling.
        for (int i = 0; i < 3; i++)
        {
            string before = text;
            foreach (string prefix in new[] { "can you please ", "could you please ", "would you please ", "can you ", "could you ", "would you ", "please " })
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                { text = text.Substring(prefix.Length).TrimStart(' ', ','); break; }
            if (text == before) break;
        }
        return text;
    }

    private static string Normalize(string text) => Regex.Replace(text.Trim().Replace('\u2019', '\'').Replace('\u2018', '\'').Replace('\u2013', '-').Replace('\u2014', '-'), @"\s+", " ").TrimEnd(' ', '.', '!', '?', ',', ';', ':');

    private static string ArgumentHelp(CommandKind kind)
    {
        switch (kind)
        {
            case CommandKind.JoinRoom: return "Say the room code.";
            case CommandKind.SetColor: return "Say three color digits, red, green, blue.";
            case CommandKind.SetTime: return "Say the time, or normal.";
            case CommandKind.Equip: return "Say the cosmetic name.";
            case CommandKind.Unequip: return "Say the cosmetic name.";
            default: return "Say the outfit number.";
        }
    }
}
