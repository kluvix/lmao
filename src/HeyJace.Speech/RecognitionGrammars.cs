using System.Globalization;
using System.Speech.Recognition;
using HeyJace.Core;

namespace HeyJace.Speech;

internal sealed class RecognitionGrammars
{
    private readonly SpeechRecognitionEngine _recognizer;
    private readonly CultureInfo _culture;
    private readonly List<Grammar> _activeCommands = new();
    private readonly List<Grammar> _colorAnswers = new(), _outfitAnswers = new(), _equipAnswers = new(), _unequipAnswers = new(), _timeAnswers = new(), _roomAnswers = new(), _micAnswers = new();
    private readonly List<Grammar> _cosmeticGrammars = new();

    internal RecognitionGrammars(SpeechRecognitionEngine recognizer, CultureInfo culture)
    { _recognizer = recognizer; _culture = culture; }

    internal void Load()
    {
        LoadPhrase(new[] { "hey jace" }, "Wake phrase", priority: 4);
        string[] prefixes =
        {
            "join room", "join the room", "join code", "join room code", "set color", "set my color to", "change color", "change my color to", "change my colour to",
            "set time", "set the time to", "change the time to", "equip outfit", "equip outfit number", "quip outfit", "quipe outfit", "equip", "quip", "quipe", "put on", "wear", "unequip", "unequipe", "unquip", "take off", "remove",
            "set name to", "set my name to", "change my name to", "set mic to", "set my mic to", "set microphone to", "cancel"
        };
        LoadPhrase(prefixes.Select(text => "hey jace " + text), "Wake commands", priority: 0);
        LoadPhrase(prefixes, "Active commands", _activeCommands, priority: 0);
        string[] rooms = { "join code", "join room", "join room code", "join the room", "join the code" };
        AddStructured("Wake spelled room code", () => RoomCommand(rooms, true), priority: 8);
        AddStructured("Active spelled room code", () => RoomCommand(rooms, false), _activeCommands, priority: 8);
        AddStructured("Room code answer", RoomCode, _roomAnswers, priority: 9);
        string[] colors = { "change my color to", "change my colour to", "change color to", "set my color to", "set color to" };
        AddStructured("Wake numeric color", () => ColorCommand(colors, true), priority: 8);
        AddStructured("Active numeric color", () => ColorCommand(colors, false), _activeCommands, priority: 8);
        AddStructured("Three color digits", Digits, _colorAnswers, priority: 9);
        string[] outfits = { "equip outfit number", "equip outfit", "quip outfit", "quipe outfit", "equip out fit", "wear outfit", "put on outfit" };
        AddStructured("Wake outfit", () => OutfitCommand(outfits, true), priority: 12);
        AddStructured("Active outfit", () => OutfitCommand(outfits, false), _activeCommands, priority: 12);
        AddStructured("Outfit number", OutfitNumber, _outfitAnswers, priority: 9);
        string[] namePrefixes = { "set name to", "set my name to", "change my name to", "change name to" };
        AddStructured("Wake player name", () => DictatedCommand(namePrefixes, true), priority: 8);
        AddStructured("Active player name", () => DictatedCommand(namePrefixes, false), _activeCommands, priority: 8);
        string[] micPrefixes = { "set mic to", "set my mic to", "set microphone to", "set my microphone to", "change mic to" };
        AddStructured("Wake microphone mode", () => NamedCommand(micPrefixes, MicModes(), true), priority: 10);
        AddStructured("Active microphone mode", () => NamedCommand(micPrefixes, MicModes(), false), _activeCommands, priority: 10);
        LoadPhrase(MicModes(), "Microphone mode answer", _micAnswers, priority: 10);

        string[] times = { "dawn", "morning", "sunrise", "day", "noon", "midday", "afternoon", "sunset", "evening", "night", "midnight", "auto", "normal", "default", "reset" };
        LoadPhrase(times, "Time answer", _timeAnswers);
        var timePrefixes = new[] { "change the time to", "set the time to", "change time to", "set time to" };
        AddStructured("Wake time", () => NamedCommand(timePrefixes, times, true), priority: 8);
        AddStructured("Active time", () => NamedCommand(timePrefixes, times, false), _activeCommands, priority: 8);
        // Dictation tails keep arbitrary codes/new names in the command grammar
        // instead of forcing the entire wake phrase through free dictation.
        // Outfit and opposite cosmetic actions have their own grammar rather
        // than sharing one broad equip/unequip dictation branch.
        string[] generic = prefixes.Where(value => value != "cancel" && !IsCosmeticPrefix(value) && !value.Contains("outfit", StringComparison.Ordinal)).ToArray();
        AddStructured("Wake arguments", () => DictatedCommand(generic, true));
        AddStructured("Active arguments", () => DictatedCommand(generic, false), _activeCommands);
        AddStructured("Wake equip dictation", () => DictatedCommand(EquipPrefixes(), true), priority: 1);
        AddStructured("Active equip dictation", () => DictatedCommand(EquipPrefixes(), false), _activeCommands, priority: 1);
        AddStructured("Wake unequip dictation", () => DictatedCommand(UnequipPrefixes(), true), priority: 1);
        AddStructured("Active unequip dictation", () => DictatedCommand(UnequipPrefixes(), false), _activeCommands, priority: 1);
        _recognizer.LoadGrammar(new DictationGrammar { Name = "Fallback dictation", Weight = .7f });
        SetState(false, null);
    }

    internal void ReplaceCosmetics(string[] names)
    {
        // Caller has stopped recognition before modifying loaded grammars.
        foreach (Grammar grammar in _cosmeticGrammars)
        {
            _activeCommands.Remove(grammar); _equipAnswers.Remove(grammar); _unequipAnswers.Remove(grammar);
            _recognizer.UnloadGrammar(grammar);
        }
        _cosmeticGrammars.Clear();
        if (names.Length == 0) return;
        IReadOnlyDictionary<string, string> aliases = CosmeticSpeechAliases.Build(names);
        if (aliases.Count == 0) return;
        foreach (CommandKind intent in new[] { CommandKind.Equip, CommandKind.Unequip })
        {
            string[] prefixes = intent == CommandKind.Equip ? EquipPrefixes() : UnequipPrefixes();
            Grammar? wake = AddStructured("Owned " + intent + " wake names", () => CosmeticCommand(prefixes, aliases, intent, true), priority: 8);
            Grammar? active = AddStructured("Owned " + intent + " command names", () => CosmeticCommand(prefixes, aliases, intent, false), _activeCommands, priority: 8);
            Grammar? answer = AddStructured("Owned " + intent + " answer names", () => CosmeticAnswer(aliases, intent), intent == CommandKind.Equip ? _equipAnswers : _unequipAnswers, priority: 10);
            foreach (Grammar? grammar in new[] { wake, active, answer }) if (grammar != null) _cosmeticGrammars.Add(grammar);
        }
    }

    internal void SetState(bool active, CommandKind? pending)
    {
        SetEnabled(_activeCommands, active);
        SetEnabled(_colorAnswers, active && pending == CommandKind.SetColor);
        SetEnabled(_outfitAnswers, active && pending == CommandKind.EquipOutfit);
        SetEnabled(_equipAnswers, active && pending == CommandKind.Equip);
        SetEnabled(_unequipAnswers, active && pending == CommandKind.Unequip);
        SetEnabled(_timeAnswers, active && pending == CommandKind.SetTime);
        SetEnabled(_roomAnswers, active && pending == CommandKind.JoinRoom);
        SetEnabled(_micAnswers, active && pending == CommandKind.SetMic);
    }

    private static void SetEnabled(IEnumerable<Grammar> grammars, bool enabled)
    { foreach (Grammar grammar in grammars) if (grammar.Enabled != enabled) grammar.Enabled = enabled; }

    private GrammarBuilder Builder() => new() { Culture = _culture };

    internal static string[] EquipPrefixes() => new[] { "equip cosmetic", "put on cosmetic", "equip", "quip", "quipe", "put on", "wear" };
    internal static string[] UnequipPrefixes() => new[] { "unequip cosmetic", "take off cosmetic", "unequip", "unequipe", "unquip", "unquipe", "take off", "remove" };
    internal static string[] MicModes() => new[] { "open mic", "push to mute", "push to talk" };
    private static bool IsCosmeticPrefix(string prefix) => EquipPrefixes().Contains(prefix) || UnequipPrefixes().Contains(prefix);

    private GrammarBuilder CosmeticNames(IReadOnlyDictionary<string, string> aliases)
    {
        var choices = new Choices();
        foreach (KeyValuePair<string, string> alias in aliases)
            choices.Add(new GrammarBuilder(new SemanticResultValue(alias.Key, alias.Value)));
        return new GrammarBuilder(choices) { Culture = _culture };
    }

    private GrammarBuilder CosmeticCommand(string[] prefixes, IReadOnlyDictionary<string, string> aliases, CommandKind intent, bool wake)
    {
        var builder = Builder();
        if (wake) builder.Append("hey jace");
        var prefix = Builder(); prefix.Append(new Choices(prefixes));
        builder.Append(new SemanticResultKey("jaceIntent", new GrammarBuilder(new SemanticResultValue(prefix, intent.ToString())) { Culture = _culture }));
        builder.Append(new SemanticResultKey("jaceArgument", CosmeticNames(aliases)));
        return builder;
    }

    private GrammarBuilder CosmeticAnswer(IReadOnlyDictionary<string, string> aliases, CommandKind intent)
    {
        var argument = Builder();
        argument.Append(new SemanticResultKey("jaceArgument", CosmeticNames(aliases)));
        var builder = Builder();
        builder.Append(new SemanticResultKey("jaceIntent", new GrammarBuilder(new SemanticResultValue(argument, intent.ToString())) { Culture = _culture }));
        return builder;
    }
    private GrammarBuilder Digits()
    {
        var builder = Builder();
        // Spoken digit words are intentionally favored over whole-number
        // dictation so e.g. zero nine five remains all three GT channels.
        builder.Append(new Choices("zero", "oh", "nought", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"), 3, 3);
        return builder;
    }

    private GrammarBuilder OutfitNumber()
    {
        var builder = Builder(); builder.Append(new Choices(OutfitNumbers())); return builder;
    }

    private GrammarBuilder RoomCode()
    {
        var spelled = Builder();
        spelled.Append(new Choices(RoomSymbols()), 1, 10);
        // Names embedded in alphanumeric codes are not ordinary dictation
        // sentences. Teach the reported JACE34 pronunciation explicitly while
        // keeping free-dictation room commands available for other word codes.
        var suffix = Builder(); suffix.Append(new Choices("zero", "oh", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"), 1, 6);
        var numbers = Builder(); numbers.Append(new Choices(OutfitNumbers()));
        var named = Builder(); named.Append("jace"); named.Append(new Choices(suffix, numbers), 0, 1);
        var code = Builder(); code.Append(new Choices(spelled, named));
        return code;
    }

    internal static string[] RoomSymbols() => Enumerable.Range('A', 26).Select(value => ((char)value).ToString())
        .Concat(new[] { "alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel", "india", "juliet", "kilo", "lima", "mike", "november", "oscar", "papa", "quebec", "romeo", "sierra", "tango", "uniform", "victor", "whiskey", "x ray", "yankee", "zulu", "zero", "oh", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" }).ToArray();

    internal static string[] OutfitNumbers()
    {
        string[] small = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
        string[] tens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };
        return Enumerable.Range(1, 99).Select(number => number < 20 ? small[number] : tens[number / 10] + (number % 10 == 0 ? "" : " " + small[number % 10])).ToArray();
    }

    private GrammarBuilder ColorCommand(string[] prefixes, bool wake)
    { var builder = CommandPrefix(prefixes, wake); builder.Append(Digits()); return builder; }
    private GrammarBuilder RoomCommand(string[] prefixes, bool wake)
    { var builder = CommandPrefix(prefixes, wake); builder.Append(RoomCode()); return builder; }
    private GrammarBuilder OutfitCommand(string[] prefixes, bool wake)
    { var builder = CommandPrefix(prefixes, wake); builder.Append(OutfitNumber()); return builder; }
    private GrammarBuilder NamedCommand(string[] prefixes, string[] names, bool wake)
    { var builder = CommandPrefix(prefixes, wake); builder.Append(new Choices(names)); return builder; }
    private GrammarBuilder DictatedCommand(string[] prefixes, bool wake)
    { var builder = CommandPrefix(prefixes, wake); builder.AppendDictation(); return builder; }
    private GrammarBuilder CommandPrefix(string[] prefixes, bool wake)
    { var builder = Builder(); if (wake) builder.Append("hey jace"); builder.Append(new Choices(prefixes)); return builder; }

    private Grammar? LoadPhrase(IEnumerable<string> phrases, string name, List<Grammar>? group = null, int priority = 2)
        => AddStructured(name, () => { var builder = Builder(); builder.Append(new Choices(phrases.ToArray())); return builder; }, group, priority);

    private Grammar? AddStructured(string name, Func<GrammarBuilder> create, List<Grammar>? group = null, int priority = 2)
    {
        try
        {
            var grammar = new Grammar(create()) { Name = name, Weight = 1f, Priority = priority, Enabled = group == null };
            _recognizer.LoadGrammar(grammar); group?.Add(grammar); return grammar;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or System.Runtime.InteropServices.COMException)
        {
            // Some installed Windows recognizers reject CFG dictation tails.
            // Keep the fallback and all other supported grammars operational.
            Console.WriteLine("Optional speech grammar unavailable (" + name + "): " + error.Message);
            return null;
        }
    }
}
