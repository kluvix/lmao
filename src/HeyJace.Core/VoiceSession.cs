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
    public const double CommandWindowSeconds = 10;
    private const string Help = "Try: join code BANANA, change my color to red, change the time to night, equip an item, or unequip an item.";
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
        if (float.IsNaN(confidence) || float.IsInfinity(confidence) || confidence < MinimumConfidence || confidence > 1 ||
            utterance == null || utterance.Length > 256 || HasControls(utterance))
        { ClearIntent(); return events; }
        if (expiresAt.HasValue && time > expiresAt.Value) ClearIntent();

        string text = Normalize(utterance);
        Match wake = Wake.Match(text);
        if (wake.Success)
        {
            pending = null;
            expiresAt = time + CommandWindowSeconds;
            events.Add(new VoiceEvent(VoiceEventKind.Wake));
            text = text.Substring(wake.Length).TrimStart(' ', ',', '.', '!', '?', ':', ';');
            if (text.Length == 0) return events;
        }
        else if (!expiresAt.HasValue || text.Length == 0) return events;

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
            if (TryArgument(kind, text, out string argument))
            {
                ClearIntent();
                events.Add(new VoiceEvent(VoiceEventKind.Command, command: new JaceCommand(kind, argument)));
            }
            else
            {
                ClearIntent();
                events.Add(new VoiceEvent(VoiceEventKind.Prompt, ArgumentHelp(kind)));
            }
            return events;
        }

        if (!TryCommand(text, out CommandKind commandKind, out string value))
        {
            ClearIntent();
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
            ClearIntent();
            events.Add(new VoiceEvent(VoiceEventKind.Prompt, ArgumentHelp(commandKind)));
        }
        return events;
    }

    private void ClearIntent() { expiresAt = null; pending = null; }

    private static bool TryCommand(string text, out CommandKind kind, out string value)
    {
        // Longest prefixes go first so the optional words never become part of an argument.
        string[][] prefixes =
        {
            new[] { "join the room code", "join room code", "join the room", "join the code", "join code", "join room" },
            new[]
            {
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
                    kind = (CommandKind)i;
                    value = text.Substring(prefix.Length).Trim();
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
                    if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_' && c != '\'') return false;
                if (!ContainsLetterOrDigit(text)) return false;
                argument = text;
                return true;
            default: return false;
        }
    }

    private static bool TryRoom(string value, out string argument)
    {
        argument = "";
        string text = value.ToLowerInvariant();
        // Spoken digits and NATO words have priority; other contiguous words preserve a literal code such as BANANA.
        int singleDigit = ColorSpec.Digit(text);
        if (singleDigit >= 0) { argument = singleDigit.ToString(CultureInfo.InvariantCulture); return true; }
        if (SpokenLetters.TryGetValue(text, out char singleLetter)) { argument = singleLetter.ToString(); return true; }
        if (text.Length >= 1 && text.Length <= 10 && IsRoomLiteral(text))
        { argument = text.ToUpperInvariant(); return true; }

        text = text.Replace("double you", "w").Replace("x ray", "xray");
        string[] tokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var code = new StringBuilder();
        foreach (string token in tokens)
        {
            int digit = ColorSpec.Digit(token);
            if (digit >= 0) code.Append((char)('0' + digit));
            else if (token.Length == 1 && IsRoomLiteral(token)) code.Append(char.ToUpperInvariant(token[0]));
            else if (SpokenLetters.TryGetValue(token, out char letter)) code.Append(letter);
            else return false;
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

    private static string Normalize(string text) => Regex.Replace(text.Trim(), @"\s+", " ").TrimEnd(' ', '.', '!', '?', ',', ';', ':');

    private static string ArgumentHelp(CommandKind kind)
    {
        switch (kind)
        {
            case CommandKind.JoinRoom: return "Say a room code of 1 to 10 letters or digits; you can spell it with NATO words.";
            case CommandKind.SetColor: return "Say a color name, three digits from zero to nine, or a six-digit hex color.";
            case CommandKind.SetTime: return "Say morning, day, noon, afternoon, sunset, evening, night, midnight, auto, a clock hour such as six AM, or a preset index.";
            case CommandKind.Equip: return "Say the name of an owned cosmetic to equip.";
            default: return "Say the name of a cosmetic to unequip.";
        }
    }
}
