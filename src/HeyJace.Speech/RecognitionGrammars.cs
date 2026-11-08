using System.Globalization;
using System.Speech.Recognition;
using HeyJace.Core;

namespace HeyJace.Speech;

internal sealed class RecognitionGrammars
{
    private readonly SpeechRecognitionEngine _recognizer;
    private readonly CultureInfo _culture;
    private readonly List<Grammar> _activeCommands = new();
    private readonly List<Grammar> _colorAnswers = new(), _outfitAnswers = new(), _cosmeticAnswers = new(), _timeAnswers = new(), _roomAnswers = new();
    private readonly List<Grammar> _cosmeticGrammars = new();

    internal RecognitionGrammars(SpeechRecognitionEngine recognizer, CultureInfo culture)
    { _recognizer = recognizer; _culture = culture; }

    internal void Load()
    {
        LoadPhrase(new[] { "hey jace" }, "Wake phrase", priority: 4);
        string[] prefixes =
        {
            "join room", "join the room", "join code", "join room code", "set color", "set my color to", "change color", "change my color to", "change my colour to",
            "set time", "set the time to", "change the time to", "equip outfit", "equip outfit number", "equip", "put on", "wear", "unequip", "take off", "remove", "cancel"
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
        string[] outfits = { "equip outfit number", "equip outfit", "wear outfit", "put on outfit" };
        AddStructured("Wake outfit", () => OutfitCommand(outfits, true), priority: 8);
        AddStructured("Active outfit", () => OutfitCommand(outfits, false), _activeCommands, priority: 8);
        AddStructured("Outfit number", OutfitNumber, _outfitAnswers, priority: 9);

        string[] times = { "dawn", "morning", "sunrise", "day", "noon", "midday", "afternoon", "sunset", "evening", "night", "midnight", "auto", "normal", "default", "reset" };
        LoadPhrase(times, "Time answer", _timeAnswers);
        var timePrefixes = new[] { "change the time to", "set the time to", "change time to", "set time to" };
        AddStructured("Wake time", () => NamedCommand(timePrefixes, times, true));
        AddStructured("Active time", () => NamedCommand(timePrefixes, times, false), _activeCommands);
        // Dictation tails keep arbitrary codes/new names in the command grammar
        // instead of forcing the entire wake phrase through free dictation.
        AddStructured("Wake arguments", () => DictatedCommand(prefixes.Where(value => value != "cancel").ToArray(), true));
        AddStructured("Active arguments", () => DictatedCommand(prefixes.Where(value => value != "cancel").ToArray(), false), _activeCommands);
        _recognizer.LoadGrammar(new DictationGrammar { Name = "Fallback dictation", Weight = .7f });
        SetState(false, null);
    }

    internal void ReplaceCosmetics(string[] names)
    {
        // Caller has stopped recognition before modifying loaded grammars.
        foreach (Grammar grammar in _cosmeticGrammars)
        {
            _activeCommands.Remove(grammar); _cosmeticAnswers.Remove(grammar);
            _recognizer.UnloadGrammar(grammar);
        }
        _cosmeticGrammars.Clear();
        if (names.Length == 0) return;
        string[] prefixes = { "equip", "put on", "wear", "unequip", "take off", "remove" };
        Grammar? wake = AddStructured("Owned cosmetic wake names", () => NamedCommand(prefixes, names, true));
        Grammar? active = AddStructured("Owned cosmetic command names", () => NamedCommand(prefixes, names, false), _activeCommands);
        Grammar? answer = LoadPhrase(names, "Owned cosmetic answer names", _cosmeticAnswers);
        foreach (Grammar? grammar in new[] { wake, active, answer }) if (grammar != null) _cosmeticGrammars.Add(grammar);
    }

    internal void SetState(bool active, CommandKind? pending)
    {
        SetEnabled(_activeCommands, active);
        SetEnabled(_colorAnswers, active && pending == CommandKind.SetColor);
        SetEnabled(_outfitAnswers, active && pending == CommandKind.EquipOutfit);
        SetEnabled(_cosmeticAnswers, active && pending is CommandKind.Equip or CommandKind.Unequip);
        SetEnabled(_timeAnswers, active && pending == CommandKind.SetTime);
        SetEnabled(_roomAnswers, active && pending == CommandKind.JoinRoom);
    }

    private static void SetEnabled(IEnumerable<Grammar> grammars, bool enabled)
    { foreach (Grammar grammar in grammars) if (grammar.Enabled != enabled) grammar.Enabled = enabled; }

    private GrammarBuilder Builder() => new() { Culture = _culture };
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
